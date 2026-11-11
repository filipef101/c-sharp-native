using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

static class PackingActionLowerer
{
	public abstract record Act;

	public sealed record Op(byte Code) : Act;

	public sealed record Flag(byte Code, bool Value) : Act;

	public sealed record Text(byte Code, string Value) : Act;

	public sealed record IfNew(List<Act> WhenNew, List<Act> WhenEdit) : Act;

	public static bool TryMethods(ClassDeclarationSyntax actions, out Dictionary<string, List<Act>> scripts, out string? reason)
	{
		scripts = new Dictionary<string, List<Act>>(StringComparer.Ordinal);
		foreach (var member in actions.Members)
		{
			if (member is not MethodDeclarationSyntax method)
			{
				reason = "A packing action can toggle, rename, add, or remove.";
				return false;
			}

			var name = method.Identifier.ValueText;
			if (name == "Find")
			{
				if (!FindOk(method, out reason))
					return false;
				continue;
			}

			if (!TryBody(method, out var body, out reason) || body is null)
				return false;
			scripts[name] = body;
		}

		foreach (var name in new[] { "BeginAdd", "BeginEdit", "Save", "Back", "Copy" })
		{
			if (!scripts.ContainsKey(name))
			{
				reason = "The packing actions are incomplete.";
				return false;
			}
		}

		reason = null;
		return true;
	}

