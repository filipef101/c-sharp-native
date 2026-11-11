using NativePoc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

sealed record LowerResult(byte[]? Program, string? Reason)
{
	public static LowerResult Refuse(string reason) => new(null, reason);

	public static LowerResult Ready(byte[] program) => new(program, null);
}

static class ScreenLowerer
{
	public static LowerResult TryLower(string source)
	{
		ArgumentNullException.ThrowIfNull(source);
		var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		foreach (var diagnostic in tree.GetDiagnostics())
		{
			if (diagnostic.Severity == DiagnosticSeverity.Error)
				return LowerResult.Refuse(diagnostic.GetMessage());
		}

		if (tree.GetCompilationUnitRoot() is not CompilationUnitSyntax root
			|| root.Members is not [FileScopedNamespaceDeclarationSyntax space]
			|| space.Name.ToString() != "NativePoc"
			|| space.Members is not [ClassDeclarationSyntax screen]
			|| screen.Identifier.ValueText != "Screen")
			return LowerResult.Refuse("The screen program is the NativePoc.Screen class.");

		FieldDeclarationSyntax? prefixField = null;
		MethodDeclarationSyntax? render = null;
		foreach (var member in screen.Members)
		{
			switch (member)
			{
				case FieldDeclarationSyntax field when field.Declaration.Variables is [var variable] && variable.Identifier.ValueText == "TitlePrefix":
					if (prefixField is not null)
						return LowerResult.Refuse("TitlePrefix is declared twice.");
					prefixField = field;
					break;
				case MethodDeclarationSyntax method when method.Identifier.ValueText == "Render":
					if (render is not null)
						return LowerResult.Refuse("Render is declared twice.");
					render = method;
					break;
				default:
					return LowerResult.Refuse("A screen program cannot carry " + MemberName(member) + ".");
			}
		}

		if (render is null)
			return LowerResult.Refuse("Screen.Render is missing.");
		if (prefixField is null)
			return LowerResult.Refuse("TitlePrefix is missing.");
		if (!IsConstString(prefixField, out var titlePrefix))
			return LowerResult.Refuse("TitlePrefix has to be a const string.");
		if (render.Body is not BlockSyntax body)
			return LowerResult.Refuse("Screen.Render has to be a block.");
		if (render.ParameterList.Parameters is not [var stateParameter, var platformParameter])
			return LowerResult.Refuse("Screen.Render has to take the state and the platform.");
		if (!IsName(render.ReturnType, "Element")
			|| !IsName(stateParameter.Type, "AppState")
			|| !IsName(platformParameter.Type, "IPlatform"))
			return LowerResult.Refuse("Screen.Render has to take the state and the platform.");
		var state = stateParameter.Identifier.ValueText;
		var platform = platformParameter.Identifier.ValueText;
		if (body.Statements.Count != 7)
			return LowerResult.Refuse("Screen.Render has to stay the counter screen.");

		if (!Title(body.Statements[0], state, out var titleName, out var titleKey))
			return LowerResult.Refuse("The title has to be TitlePrefix plus the count.");
		if (!PlainReturn(body.Statements[1], state, titleName))
			return LowerResult.Refuse("PlainRoot has to return the title.");
		if (!Field(body.Statements[2], state, platform, out var nameLocal, out var nameKey, out var placeholder, out var nameLabel))
			return LowerResult.Refuse("The name field has to write the name and ask for another frame.");
		if (!Save(body.Statements[3], state, platform, out var saveLocal, out var saveKey, out var saveTitle, out var saveLabel, out var nonce))
			return LowerResult.Refuse("Save has to increment, copy the nonce, and ask for another frame.");
		if (!Greeting(body.Statements[4], state, out var greetingLocal, out var greetingKey, out var emptyGreeting, out var hello))
			return LowerResult.Refuse("The greeting has to be the empty line or the hello line.");
		if (!Orders(body.Statements[5], state, titleName, nameLocal, saveLocal, greetingLocal, out var children, out var first, out var rest))
			return LowerResult.Refuse("GreetingFirst has to reorder the title, the name, Save, and the greeting.");
		if (!ColumnReturn(body.Statements[6], children))
			return LowerResult.Refuse("Screen.Render has to return the column.");

		var writer = new ScreenProgram.Writer();
		var prefixIndex = writer.Intern(titlePrefix);
		var titleKeyIndex = writer.Intern(titleKey);
		writer.PushString(prefixIndex);
		writer.PushCount();
		writer.Concat();
		writer.MakeText(titleKeyIndex);
		writer.Store(0);
		writer.PushPlainRoot();
		var toRest = writer.JumpIfFalse();
		writer.Load(0);
		writer.Return();
		writer.Patch(toRest, writer.Here);

		writer.PushName();
		writer.PushString(writer.Intern(placeholder));
		writer.PushString(writer.Intern(nameLabel));
		writer.MakeField(writer.Intern(nameKey));
		writer.Store(1);

		writer.PushString(writer.Intern(saveTitle));
		writer.PushString(writer.Intern(saveLabel));
		writer.MakeButton(writer.Intern(saveKey), writer.Intern(nonce));
		writer.Store(2);

		writer.PushName();
		writer.IsEmpty();
		var toHello = writer.JumpIfFalse();
		writer.PushString(writer.Intern(emptyGreeting));
		var toGreeting = writer.Jump();
		writer.Patch(toHello, writer.Here);
		writer.PushString(writer.Intern(hello));
		writer.PushName();
		writer.Concat();
		writer.Patch(toGreeting, writer.Here);
		writer.MakeText(writer.Intern(greetingKey));
		writer.Store(3);

		writer.PushGreetingFirst();
		var toSecond = writer.JumpIfFalse();
		EmitOrder(writer, first);
		writer.Patch(toSecond, writer.Here);
		EmitOrder(writer, rest);
		return LowerResult.Ready(writer.Finish(source));
	}

