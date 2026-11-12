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
			refusal = Describe(diagnostic) + " could not be lowered into the action VM. The new body is not a single expression of int, bool, or string. Relaunching.";
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

			ExpressionSyntax? body = method.ExpressionBody?.Expression;
			if (body is null && method.Body is { Statements: [ReturnStatementSyntax returned] })
				body = returned.Expression;
			if (body is null)
			{
				refusal = Refuse(typeName, method.Identifier.ValueText);
				return false;
			}

			var buf = new Buf();
			if (!Write(body, names, args, buf, out var kind) || kind != ret)
			{
				refusal = Refuse(typeName, method.Identifier.ValueText);
				return false;
			}

			found.Add(new Body(typeName, method.Identifier.ValueText, ret, args, buf.Bytes.ToArray(), buf.Strings.ToArray()));
		}

		return true;
	}

	static string Refuse(string typeName, string method) =>
		typeName + "." + method + " could not be lowered into the action VM. The new body is not a single expression of int, bool, or string. Relaunching.";

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
	}
}
