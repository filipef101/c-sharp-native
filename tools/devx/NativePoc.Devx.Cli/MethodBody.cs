using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

internal static class MethodBody
{
	internal sealed record Body(string TypeName, string MethodName, byte ReturnKind, byte[] ArgKinds, byte[] Code, string[] Strings);

	internal static bool TryLower(string source, out Body[] bodies, out string refusal)
	{
		bodies = [];
		refusal = "";
		var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		foreach (var diagnostic in tree.GetDiagnostics())
		{
			if (diagnostic.Severity != DiagnosticSeverity.Error)
				continue;
			refusal = Describe(diagnostic) + " could not be lowered into the action VM. The new body is not locals, if, loops, try, and return of int, bool, or string. Relaunching.";
			return false;
		}

		var root = tree.GetCompilationUnitRoot();
		var space = NamespaceOf(root);
		var found = new List<Body>();
		foreach (var member in root.Members)
		{
			if (member is BaseNamespaceDeclarationSyntax nested)
			{
				space = nested.Name.ToString();
				foreach (var inner in nested.Members)
				{
					if (!TakeType(inner, space, found, out refusal))
						return false;
				}

				continue;
			}

			if (!TakeType(member, space, found, out refusal))
				return false;
		}

		if (found.Count == 0)
		{
			refusal = "The file has no public static method that can be lowered into the action VM. Relaunching.";
			return false;
		}

		bodies = found.ToArray();
		return true;
	}

	static bool TakeType(MemberDeclarationSyntax member, string space, List<Body> found, out string refusal)
	{
		refusal = "";
		if (member is not ClassDeclarationSyntax type)
			return true;
		var typeName = space.Length == 0 ? type.Identifier.ValueText : space + "." + type.Identifier.ValueText;
		foreach (var child in type.Members)
		{
			if (child is not MethodDeclarationSyntax method || !method.Modifiers.Any(SyntaxKind.PublicKeyword) || !method.Modifiers.Any(SyntaxKind.StaticKeyword))
				continue;
			if (method.TypeParameterList is not null || method.ParameterList.Parameters.Count > 8)
			{
				refusal = Refuse(typeName, method.Identifier.ValueText);
				return false;
			}

			if (!KindOf(method.ReturnType, out var ret))
			{
				refusal = Refuse(typeName, method.Identifier.ValueText);
				return false;
			}

			var args = new byte[method.ParameterList.Parameters.Count];
			var names = new string[args.Length];
			for (var index = 0; index < args.Length; index++)
			{
				var parameter = method.ParameterList.Parameters[index];
				if (!KindOf(parameter.Type, out var argKind) || argKind == 0)
				{
					refusal = Refuse(typeName, method.Identifier.ValueText);
					return false;
				}

				args[index] = argKind;
				names[index] = parameter.Identifier.ValueText;
			}

			var buf = new Buf();
			var nameList = new List<string>(names);
			var kindList = new List<byte>(args);
			ExpressionSyntax? body = method.ExpressionBody?.Expression;
			if (body is null && method.Body is { Statements: [ReturnStatementSyntax returned] })
				body = returned.Expression;
			if (body is not null)
			{
				if (!Write(body, names, args, buf, out var kind) || kind != ret)
				{
					refusal = Refuse(typeName, method.Identifier.ValueText);
					return false;
				}
			}
			else if (method.Body is not null)
			{
				if (!WriteBlock(method.Body, nameList, kindList, ret, buf, out var complete) || !complete)
				{
					refusal = Refuse(typeName, method.Identifier.ValueText);
					return false;
				}
			}
			else
			{
				refusal = Refuse(typeName, method.Identifier.ValueText);
				return false;
			}

			found.Add(new Body(typeName, method.Identifier.ValueText, ret, args, buf.Bytes.ToArray(), buf.Strings.ToArray()));
		}

		return true;
	}

	static string Refuse(string typeName, string method) =>
		typeName + "." + method + " could not be lowered into the action VM. The new body is not locals, if, loops, try, and return of int, bool, or string. Relaunching.";

