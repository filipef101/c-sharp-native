using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

public static partial class SubsetLowerer
{
	sealed partial class Lower
	{
		const string ActionLimit = "A screen action can update state, branch on it, change a list, or call the platform.";

		bool Statement(StatementSyntax statement, string parameter, List<byte> script)
		{
			switch (statement)
			{
				case ExpressionStatementSyntax expression:
					return Action(expression.Expression, parameter, script);
				case LocalDeclarationStatementSyntax local:
					return LocalDecl(local, script);
				case IfStatementSyntax branch:
					return If(branch, parameter, script);
				default:
					return Fail(BadLambda, ActionLimit, statement);
			}
		}

		bool If(IfStatementSyntax branch, string parameter, List<byte> script)
		{
			var condition = new List<byte>();
			if (!WriteCond(branch.Condition, condition))
				return Fail(BadLambda, ActionLimit, branch.Condition);
			var thenScript = new List<byte>();
			if (!Branch(branch.Statement, parameter, thenScript))
				return false;
			var elseScript = new List<byte>();
			if (branch.Else is not null && !Branch(branch.Else.Statement, parameter, elseScript))
				return false;
			if (thenScript.Count > ushort.MaxValue || elseScript.Count > ushort.MaxValue)
				return Fail(BadLambda, ActionLimit, branch);
			script.Add(RecordProgram.ActIf);
			script.AddRange(condition);
			U16(script, thenScript.Count);
			script.AddRange(thenScript);
			U16(script, elseScript.Count);
			script.AddRange(elseScript);
			return true;
		}

		bool Branch(StatementSyntax statement, string parameter, List<byte> script)
		{
			if (statement is not BlockSyntax block)
				return Statement(statement, parameter, script);
			foreach (var item in block.Statements)
			{
				if (!Statement(item, parameter, script))
					return false;
			}

			return true;
		}

		bool LocalDecl(LocalDeclarationStatementSyntax local, List<byte> script)
		{
			if (local.Declaration.Variables.Count != 1)
				return Fail(BadLambda, ActionLimit, local);
			var variable = local.Declaration.Variables[0];
			if (variable.Initializer is null || _locals is null || _locals.ContainsKey(variable.Identifier.ValueText) || _localSlots > 7)
				return Fail(BadLambda, ActionLimit, variable);
			var slot = _localSlots;
			var init = variable.Initializer.Value;
			if (WriteIntExpr(init, out var intExpr))
			{
				script.Add(RecordProgram.ActLocal);
				script.Add(slot);
				script.Add(RecordProgram.LocalInt);
				script.AddRange(intExpr);
				_locals[variable.Identifier.ValueText] = (slot, ValueKind.Int);
				_localSlots++;
				return true;
			}

			if (WriteStrCat(init, out var strExpr, out var strCount))
			{
				script.Add(RecordProgram.ActLocal);
				script.Add(slot);
				script.Add(RecordProgram.LocalStr);
				script.Add(strCount);
				script.AddRange(strExpr);
				_locals[variable.Identifier.ValueText] = (slot, ValueKind.Str);
				_localSlots++;
				return true;
			}

			var condition = new List<byte>();
			if (WriteCond(init, condition))
			{
				script.Add(RecordProgram.ActLocal);
				script.Add(slot);
				script.Add(RecordProgram.LocalBool);
				script.AddRange(condition);
				_locals[variable.Identifier.ValueText] = (slot, ValueKind.Bool);
				_localSlots++;
				return true;
			}

			return Fail(BadLambda, ActionLimit, init);
		}

		bool Compound(AssignmentExpressionSyntax assignment, List<byte> script)
		{
			if (assignment.Left is not MemberAccessExpressionSyntax member || !Member(member, out var field))
				return Fail(BadLambda, ActionLimit, assignment);
			var subtract = assignment.IsKind(SyntaxKind.SubtractAssignmentExpression);
			if (field.Kind == ValueKind.Int)
			{
				var delta = new List<byte>();
				if (subtract)
				{
					delta.Add(RecordProgram.IntExprSub);
					delta.Add(RecordProgram.IntExprLit);
					WriteI32(delta, 0);
				}

				if (!WriteIntExprInto(Bare(assignment.Right), delta))
					return Fail(BadLambda, ActionLimit, assignment.Right);
				script.Add(RecordProgram.ActAddInt);
				U16(script, _code.Intern(field.Name));
				script.AddRange(delta);
				return true;
			}

			if (!subtract && field.Kind == ValueKind.Str)
			{
				var parts = new List<byte> { RecordProgram.StrProp };
				U16(parts, _code.Intern(field.Name));
				var count = 1;
				if (!WriteStrParts(Bare(assignment.Right), parts, ref count))
					return Fail(BadLambda, ActionLimit, assignment.Right);
				script.Add(RecordProgram.ActSetStrCat);
				U16(script, _code.Intern(field.Name));
				script.Add((byte)count);
				script.AddRange(parts);
				return true;
			}

			return Fail(BadLambda, ActionLimit, assignment);
		}