	public static bool TryLambda(LambdaExpressionSyntax lambda, out List<Act>? script, out string? reason)
	{
		script = null;
		if (lambda.Block is BlockSyntax block)
			return TryStatements(block.Statements, out script, out reason);
		if (lambda.ExpressionBody is not null)
		{
			reason = "A packing action can toggle, rename, add, or remove.";
			return false;
		}

		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	public static byte[] Encode(ScreenProgram.Writer writer, IReadOnlyList<Act> script)
	{
		var code = new List<byte>();
		EncodeInto(writer, script, code);
		if (code.Count == 0)
			throw new InvalidOperationException("A packing action is empty.");
		return code.ToArray();
	}

	static void EncodeInto(ScreenProgram.Writer writer, IReadOnlyList<Act> script, List<byte> code)
	{
		foreach (var act in script)
		{
			switch (act)
			{
				case Op op:
					code.Add(op.Code);
					break;
				case Flag flag:
					code.Add(flag.Code);
					code.Add(flag.Value ? (byte)1 : (byte)0);
					break;
				case Text text:
				{
					var index = writer.Intern(text.Value);
					code.Add(text.Code);
					code.Add((byte)index);
					code.Add((byte)(index >> 8));
					break;
				}
				case IfNew branch:
				{
					var jumpNew = code.Count + 1;
					code.Add(ScreenProgram.ActJumpIfNew);
					code.Add(0);
					code.Add(0);
					EncodeInto(writer, branch.WhenEdit, code);
					var jumpEnd = code.Count + 1;
					code.Add(ScreenProgram.ActJump);
					code.Add(0);
					code.Add(0);
					Patch(code, jumpNew, code.Count);
					EncodeInto(writer, branch.WhenNew, code);
					Patch(code, jumpEnd, code.Count);
					break;
				}
				default:
				{
					var unknown = act;
					_ = unknown ?? throw new InvalidOperationException("A packing action can toggle, rename, add, or remove.");
					throw new InvalidOperationException("A packing action can toggle, rename, add, or remove.");
				}
			}
		}
	}

	static void Patch(List<byte> code, int operandAt, int target)
	{
		var relative = target - (operandAt + 2);
		if (relative <= 0 || relative > short.MaxValue)
			throw new InvalidOperationException("A packing action jump has to move forward.");
		code[operandAt] = (byte)relative;
		code[operandAt + 1] = (byte)(relative >> 8);
	}

	static bool TryBody(MethodDeclarationSyntax method, out List<Act>? script, out string? reason)
	{
		script = null;
		if (method.Body is not BlockSyntax block)
		{
			reason = "A packing action can toggle, rename, add, or remove.";
			return false;
		}

		return TryStatements(block.Statements, out script, out reason);
	}

	static bool TryStatements(IEnumerable<StatementSyntax> statements, out List<Act>? script, out string? reason)
	{
		script = new List<Act>();
		var trimmed = false;
		foreach (var statement in statements)
		{
			if (!TryStatement(statement, script, ref trimmed, out reason))
			{
				script = null;
				return false;
			}
		}

		reason = null;
		return true;
	}

	static bool TryStatement(StatementSyntax statement, List<Act> script, ref bool trimmed, out string? reason)
	{
		reason = null;
		if (statement is LocalDeclarationStatementSyntax local)
			return TryLocal(local, script, ref trimmed, out reason);
		if (statement is ExpressionStatementSyntax expression)
			return TryExpression(expression.Expression, script, out reason);
		if (statement is IfStatementSyntax check)
			return TryIf(check, script, ref trimmed, out reason);
		if (statement is ReturnStatementSyntax returned && returned.Expression is null)
			return true;
		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool TryLocal(LocalDeclarationStatementSyntax local, List<Act> script, ref bool trimmed, out string? reason)
	{
		reason = null;
		if (local.Declaration.Variables.Count != 1 || local.Declaration.Variables[0].Initializer is not EqualsValueClauseSyntax initializer)
		{
			reason = "A packing action can toggle, rename, add, or remove.";
			return false;
		}

		var variable = local.Declaration.Variables[0];
		var value = initializer.Value;
		if (variable.Identifier.ValueText == "item" && value is InvocationExpressionSyntax find && IsFind(find))
			return true;
		if (variable.Identifier.ValueText == "name" && IsTrim(value))
		{
			trimmed = true;
			return true;
		}

		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool TryIf(IfStatementSyntax check, List<Act> script, ref bool trimmed, out string? reason)
	{
		if (IsEmptyReturn(check, trimmed))
		{
			script.Add(new Op(ScreenProgram.ActTrimGate));
			reason = null;
			return true;
		}

		if (IsEditingCheck(check.Condition) && check.Else is ElseClauseSyntax otherwise)
		{
			if (!BlockOf(check.Statement, out var whenNew, out reason) || whenNew is null)
				return false;
			if (!BlockOf(otherwise.Statement, out var whenEdit, out reason) || whenEdit is null)
				return false;
			if (!FoldAdd(whenNew, out var foldedNew, out reason) || foldedNew is null)
				return false;
			script.Add(new IfNew(foldedNew, whenEdit));
			return true;
		}

		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool FoldAdd(List<Act> body, out List<Act>? folded, out string? reason)
	{
		folded = new List<Act>();
		for (var index = 0; index < body.Count; index++)
		{
			if (body[index] is Op op && op.Code == ScreenProgram.ActAdd && index + 1 < body.Count && body[index + 1] is Op next && next.Code == 0)
			{
				reason = "A packing action can toggle, rename, add, or remove.";
				folded = null;
				return false;
			}

			folded.Add(body[index]);
		}

		reason = null;
		return true;
	}

	static bool BlockOf(StatementSyntax statement, out List<Act>? script, out string? reason)
	{
		if (statement is BlockSyntax block)
			return TryStatements(block.Statements, out script, out reason);
		return TryStatements(new[] { statement }, out script, out reason);
	}

	static bool TryExpression(ExpressionSyntax expression, List<Act> script, out string? reason)
	{
		reason = null;
		if (expression is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
			return TryAssign(assignment, script, out reason);
		if (expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.PostIncrementExpression))
		{
			if (IsMember(postfix.Operand, "state", "NextNumber"))
				return true;
			if (IsMember(postfix.Operand, "PackingTelemetry", "SwitchDeliveries"))
			{
				script.Add(new Op(ScreenProgram.ActCountSwitch));
				return true;
			}

			if (IsMember(postfix.Operand, "PackingTelemetry", "FieldDeliveries"))
			{
				script.Add(new Op(ScreenProgram.ActCountField));
				return true;
			}
		}

		if (expression is InvocationExpressionSyntax call)
			return TryCall(call, script, out reason);
		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool TryAssign(AssignmentExpressionSyntax assignment, List<Act> script, out string? reason)
	{
		reason = null;
		var left = assignment.Left;
		var right = assignment.Right;
		if (IsMember(left, "state", "EditingId") && right.IsKind(SyntaxKind.NullLiteralExpression))
		{
			script.Add(new Op(ScreenProgram.ActEditingClear));
			return true;
		}

		if (IsMember(left, "state", "EditingId") && IsMember(right, "item", "Id"))
		{
			script.Add(new Op(ScreenProgram.ActEditingItem));
			return true;
		}

		if (IsMember(left, "state", "DraftName") && right.IsKind(SyntaxKind.StringLiteralExpression) && Literal(right) == "")
		{
			script.Add(new Op(ScreenProgram.ActDraftClear));
			return true;
		}

		if (IsMember(left, "state", "DraftName") && IsMember(right, "item", "Name"))
		{
			script.Add(new Op(ScreenProgram.ActDraftFromItem));
			return true;
		}

		if (IsMember(left, "state", "DraftName") && right is IdentifierNameSyntax draftArg)
		{
			script.Add(new Op(ScreenProgram.ActDraftFromArg));
			_ = draftArg;
			return true;
		}

		if (IsMember(left, "state", "DraftNote") && right is IdentifierNameSyntax)
		{
			script.Add(new Op(ScreenProgram.ActDraftNoteArg));
			return true;
		}

		if (IsMember(left, "state", "DraftPacked") && right.IsKind(SyntaxKind.FalseLiteralExpression))
		{
			script.Add(new Flag(ScreenProgram.ActDraftPacked, false));
			return true;
		}

		if (IsMember(left, "state", "DraftPacked") && right.IsKind(SyntaxKind.TrueLiteralExpression))
		{
			script.Add(new Flag(ScreenProgram.ActDraftPacked, true));
			return true;
		}

		if (IsMember(left, "state", "DraftPacked") && IsMember(right, "item", "Packed"))
		{
			script.Add(new Op(ScreenProgram.ActDraftPackedItem));
			return true;
		}

		if (IsMember(left, "state", "DraftPacked") && right is IdentifierNameSyntax)
		{
			script.Add(new Op(ScreenProgram.ActDraftPackedArg));
			return true;
		}

		if (IsMember(left, "state", "Route") && IsMember(right, "PackingRoute", "Editor"))
		{
			script.Add(new Flag(ScreenProgram.ActRoute, true));
			return true;
		}

		if (IsMember(left, "state", "Route") && IsMember(right, "PackingRoute", "List"))
		{
			script.Add(new Flag(ScreenProgram.ActRoute, false));
			return true;
		}

		if (IsPackedMember(left) && right is IdentifierNameSyntax)
		{
			script.Add(new Op(ScreenProgram.ActItemPackedArg));
			return true;
		}

		if (IsMember(left, "item", "Name") && right is IdentifierNameSyntax)
		{
			script.Add(new Op(ScreenProgram.ActRename));
			return true;
		}

		if (IsMember(left, "item", "Packed") && IsMember(right, "state", "DraftPacked"))
		{
			script.Add(new Op(ScreenProgram.ActItemPackedDraft));
			return true;
		}

		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool TryCall(InvocationExpressionSyntax call, List<Act> script, out string? reason)
	{
		reason = null;
		if (IsMember(call.Expression, "host", "RequestRender") && call.ArgumentList.Arguments.Count == 0)
		{
			script.Add(new Op(ScreenProgram.ActRender));
			return true;
		}

		if (IsMember(call.Expression, "host", "Native", "Pulse") && call.ArgumentList.Arguments.Count == 0)
		{
			script.Add(new Op(ScreenProgram.ActPulse));
			return true;
		}

		if (IsMember(call.Expression, "host", "Native", "CopyText")
			&& call.ArgumentList.Arguments.Count == 2
			&& Literal(call.ArgumentList.Arguments[0].Expression) is string label
			&& call.ArgumentList.Arguments[1].Expression is InvocationExpressionSyntax copy
			&& IsMember(copy.Expression, "state", "CopyText")
			&& copy.ArgumentList.Arguments.Count == 0)
		{
			script.Add(new Text(ScreenProgram.ActCopy, label));
			return true;
		}

		if (IsAddItem(call, out var prefix))
		{
			script.Add(new Text(ScreenProgram.ActAdd, prefix));
			return true;
		}

		if (IsMember(call.Expression, "state", "Items", "Remove")
			&& call.ArgumentList.Arguments.Count == 1
			&& call.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax)
		{
			script.Add(new Op(ScreenProgram.ActRemove));
			return true;
		}

		reason = "A packing action can toggle, rename, add, or remove.";
		return false;
	}

	static bool IsAddItem(InvocationExpressionSyntax call, out string prefix)
	{
		prefix = "";
		if (!IsMember(call.Expression, "state", "Items", "Add") || call.ArgumentList.Arguments.Count != 1)
			return false;
		if (call.ArgumentList.Arguments[0].Expression is not ObjectCreationExpressionSyntax creation)
			return false;
		if (creation.Type is not IdentifierNameSyntax type || type.Identifier.ValueText != "PackingItem")
			return false;
		if (creation.ArgumentList?.Arguments.Count != 3)
			return false;
		var id = creation.ArgumentList.Arguments[0].Expression;
		if (id is not BinaryExpressionSyntax add || !add.IsKind(SyntaxKind.AddExpression))
			return false;
		if (Literal(add.Left) is not string text || !IsMember(add.Right, "state", "NextNumber"))
			return false;
		if (creation.ArgumentList.Arguments[1].Expression is not IdentifierNameSyntax)
			return false;
		if (!IsMember(creation.ArgumentList.Arguments[2].Expression, "state", "DraftPacked"))
			return false;
		prefix = text;
		return true;
	}

	static bool FindOk(MethodDeclarationSyntax method, out string? reason)
	{
		if (method.Body is null)
		{
			reason = "Find can only look up an item by id.";
			return false;
		}

		foreach (var call in method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			_ = call;
			reason = "Find can only look up an item by id.";
			return false;
		}

		var sawItems = false;
		foreach (var access in method.Body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
		{
			if (access.Name.Identifier.ValueText == "Items")
				sawItems = true;
		}

		if (!sawItems)
		{
			reason = "Find can only look up an item by id.";
			return false;
		}

		reason = null;
		return true;
	}

	static bool IsEmptyReturn(IfStatementSyntax check, bool trimmed)
	{
		if (!trimmed || check.Else is not null)
			return false;
		if (check.Condition is not BinaryExpressionSyntax compare || !compare.IsKind(SyntaxKind.EqualsExpression))
			return false;
		if (!IsMember(compare.Left, "name", "Length"))
			return false;
		if (compare.Right is not LiteralExpressionSyntax zero || zero.Token.ValueText != "0")
			return false;
		var body = check.Statement is BlockSyntax block && block.Statements.Count == 1 ? block.Statements[0] : check.Statement;
		return body is ReturnStatementSyntax returned && returned.Expression is null;
	}

	static bool IsEditingCheck(ExpressionSyntax condition) =>
		condition is IsPatternExpressionSyntax pattern
		&& IsMember(pattern.Expression, "state", "EditingId")
		&& pattern.Pattern is ConstantPatternSyntax constant
		&& constant.Expression.IsKind(SyntaxKind.NullLiteralExpression);

	static bool IsTrim(ExpressionSyntax expression) =>
		expression is InvocationExpressionSyntax call
		&& IsMember(call.Expression, "state", "DraftName", "Trim")
		&& call.ArgumentList.Arguments.Count == 0;

	static bool IsFind(InvocationExpressionSyntax call) =>
		call.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "Find";

	static bool IsPackedMember(ExpressionSyntax expression) =>
		expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Packed";

	static string? Literal(ExpressionSyntax expression)
	{
		if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
			return null;
		return literal.Token.ValueText;
	}

	static bool IsMember(ExpressionSyntax expression, params string[] parts)
	{
		var members = new List<string>();
		ExpressionSyntax current = expression;
		while (current is MemberAccessExpressionSyntax access)
		{
			members.Add(access.Name.Identifier.ValueText);
			current = access.Expression;
		}

		if (current is not IdentifierNameSyntax identifier)
			return false;
		members.Add(identifier.Identifier.ValueText);
		members.Reverse();
		if (members.Count != parts.Length)
			return false;
		for (var index = 0; index < parts.Length; index++)
		{
			if (members[index] != parts[index])
				return false;
		}

		return true;
	}
}