	static string Describe(Diagnostic diagnostic)
	{
		var span = diagnostic.Location.GetLineSpan();
		var line = span.StartLinePosition.Line + 1;
		return "line " + line.ToString(CultureInfo.InvariantCulture);
	}

	static string NamespaceOf(CompilationUnitSyntax root)
	{
		foreach (var member in root.Members)
		{
			if (member is FileScopedNamespaceDeclarationSyntax file)
				return file.Name.ToString();
			if (member is NamespaceDeclarationSyntax nested)
				return nested.Name.ToString();
		}

		return "";
	}

	static bool KindOf(TypeSyntax? type, out byte kind)
	{
		kind = 0;
		if (type is null)
			return true;
		switch (type.ToString())
		{
			case "void":
				return true;
			case "int" or "Int32" or "System.Int32":
				kind = RecordProgram.VmKindInt;
				return true;
			case "bool" or "Boolean" or "System.Boolean":
				kind = RecordProgram.VmKindBool;
				return true;
			case "string" or "String" or "System.String":
				kind = RecordProgram.VmKindStr;
				return true;
			default:
				return false;
		}
	}

	static bool WriteBlock(BlockSyntax block, List<string> names, List<byte> kinds, byte ret, Buf buf, out bool complete)
	{
		var exits = new List<int>();
		complete = false;
		foreach (var statement in block.Statements)
		{
			if (complete)
				return false;
			if (!WriteStatement(statement, names, kinds, ret, buf, exits, 0, ref complete))
				return false;
		}

		foreach (var exit in exits)
			buf.Patch(exit);
		return true;
	}

	static bool WriteStatement(StatementSyntax statement, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		switch (statement)
		{
			case BlockSyntax nested:
				foreach (var child in nested.Statements)
				{
					if (complete)
						return false;
					if (!WriteStatement(child, names, kinds, ret, buf, exits, tries, ref complete))
						return false;
				}

				return true;
			case LocalDeclarationStatementSyntax local:
				return Declare(local, names, kinds, buf);
			case IfStatementSyntax branch:
				return WriteIf(branch, names, kinds, ret, buf, exits, tries, ref complete);
			case WhileStatementSyntax loop:
				return WriteWhile(loop, names, kinds, ret, buf, exits, tries);
			case ForStatementSyntax loop:
				return WriteFor(loop, names, kinds, ret, buf, exits, tries);
			case TryStatementSyntax tried:
				return WriteTry(tried, names, kinds, ret, buf, exits, tries, ref complete);
			case ReturnStatementSyntax returned:
				if (returned.Expression is null || !Emit(returned.Expression, names, kinds, buf, out var kind) || kind != ret)
					return false;
				exits.Add(buf.Reserve(tries > 0 ? RecordProgram.VmLeaveTo : RecordProgram.VmJump));
				complete = true;
				return true;
			case ExpressionStatementSyntax expression:
				return WriteExpression(expression.Expression, names, kinds, buf);
			default:
				return false;
		}
	}

	static bool Declare(LocalDeclarationStatementSyntax local, List<string> names, List<byte> kinds, Buf buf)
	{
		if (local.Declaration.Variables.Count != 1 || names.Count >= 8)
			return false;
		var variable = local.Declaration.Variables[0];
		if (variable.Initializer is null || names.Contains(variable.Identifier.ValueText))
			return false;
		var declared = local.Declaration.Type?.ToString() ?? "";
		if (declared == "var")
		{
			if (!Emit(variable.Initializer.Value, names, kinds, buf, out var inferred) || inferred == 0)
				return false;
			Store(buf, inferred, names.Count);
			names.Add(variable.Identifier.ValueText);
			kinds.Add(inferred);
			return true;
		}

		if (!KindOf(local.Declaration.Type, out var kind) || kind == 0 || !Emit(variable.Initializer.Value, names, kinds, buf, out var got) || got != kind)
			return false;
		Store(buf, kind, names.Count);
		names.Add(variable.Identifier.ValueText);
		kinds.Add(kind);
		return true;
	}