		bool Decrement(ExpressionSyntax operand, List<byte> script)
		{
			if (operand is not MemberAccessExpressionSyntax member || !Member(member, out var field) || field.Kind != ValueKind.Int)
				return Fail(BadLambda, ActionLimit, operand);
			script.Add(RecordProgram.ActAddInt);
			U16(script, _code.Intern(field.Name));
			script.Add(RecordProgram.IntExprLit);
			WriteI32(script, -1);
			return true;
		}

		bool Ternary(Field field, ConditionalExpressionSyntax choice, List<byte> script)
		{
			var condition = new List<byte>();
			if (!WriteCond(choice.Condition, condition))
				return Fail(BadLambda, ActionLimit, choice.Condition);
			var thenScript = new List<byte>();
			var elseScript = new List<byte>();
			if (!Store(field, choice.WhenTrue, thenScript) || !Store(field, choice.WhenFalse, elseScript))
				return false;
			if (thenScript.Count > ushort.MaxValue || elseScript.Count > ushort.MaxValue)
				return Fail(BadLambda, ActionLimit, choice);
			script.Add(RecordProgram.ActIf);
			script.AddRange(condition);
			U16(script, thenScript.Count);
			script.AddRange(thenScript);
			U16(script, elseScript.Count);
			script.AddRange(elseScript);
			return true;
		}

		bool Store(Field field, ExpressionSyntax expression, List<byte> script)
		{
			expression = Bare(expression);
			if (field.Kind == ValueKind.Str && expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				Act(script, RecordProgram.ActSetStrLit, _code.Intern(field.Name));
				U16(script, _code.Intern(literal.Token.ValueText));
				return true;
			}

			if (field.Kind == ValueKind.Bool && expression is LiteralExpressionSyntax flag && (flag.IsKind(SyntaxKind.TrueLiteralExpression) || flag.IsKind(SyntaxKind.FalseLiteralExpression)))
			{
				Act(script, RecordProgram.ActSetBoolLit, _code.Intern(field.Name));
				script.Add(flag.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
				return true;
			}

			if (field.Kind == ValueKind.Int && WriteIntExpr(expression, out var intExpr))
			{
				script.Add(RecordProgram.ActSetInt);
				U16(script, _code.Intern(field.Name));
				script.AddRange(intExpr);
				return true;
			}

			if (field.Kind == ValueKind.Str && WriteStrCat(expression, out var strExpr, out var strCount))
			{
				script.Add(RecordProgram.ActSetStrCat);
				U16(script, _code.Intern(field.Name));
				script.Add(strCount);
				script.AddRange(strExpr);
				return true;
			}

			if (field.Kind == ValueKind.Bool && expression is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
				return WriteNot(field, negation.Operand, script);
			var copied = new List<byte>();
			if (field.Kind == ValueKind.Bool && BoolSource(expression, copied, field.Name))
			{
				script.AddRange(copied);
				return true;
			}

			return Fail(BadLambda, ActionLimit, expression);
		}

		bool WriteNot(Field field, ExpressionSyntax operand, List<byte> script)
		{
			operand = Bare(operand);
			if (operand is MemberAccessExpressionSyntax member && Member(member, out var source) && source.Kind == ValueKind.Bool)
			{
				script.Add(RecordProgram.ActNotBool);
				U16(script, _code.Intern(field.Name));
				U16(script, _code.Intern(source.Name));
				return true;
			}

			if (operand is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Bool, out var slot))
			{
				script.Add(RecordProgram.ActNotSlot);
				U16(script, _code.Intern(field.Name));
				script.Add(slot);
				return true;
			}

			return Fail(BadLambda, ActionLimit, operand);
		}

		bool BoolSource(ExpressionSyntax expression, List<byte> script, string dest)
		{
			expression = Bare(expression);
			if (expression is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Bool, out var slot))
			{
				script.Add(RecordProgram.ActSetBoolSlot);
				U16(script, _code.Intern(dest));
				script.Add(slot);
				return true;
			}

			if (expression is not MemberAccessExpressionSyntax member || !Member(member, out var source) || source.Kind != ValueKind.Bool)
				return false;
			var condition = new List<byte> { RecordProgram.CondBool };
			U16(condition, _code.Intern(source.Name));
			var thenScript = new List<byte>();
			Act(thenScript, RecordProgram.ActSetBoolLit, _code.Intern(dest));
			thenScript.Add(1);
			var elseScript = new List<byte>();
			Act(elseScript, RecordProgram.ActSetBoolLit, _code.Intern(dest));
			elseScript.Add(0);
			script.Add(RecordProgram.ActIf);
			script.AddRange(condition);
			U16(script, thenScript.Count);
			script.AddRange(thenScript);
			U16(script, elseScript.Count);
			script.AddRange(elseScript);
			return true;
		}