	static void EmitOrder(ScreenProgram.Writer writer, byte[] slots)
	{
		foreach (var slot in slots)
			writer.Load(slot);
		writer.MakeColumn((byte)slots.Length);
		writer.Return();
	}

	static bool Title(StatementSyntax statement, string state, out string local, out string key)
	{
		local = "";
		key = "";
		if (statement is not LocalDeclarationStatementSyntax declaration)
			return false;
		if (declaration.Declaration.Variables is not [var variable] || variable.Initializer is null)
			return false;
		if (variable.Initializer.Value is not ObjectCreationExpressionSyntax creation || !IsName(creation.Type, "Text"))
			return false;
		if (creation.ArgumentList is not { Arguments: [var argument] })
			return false;
		if (argument.Expression is not BinaryExpressionSyntax add || !add.IsKind(SyntaxKind.AddExpression))
			return false;
		if (add.Left is not IdentifierNameSyntax prefix || prefix.Identifier.ValueText != "TitlePrefix")
			return false;
		if (!StateMember(add.Right, state, "Count"))
			return false;
		if (!CreationKey(creation, out key))
			return false;
		local = variable.Identifier.ValueText;
		return local.Length > 0;
	}

	static bool PlainReturn(StatementSyntax statement, string state, string title)
	{
		if (statement is not IfStatementSyntax check || check.Else is not null)
			return false;
		if (!StateMember(check.Condition, state, "PlainRoot"))
			return false;
		var branch = Unwrap(check.Statement);
		return branch is ReturnStatementSyntax returned
			&& returned.Expression is IdentifierNameSyntax name
			&& name.Identifier.ValueText == title;
	}

	static bool Field(
		StatementSyntax statement,
		string state,
		string platform,
		out string local,
		out string key,
		out string placeholder,
		out string label)
	{
		local = "";
		key = "";
		placeholder = "";
		label = "";
		if (!Creation(statement, "TextField", out local, out var creation))
			return false;
		if (creation.ArgumentList is not { Arguments: [var value, var change, var placeholderArgument, var labelArgument] })
			return false;
		if (!StateMember(value.Expression, state, "Name"))
			return false;
		if (!IsSetName(change.Expression, state, platform))
			return false;
		if (Literal(placeholderArgument.Expression) is not string placeholderText || Literal(labelArgument.Expression) is not string labelText)
			return false;
		if (!CreationKey(creation, out key))
			return false;
		placeholder = placeholderText;
		label = labelText;
		return true;
	}

	static bool Save(
		StatementSyntax statement,
		string state,
		string platform,
		out string local,
		out string key,
		out string title,
		out string label,
		out string nonce)
	{
		local = "";
		key = "";
		title = "";
		label = "";
		nonce = "";
		if (!Creation(statement, "Button", out local, out var creation))
			return false;
		if (creation.ArgumentList is not { Arguments: [var titleArgument, var press, var labelArgument] })
			return false;
		if (Literal(titleArgument.Expression) is not string titleText || Literal(labelArgument.Expression) is not string labelText)
			return false;
		if (!IsSave(press.Expression, state, platform, out var prefix))
			return false;
		if (!CreationKey(creation, out key))
			return false;
		title = titleText;
		label = labelText;
		nonce = prefix;
		return true;
	}