	static bool WriteIf(IfStatementSyntax branch, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		if (!Emit(branch.Condition, names, kinds, buf, out var cond) || cond != RecordProgram.VmKindBool)
			return false;
		var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
		var thenDone = false;
		if (!WriteStatement(branch.Statement, names, kinds, ret, buf, exits, tries, ref thenDone))
			return false;
		if (branch.Else is null)
		{
			buf.Patch(skip);
			return true;
		}

		var end = buf.Reserve(RecordProgram.VmJump);
		buf.Patch(skip);
		var elseDone = false;
		if (!WriteStatement(branch.Else.Statement, names, kinds, ret, buf, exits, tries, ref elseDone))
			return false;
		buf.Patch(end);
		complete = thenDone && elseDone;
		return true;
	}

	static bool WriteWhile(WhileStatementSyntax loop, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries)
	{
		var start = buf.Bytes.Count;
		if (!Emit(loop.Condition, names, kinds, buf, out var cond) || cond != RecordProgram.VmKindBool)
			return false;
		var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
		var ignored = false;
		if (!WriteStatement(loop.Statement, names, kinds, ret, buf, exits, tries, ref ignored))
			return false;
		buf.Jump(start);
		buf.Patch(skip);
		return true;
	}

	static bool WriteFor(ForStatementSyntax loop, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries)
	{
		if (loop.Initializers.Count != 0 || loop.Declaration is not { Variables: [{ Initializer: not null } only] } || loop.Condition is null || loop.Incrementors.Count != 1)
			return false;
		var declared = loop.Declaration.Type?.ToString() ?? "";
		byte kind;
		if (declared == "var")
		{
			if (!Emit(only.Initializer!.Value, names, kinds, buf, out kind) || kind == 0)
				return false;
		}
		else if (!KindOf(loop.Declaration.Type, out kind) || kind == 0 || !Emit(only.Initializer!.Value, names, kinds, buf, out var got) || got != kind)
			return false;
		if (names.Count >= 8 || names.Contains(only.Identifier.ValueText))
			return false;
		Store(buf, kind, names.Count);
		names.Add(only.Identifier.ValueText);
		kinds.Add(kind);
		var header = SyntaxFactory.WhileStatement(loop.Condition, WithStep(loop.Statement, loop.Incrementors[0]));
		return WriteWhile(header, names, kinds, ret, buf, exits, tries);
	}

	static bool WriteTry(TryStatementSyntax statement, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		foreach (var clause in statement.Catches)
		{
			if (clause.Filter is not null)
				return false;
			var bound = clause.Declaration?.Identifier.ValueText ?? "";
			if (bound.Length > 0 && clause.Block.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name => name.Identifier.ValueText == bound))
				return false;
		}

		if (statement.Finally is not null && statement.Finally.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
			return false;
		return WriteTryRegion(statement.Block, statement.Catches, statement.Finally, names, kinds, ret, buf, exits, tries, ref complete);
	}

	static bool WriteTryRegion(BlockSyntax body, IReadOnlyList<CatchClauseSyntax> catches, FinallyClauseSyntax? fin, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		if (catches.Count > 1)
		{
			var inner = catches.Take(catches.Count - 1).ToArray();
			bool Inner(Buf target, ref bool done) => WriteTryRegion(body, inner, null, names, kinds, ret, target, exits, tries, ref done);
			return WriteOneTry(Inner, catches[^1], fin, names, kinds, ret, buf, exits, tries, ref complete);
		}

		bool Direct(Buf target, ref bool done) => WriteStatements(body.Statements, names, kinds, ret, target, exits, tries + 1, ref done);
		return WriteOneTry(Direct, catches.Count == 0 ? null : catches[0], fin, names, kinds, ret, buf, exits, tries, ref complete);
	}