		bool WriteIntExpr(ExpressionSyntax expression, out List<byte> bytes)
		{
			bytes = new List<byte>();
			return WriteIntExprInto(Bare(expression), bytes);
		}

		bool WriteIntExprInto(ExpressionSyntax expression, List<byte> script)
		{
			expression = Bare(expression);
			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int number)
			{
				script.Add(RecordProgram.IntExprLit);
				WriteI32(script, number);
				return true;
			}

			if (expression is PrefixUnaryExpressionSyntax minus && minus.IsKind(SyntaxKind.UnaryMinusExpression))
			{
				if (Bare(minus.Operand) is LiteralExpressionSyntax inner && inner.IsKind(SyntaxKind.NumericLiteralExpression) && inner.Token.Value is int negated)
				{
					script.Add(RecordProgram.IntExprLit);
					WriteI32(script, -negated);
					return true;
				}

				script.Add(RecordProgram.IntExprSub);
				script.Add(RecordProgram.IntExprLit);
				WriteI32(script, 0);
				return WriteIntExprInto(minus.Operand, script);
			}

			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Int)
			{
				script.Add(RecordProgram.IntExprProp);
				U16(script, _code.Intern(field.Name));
				return true;
			}

			if (expression is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Int, out var slot))
			{
				script.Add(RecordProgram.IntExprLocal);
				script.Add(slot);
				return true;
			}

			if (expression is BinaryExpressionSyntax binary && (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression)))
			{
				var left = new List<byte>();
				var right = new List<byte>();
				if (!WriteIntExprInto(binary.Left, left) || !WriteIntExprInto(binary.Right, right))
					return false;
				script.Add(binary.IsKind(SyntaxKind.AddExpression) ? RecordProgram.IntExprAdd : RecordProgram.IntExprSub);
				script.AddRange(left);
				script.AddRange(right);
				return true;
			}

			return false;
		}

		bool WriteStrCat(ExpressionSyntax expression, out List<byte> bytes, out byte count)
		{
			bytes = new List<byte>();
			var parts = 0;
			if (!WriteStrParts(Bare(expression), bytes, ref parts) || parts is < 1 or > 255)
			{
				count = 0;
				return false;
			}

			count = (byte)parts;
			return true;
		}

		bool WriteStrParts(ExpressionSyntax expression, List<byte> parts, ref int count)
		{
			expression = Bare(expression);
			if (expression is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression))
			{
				var left = new List<byte>();
				var right = new List<byte>();
				if (WriteIntExprInto(Bare(add.Left), left) && WriteIntExprInto(Bare(add.Right), right))
					return false;
				return WriteStrParts(add.Left, parts, ref count) && WriteStrParts(add.Right, parts, ref count);
			}

			if (expression is InterpolatedStringExpressionSyntax interpolated)
			{
				foreach (var content in interpolated.Contents)
				{
					if (content is InterpolatedStringTextSyntax text)
					{
						if (!AddPart(parts, ref count, RecordProgram.StrLit, _code.Intern(text.TextToken.ValueText)))
							return false;
						continue;
					}

					if (content is InterpolationSyntax hole)
					{
						if (!WriteStrParts(hole.Expression, parts, ref count))
							return false;
						continue;
					}

					return false;
				}

				return count > 0;
			}

			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				return AddPart(parts, ref count, RecordProgram.StrLit, _code.Intern(literal.Token.ValueText));
			if (expression is LiteralExpressionSyntax number && number.IsKind(SyntaxKind.NumericLiteralExpression))
				return AddPart(parts, ref count, RecordProgram.StrLit, _code.Intern(number.Token.ValueText));
			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field))
			{
				if (field.Kind == ValueKind.Str)
					return AddPart(parts, ref count, RecordProgram.StrProp, _code.Intern(field.Name));
				if (field.Kind == ValueKind.Int)
					return AddPart(parts, ref count, RecordProgram.StrInt, _code.Intern(field.Name));
			}

			if (expression is IdentifierNameSyntax local)
			{
				if (Local(local.Identifier.ValueText, ValueKind.Str, out var strSlot))
					return AddSlotPart(parts, ref count, RecordProgram.StrLocal, strSlot);
				if (Local(local.Identifier.ValueText, ValueKind.Int, out var intSlot))
					return AddSlotPart(parts, ref count, RecordProgram.StrIntLocal, intSlot);
			}

			return false;
		}

		static bool AddPart(List<byte> parts, ref int count, byte kind, int index)
		{
			if (count >= 255)
				return false;
			parts.Add(kind);
			U16(parts, index);
			count++;
			return true;
		}

		static bool AddSlotPart(List<byte> parts, ref int count, byte kind, byte slot)
		{
			if (count >= 255)
				return false;
			parts.Add(kind);
			U16(parts, slot);
			count++;
			return true;
		}

		bool WriteCond(ExpressionSyntax expression, List<byte> script)
		{
			expression = Bare(expression);
			if (expression is LiteralExpressionSyntax flag && (flag.IsKind(SyntaxKind.TrueLiteralExpression) || flag.IsKind(SyntaxKind.FalseLiteralExpression)))
			{
				script.Add(RecordProgram.CondLit);
				script.Add(flag.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
				return true;
			}

			if (expression is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
			{
				var operand = Bare(negation.Operand);
				if (operand is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Bool)
				{
					script.Add(RecordProgram.CondNotBool);
					U16(script, _code.Intern(field.Name));
					return true;
				}

				if (operand is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Bool, out var slot))
				{
					script.Add(RecordProgram.CondNotLocal);
					script.Add(slot);
					return true;
				}

				return false;
			}

			if (expression is MemberAccessExpressionSyntax property && Member(property, out var boolean) && boolean.Kind == ValueKind.Bool)
			{
				script.Add(RecordProgram.CondBool);
				U16(script, _code.Intern(boolean.Name));
				return true;
			}

			if (expression is IdentifierNameSyntax name && Local(name.Identifier.ValueText, ValueKind.Bool, out var boolSlot))
			{
				script.Add(RecordProgram.CondLocal);
				script.Add(boolSlot);
				return true;
			}

			if (expression is not BinaryExpressionSyntax binary)
				return false;
			if (binary.IsKind(SyntaxKind.EqualsExpression) && LengthOf(binary, out var empty))
			{
				script.Add(RecordProgram.CondEmpty);
				U16(script, _code.Intern(empty));
				return true;
			}

			if (binary.IsKind(SyntaxKind.NotEqualsExpression) && LengthOf(binary, out var present))
			{
				script.Add(RecordProgram.CondPresent);
				U16(script, _code.Intern(present));
				return true;
			}

			if (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))
			{
				var equal = binary.IsKind(SyntaxKind.EqualsExpression);
				if (Bare(binary.Left) is MemberAccessExpressionSyntax left && Member(left, out var field) && Bare(binary.Right) is LiteralExpressionSyntax literal)
				{
					if (field.Kind == ValueKind.Str && literal.IsKind(SyntaxKind.StringLiteralExpression))
					{
						script.Add(equal ? RecordProgram.CondStrEq : RecordProgram.CondStrNe);
						U16(script, _code.Intern(field.Name));
						U16(script, _code.Intern(literal.Token.ValueText));
						return true;
					}

					if (field.Kind == ValueKind.Int && literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int number)
					{
						script.Add(equal ? RecordProgram.CondIntEq : RecordProgram.CondIntNe);
						U16(script, _code.Intern(field.Name));
						WriteI32(script, number);
						return true;
					}
				}

				if (Bare(binary.Left) is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Int, out var slot) && equal && Bare(binary.Right) is LiteralExpressionSyntax right && right.IsKind(SyntaxKind.NumericLiteralExpression) && right.Token.Value is int compared)
				{
					script.Add(RecordProgram.CondLocalIntEq);
					script.Add(slot);
					WriteI32(script, compared);
					return true;
				}
			}

			return false;
		}

		bool ListField(string name, out Field field)
		{
			var found = _fields.FirstOrDefault(item => item.Name == name && item.Kind == ValueKind.List);
			field = found!;
			return found is not null;
		}

		bool ListMutate(Field list, string method, InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (method == "Clear" && args.Count == 0)
			{
				Act(script, RecordProgram.ActClearList, _code.Intern(list.Name));
				return true;
			}

			if (method == "RemoveAt" && args.Count == 1 && WriteIntExpr(args[0].Expression, out var index))
			{
				script.Add(RecordProgram.ActRemoveAt);
				U16(script, _code.Intern(list.Name));
				script.AddRange(index);
				return true;
			}

			if (method == "Add" && args.Count == 1)
				return ListAdd(list, args[0].Expression, script);
			return Fail(BadLambda, ActionLimit, invocation);
		}

		bool ListAdd(Field list, ExpressionSyntax expression, List<byte> script)
		{
			expression = Bare(expression);
			InitializerExpressionSyntax? initializer = expression switch
			{
				ObjectCreationExpressionSyntax creation => creation.Initializer,
				ImplicitObjectCreationExpressionSyntax creation => creation.Initializer,
				_ => null,
			};
			if (initializer is null || !_records.TryGetValue(list.Item, out var fields))
				return Fail(BadLambda, ActionLimit, expression);
			var assigned = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
			foreach (var item in initializer.Expressions)
			{
				if (item is not AssignmentExpressionSyntax assignment || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.Left is not IdentifierNameSyntax name)
					return Fail(BadLambda, ActionLimit, item);
				assigned[name.Identifier.ValueText] = assignment.Right;
			}

			var parts = new List<byte>();
			var count = 0;
			foreach (var field in fields)
			{
				if (field.Kind is not (ValueKind.Str or ValueKind.Bool))
					return Fail(BadLambda, ActionLimit, expression);
				if (!assigned.TryGetValue(field.Name, out var value))
				{
					if (!AddPart(parts, ref count, RecordProgram.StrLit, _code.Intern(field.Kind == ValueKind.Bool ? "false" : "")))
						return Fail(BadLambda, ActionLimit, expression);
					continue;
				}

				if (!WriteCell(field, value, parts))
					return false;
			}

			if (fields.Count > 255)
				return Fail(BadLambda, ActionLimit, expression);
			script.Add(RecordProgram.ActAddRow);
			U16(script, _code.Intern(list.Name));
			script.Add((byte)fields.Count);
			script.AddRange(parts);
			return true;
		}

		bool WriteCell(Field field, ExpressionSyntax expression, List<byte> parts)
		{
			expression = Bare(expression);
			if (field.Kind == ValueKind.Bool && expression is LiteralExpressionSyntax flag && (flag.IsKind(SyntaxKind.TrueLiteralExpression) || flag.IsKind(SyntaxKind.FalseLiteralExpression)))
			{
				var count = 0;
				return AddPart(parts, ref count, RecordProgram.StrLit, _code.Intern(flag.IsKind(SyntaxKind.TrueLiteralExpression) ? "true" : "false"));
			}
			if (field.Kind != ValueKind.Str || !WriteStrCat(expression, out var encoded, out var pieces) || pieces != 1)
				return Fail(BadLambda, ActionLimit, expression);
			parts.AddRange(encoded);
			return true;
		}

		bool Local(string name, ValueKind kind, out byte slot)
		{
			slot = 0;
			if (_locals is null || !_locals.TryGetValue(name, out var local) || local.Kind != kind)
				return false;
			slot = local.Slot;
			return true;
		}

		static ExpressionSyntax Bare(ExpressionSyntax expression)
		{
			while (expression is ParenthesizedExpressionSyntax parenthesized)
				expression = parenthesized.Expression;
			return expression;
		}

		static void WriteI32(List<byte> script, int value)
		{
			script.Add((byte)value);
			script.Add((byte)(value >> 8));
			script.Add((byte)(value >> 16));
			script.Add((byte)(value >> 24));
		}
	}
}