	static bool Greeting(
		StatementSyntax statement,
		string state,
		out string local,
		out string key,
		out string empty,
		out string hello)
	{
		local = "";
		key = "";
		empty = "";
		hello = "";
		if (!Creation(statement, "Text", out local, out var creation))
			return false;
		if (creation.ArgumentList is not { Arguments: [var argument] })
			return false;
		if (argument.Expression is not ConditionalExpressionSyntax choice)
			return false;
		if (choice.Condition is not BinaryExpressionSyntax compare || !compare.IsKind(SyntaxKind.EqualsExpression))
			return false;
		if (!LengthOfName(compare.Left, state) || compare.Right is not LiteralExpressionSyntax zero || zero.Token.ValueText != "0")
			return false;
		if (Literal(choice.WhenTrue) is not string emptyText)
			return false;
		if (choice.WhenFalse is not BinaryExpressionSyntax add || !add.IsKind(SyntaxKind.AddExpression))
			return false;
		if (Literal(add.Left) is not string helloText || !StateMember(add.Right, state, "Name"))
			return false;
		if (!CreationKey(creation, out key))
			return false;
		empty = emptyText;
		hello = helloText;
		return true;
	}

	static bool Orders(
		StatementSyntax statement,
		string state,
		string title,
		string name,
		string save,
		string greeting,
		out string local,
		out byte[] first,
		out byte[] rest)
	{
		local = "";
		first = [];
		rest = [];
		if (statement is not LocalDeclarationStatementSyntax declaration)
			return false;
		if (declaration.Declaration.Variables is not [var variable] || variable.Initializer is null)
			return false;
		if (variable.Initializer.Value is not ConditionalExpressionSyntax choice)
			return false;
		if (!StateMember(choice.Condition, state, "GreetingFirst"))
			return false;
		if (!Slots(choice.WhenTrue, title, name, save, greeting, out first))
			return false;
		if (!Slots(choice.WhenFalse, title, name, save, greeting, out rest))
			return false;
		local = variable.Identifier.ValueText;
		return local.Length > 0;
	}

	static bool Slots(ExpressionSyntax expression, string title, string name, string save, string greeting, out byte[] slots)
	{
		slots = [];
		if (expression is not ArrayCreationExpressionSyntax array || array.Initializer is null)
			return false;
		if (array.Type is not ArrayTypeSyntax type || !IsName(type.ElementType, "Element") || type.RankSpecifiers.Count != 1)
			return false;
		if (array.Initializer.Expressions.Count != 4)
			return false;
		var mapped = new byte[4];
		var seen = 0;
		for (var index = 0; index < 4; index++)
		{
			if (array.Initializer.Expressions[index] is not IdentifierNameSyntax identifier)
				return false;
			var text = identifier.Identifier.ValueText;
			byte slot;
			if (text == title)
				slot = 0;
			else if (text == name)
				slot = 1;
			else if (text == save)
				slot = 2;
			else if (text == greeting)
				slot = 3;
			else
				return false;
			var bit = 1 << slot;
			if ((seen & bit) != 0)
				return false;
			seen |= bit;
			mapped[index] = slot;
		}

		slots = mapped;
		return seen == 0b1111;
	}

	static bool ColumnReturn(StatementSyntax statement, string children)
	{
		if (statement is not ReturnStatementSyntax returned)
			return false;
		if (returned.Expression is not ObjectCreationExpressionSyntax creation || !IsName(creation.Type, "Column"))
			return false;
		if (creation.Initializer is not null || creation.ArgumentList is not { Arguments: [var argument] })
			return false;
		return argument.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == children;
	}

	static bool IsConstString(FieldDeclarationSyntax field, out string value)
	{
		value = "";
		if (!field.Modifiers.Any(SyntaxKind.PublicKeyword) || !field.Modifiers.Any(SyntaxKind.ConstKeyword))
			return false;
		if (field.Declaration.Type is not PredefinedTypeSyntax predefined || !predefined.Keyword.IsKind(SyntaxKind.StringKeyword))
			return false;
		if (field.Declaration.Variables is not [var variable] || variable.Initializer is null)
			return false;
		if (Literal(variable.Initializer.Value) is not string text)
			return false;
		value = text;
		return true;
	}