	static bool WriteStatements(IEnumerable<StatementSyntax> statements, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		var mark = names.Count;
		foreach (var statement in statements)
		{
			if (complete)
				return false;
			if (!WriteStatement(statement, names, kinds, ret, buf, exits, tries, ref complete))
				return false;
		}

		while (names.Count > mark)
		{
			names.RemoveAt(names.Count - 1);
			kinds.RemoveAt(kinds.Count - 1);
		}

		return true;
	}

	static bool WriteOneTry(TryBody writeBody, CatchClauseSyntax? caught, FinallyClauseSyntax? fin, List<string> names, List<byte> kinds, byte ret, Buf buf, List<int> exits, int tries, ref bool complete)
	{
		buf.Op(RecordProgram.VmTry);
		var catchAt = buf.Bytes.Count;
		buf.Raw(0);
		buf.Raw(0);
		var finallyAt = buf.Bytes.Count;
		buf.Raw(0);
		buf.Raw(0);
		var endAt = buf.Bytes.Count;
		buf.Raw(0);
		buf.Raw(0);
		var type = caught?.Declaration is null
			? caught is null ? 0xFFFE : 0xFFFF
			: buf.Intern(caught.Declaration.Type.ToString());
		if (type > ushort.MaxValue)
			return false;
		buf.U16(type);
		var origin = buf.Bytes.Count;
		var bodyDone = false;
		if (!writeBody(buf, ref bodyDone))
			return false;
		if (!bodyDone)
			buf.Op(RecordProgram.VmLeave);
		var catchIp = buf.Bytes.Count;
		var catchDone = caught is null;
		if (caught is not null)
		{
			catchDone = false;
			if (!WriteStatements(caught.Block.Statements, names, kinds, ret, buf, exits, tries + 1, ref catchDone))
				return false;
			if (!catchDone)
				buf.Op(RecordProgram.VmLeave);
		}

		var finallyIp = buf.Bytes.Count;
		if (fin is not null)
		{
			var ignored = false;
			if (!WriteStatements(fin.Block.Statements, names, kinds, ret, buf, exits, tries + 1, ref ignored))
				return false;
			buf.Op(RecordProgram.VmEndFinally);
		}

		var endIp = buf.Bytes.Count;
		if (!buf.PatchTo(catchAt, caught is null ? short.MinValue : catchIp - origin))
			return false;
		if (!buf.PatchTo(finallyAt, fin is null ? short.MinValue : finallyIp - origin))
			return false;
		if (!buf.PatchTo(endAt, endIp - origin))
			return false;
		complete = bodyDone && catchDone;
		return true;
	}

	delegate bool TryBody(Buf buf, ref bool complete);

	static StatementSyntax WithStep(StatementSyntax body, ExpressionSyntax step)
	{
		var extra = SyntaxFactory.ExpressionStatement(step);
		if (body is BlockSyntax block)
		{
			var statements = block.Statements.Add(extra);
			return block.WithStatements(statements);
		}

		return SyntaxFactory.Block(body, extra);
	}

	static bool WriteExpression(ExpressionSyntax expression, List<string> names, List<byte> kinds, Buf buf)
	{
		if (expression is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left is IdentifierNameSyntax target)
		{
			var slot = names.IndexOf(target.Identifier.ValueText);
			if (slot < 0 || !Emit(assignment.Right, names, kinds, buf, out var kind) || kind != kinds[slot])
				return false;
			Store(buf, kind, slot);
			return true;
		}

		if (expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.PostIncrementExpression) && postfix.Operand is IdentifierNameSyntax name)
		{
			var slot = names.IndexOf(name.Identifier.ValueText);
			if (slot < 0 || kinds[slot] != RecordProgram.VmKindInt)
				return false;
			buf.Op(RecordProgram.VmIncLocal);
			buf.Raw((byte)slot);
			buf.Raw(RecordProgram.VmIncPost);
			buf.Op(RecordProgram.VmPop);
			return true;
		}