	static bool IsSetName(ExpressionSyntax expression, string state, string platform)
	{
		if (!LambdaBlock(expression, out var parameter, out var block) || block.Statements.Count != 2)
			return false;
		if (block.Statements[0] is not ExpressionStatementSyntax assignmentStatement
			|| assignmentStatement.Expression is not AssignmentExpressionSyntax assignment
			|| !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
			return false;
		if (!StateMember(assignment.Left, state, "Name"))
			return false;
		if (assignment.Right is not IdentifierNameSyntax value || value.Identifier.ValueText != parameter)
			return false;
		return IsRequest(block.Statements[1], platform);
	}

	static bool IsSave(ExpressionSyntax expression, string state, string platform, out string nonce)
	{
		nonce = "";
		if (!LambdaBlock(expression, out _, out var block) || block.Statements.Count != 3)
			return false;
		if (block.Statements[0] is not ExpressionStatementSyntax incrementStatement
			|| incrementStatement.Expression is not PostfixUnaryExpressionSyntax increment
			|| !increment.IsKind(SyntaxKind.PostIncrementExpression)
			|| !StateMember(increment.Operand, state, "Count"))
			return false;
		if (block.Statements[1] is not ExpressionStatementSyntax copyStatement
			|| copyStatement.Expression is not InvocationExpressionSyntax copy
			|| !IsCall(copy, platform, "CopyNonce")
			|| copy.ArgumentList.Arguments is not [var argument])
			return false;
		if (argument.Expression is not BinaryExpressionSyntax add || !add.IsKind(SyntaxKind.AddExpression))
			return false;
		if (Literal(add.Left) is not string prefix || !StateMember(add.Right, state, "Count"))
			return false;
		if (!IsRequest(block.Statements[2], platform))
			return false;
		nonce = prefix;
		return true;
	}

	static bool IsRequest(StatementSyntax statement, string platform)
	{
		return statement is ExpressionStatementSyntax expression
			&& expression.Expression is InvocationExpressionSyntax call
			&& IsCall(call, platform, "RequestRender")
			&& call.ArgumentList.Arguments.Count == 0;
	}

	static bool IsCall(InvocationExpressionSyntax call, string receiver, string name)
	{
		return call.Expression is MemberAccessExpressionSyntax member
			&& member.Expression is IdentifierNameSyntax identifier
			&& identifier.Identifier.ValueText == receiver
			&& member.Name.Identifier.ValueText == name;
	}

	static bool LambdaBlock(ExpressionSyntax expression, out string parameter, out BlockSyntax block)
	{
		parameter = "";
		block = null!;
		switch (expression)
		{
			case SimpleLambdaExpressionSyntax simple when simple.Body is BlockSyntax simpleBlock:
				parameter = simple.Parameter.Identifier.ValueText;
				block = simpleBlock;
				return parameter.Length > 0;
			case ParenthesizedLambdaExpressionSyntax lambda when lambda.Body is BlockSyntax lambdaBlock:
				if (lambda.ParameterList.Parameters.Count == 1)
					parameter = lambda.ParameterList.Parameters[0].Identifier.ValueText;
				block = lambdaBlock;
				return true;
			default:
				return false;
		}
	}

	static bool LengthOfName(ExpressionSyntax expression, string state)
	{
		return expression is MemberAccessExpressionSyntax member
			&& member.Name.Identifier.ValueText == "Length"
			&& StateMember(member.Expression, state, "Name");
	}

	static bool Creation(StatementSyntax statement, string type, out string local, out ObjectCreationExpressionSyntax creation)
	{
		local = "";
		creation = null!;
		if (statement is not LocalDeclarationStatementSyntax declaration)
			return false;
		if (declaration.Declaration.Variables is not [var variable] || variable.Initializer is null)
			return false;
		if (variable.Initializer.Value is not ObjectCreationExpressionSyntax made || !IsName(made.Type, type))
			return false;
		local = variable.Identifier.ValueText;
		creation = made;
		return local.Length > 0;
	}

	static bool CreationKey(ObjectCreationExpressionSyntax creation, out string key)
	{
		key = "";
		if (creation.Initializer is not InitializerExpressionSyntax initializer)
			return false;
		if (initializer.Expressions is not [AssignmentExpressionSyntax assignment])
			return false;
		if (assignment.Left is not IdentifierNameSyntax name || name.Identifier.ValueText != "Key")
			return false;
		if (Literal(assignment.Right) is not string text)
			return false;
		key = text;
		return true;
	}

	static StatementSyntax Unwrap(StatementSyntax statement)
	{
		if (statement is BlockSyntax block && block.Statements.Count == 1)
			return block.Statements[0];
		return statement;
	}

	static bool StateMember(ExpressionSyntax expression, string state, string member)
	{
		return expression is MemberAccessExpressionSyntax access
			&& access.Expression is IdentifierNameSyntax identifier
			&& identifier.Identifier.ValueText == state
			&& access.Name.Identifier.ValueText == member;
	}

	static string? Literal(ExpressionSyntax expression)
	{
		if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			return literal.Token.ValueText;
		return null;
	}

	static bool IsName(TypeSyntax? type, string name) =>
		type is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;

	static string MemberName(MemberDeclarationSyntax member) =>
		member switch
		{
			MethodDeclarationSyntax method => method.Identifier.ValueText,
			FieldDeclarationSyntax field when field.Declaration.Variables.Count > 0 => field.Declaration.Variables[0].Identifier.ValueText,
			PropertyDeclarationSyntax property => property.Identifier.ValueText,
			_ => member.Kind().ToString(),
		};
}