		return false;
	}

	static bool Emit(ExpressionSyntax expression, List<string> names, List<byte> kinds, Buf buf, out byte kind) =>
		Write(expression, names.ToArray(), kinds.ToArray(), buf, out kind);

	static void Store(Buf buf, byte kind, int slot)
	{
		buf.Op(RecordProgram.VmStoreLocal);
		buf.Raw(kind);
		buf.Raw((byte)slot);
	}

	static bool Write(ExpressionSyntax expression, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		if (expression is ParenthesizedExpressionSyntax paren)
			return Write(paren.Expression, names, kinds, buf, out kind);
		if (expression is LiteralExpressionSyntax literal)
			return Literal(literal, buf, out kind);
		if (expression is IdentifierNameSyntax name)
			return Load(name.Identifier.ValueText, names, kinds, buf, out kind);
		if (expression is PrefixUnaryExpressionSyntax unary)
			return Unary(unary, names, kinds, buf, out kind);
		if (expression is BinaryExpressionSyntax binary)
			return Binary(binary, names, kinds, buf, out kind);
		if (expression is ConditionalExpressionSyntax choice)
			return Choice(choice, names, kinds, buf, out kind);
		if (expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Length")
			return Length(member.Expression, names, kinds, buf, out kind);
		if (expression is InvocationExpressionSyntax invocation)
			return Call(invocation, names, kinds, buf, out kind);
		return false;
	}

	static bool Literal(LiteralExpressionSyntax literal, Buf buf, out byte kind)
	{
		kind = 0;
		if (literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int number)
		{
			buf.Op(RecordProgram.VmLitI);
			buf.I32(number);
			kind = RecordProgram.VmKindInt;
			return true;
		}

		if (literal.IsKind(SyntaxKind.StringLiteralExpression))
		{
			buf.Op(RecordProgram.VmLitS);
			buf.U16(buf.Intern(literal.Token.ValueText));
			kind = RecordProgram.VmKindStr;
			return true;
		}

		if (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression))
		{
			buf.Op(RecordProgram.VmLitB);
			buf.Raw(literal.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
			kind = RecordProgram.VmKindBool;
			return true;
		}

		return false;
	}

	static bool Load(string name, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		for (var index = 0; index < names.Length; index++)
		{
			if (names[index] != name)
				continue;
			buf.Op(RecordProgram.VmLoadLocal);
			buf.Raw(kinds[index]);
			buf.Raw((byte)index);
			kind = kinds[index];
			return true;
		}

		return false;
	}

	static bool Unary(PrefixUnaryExpressionSyntax unary, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		if (!Write(unary.Operand, names, kinds, buf, out var operand))
			return false;
		if (unary.IsKind(SyntaxKind.UnaryMinusExpression) && operand == RecordProgram.VmKindInt)
		{
			buf.Op(RecordProgram.VmNeg);
			kind = RecordProgram.VmKindInt;
			return true;
		}

		if (unary.IsKind(SyntaxKind.LogicalNotExpression) && operand == RecordProgram.VmKindBool)
		{
			buf.Op(RecordProgram.VmNot);
			kind = RecordProgram.VmKindBool;
			return true;
		}

		return false;
	}

	static bool Binary(BinaryExpressionSyntax binary, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		var op = binary.Kind() switch
		{
			SyntaxKind.AddExpression => RecordProgram.VmAdd,
			SyntaxKind.SubtractExpression => RecordProgram.VmSub,
			SyntaxKind.MultiplyExpression => RecordProgram.VmMul,
			SyntaxKind.DivideExpression => RecordProgram.VmDiv,
			SyntaxKind.ModuloExpression => RecordProgram.VmRem,
			SyntaxKind.EqualsExpression => RecordProgram.VmEq,
			SyntaxKind.NotEqualsExpression => RecordProgram.VmNe,
			SyntaxKind.LessThanExpression => RecordProgram.VmLt,
			SyntaxKind.LessThanOrEqualExpression => RecordProgram.VmLe,
			SyntaxKind.GreaterThanExpression => RecordProgram.VmGt,
			SyntaxKind.GreaterThanOrEqualExpression => RecordProgram.VmGe,
			_ => (byte)0,
		};
		if (op == 0 || !Write(binary.Left, names, kinds, buf, out var left) || !Write(binary.Right, names, kinds, buf, out _))
			return false;
		buf.Op(op);
		kind = op is RecordProgram.VmEq or RecordProgram.VmNe or RecordProgram.VmLt or RecordProgram.VmLe or RecordProgram.VmGt or RecordProgram.VmGe
			? RecordProgram.VmKindBool
			: left;
		return true;
	}

	static bool Choice(ConditionalExpressionSyntax choice, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		if (!Write(choice.Condition, names, kinds, buf, out var cond) || cond != RecordProgram.VmKindBool)
			return false;
		var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
		if (!Write(choice.WhenTrue, names, kinds, buf, out var thenKind))
			return false;
		var end = buf.Reserve(RecordProgram.VmJump);
		buf.Patch(skip);
		if (!Write(choice.WhenFalse, names, kinds, buf, out var elseKind) || thenKind != elseKind)
			return false;
		buf.Patch(end);
		kind = thenKind;
		return true;
	}

	static bool Length(ExpressionSyntax receiver, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		if (!Write(receiver, names, kinds, buf, out var got) || got != RecordProgram.VmKindStr)
			return false;
		buf.Op(RecordProgram.VmCall);
		buf.U16(RecordProgram.VmLength);
		buf.Raw(1);
		kind = RecordProgram.VmKindInt;
		return true;
	}

	static bool Call(InvocationExpressionSyntax invocation, string[] names, byte[] kinds, Buf buf, out byte kind)
	{
		kind = 0;
		if (invocation.Expression is not MemberAccessExpressionSyntax member || invocation.ArgumentList.Arguments.Count != 0)
			return false;
		if (!Write(member.Expression, names, kinds, buf, out var receiver) || receiver != RecordProgram.VmKindStr)
			return false;
		var id = member.Name.Identifier.ValueText switch
		{
			"Trim" => RecordProgram.VmTrim,
			"ToUpper" => RecordProgram.VmUpper,
			"ToLower" => RecordProgram.VmLower,
			_ => (ushort)0,
		};
		if (id == 0)
			return false;
		buf.Op(RecordProgram.VmCall);
		buf.U16(id);
		buf.Raw(1);
		kind = RecordProgram.VmKindStr;
		return true;
	}

	sealed class Buf
	{
		public readonly List<byte> Bytes = new();

		public readonly List<string> Strings = new();

		public void Op(byte op) => Bytes.Add(op);

		public void Raw(byte value) => Bytes.Add(value);

		public void U16(int value)
		{
			Bytes.Add((byte)value);
			Bytes.Add((byte)(value >> 8));
		}

		public void I32(int value)
		{
			Bytes.Add((byte)value);
			Bytes.Add((byte)(value >> 8));
			Bytes.Add((byte)(value >> 16));
			Bytes.Add((byte)(value >> 24));
		}

		public int Intern(string text)
		{
			var at = Strings.IndexOf(text);
			if (at >= 0)
				return at;
			Strings.Add(text);
			return Strings.Count - 1;
		}

		public int Reserve(byte op)
		{
			Op(op);
			var at = Bytes.Count;
			Bytes.Add(0);
			Bytes.Add(0);
			return at;
		}

		public void Patch(int at)
		{
			var relative = Bytes.Count - (at + 2);
			Bytes[at] = (byte)relative;
			Bytes[at + 1] = (byte)(relative >> 8);
		}

		public bool PatchTo(int at, int relative)
		{
			if (relative < short.MinValue || relative > short.MaxValue)
				return false;
			Bytes[at] = (byte)relative;
			Bytes[at + 1] = (byte)(relative >> 8);
			return true;
		}

		public void Jump(int target)
		{
			Op(RecordProgram.VmJump);
			var relative = target - (Bytes.Count + 2);
			Bytes.Add((byte)relative);
			Bytes.Add((byte)(relative >> 8));
		}
	}
}
