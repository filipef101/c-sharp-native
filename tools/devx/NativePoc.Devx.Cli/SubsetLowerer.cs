using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NativePoc;

namespace NativePoc.Devx;

public sealed record SubsetDiagnostic(string Id, string Message, int Start, int End, int Line, int Column);

public sealed record SubsetResult(byte[]? Program, IReadOnlyList<SubsetDiagnostic> Diagnostics)
{
	public bool Ok => Program is not null && Diagnostics.Count == 0;

	public IReadOnlyList<(bool Works, int Length)> Actions { get; init; } = [];

	public IReadOnlyList<string> Types { get; init; } = [];

	public IReadOnlyList<SubsetDiagnostic> Reported { get; init; } = [];

	public static SubsetResult Ready(byte[] program, IReadOnlyList<(bool Works, int Length)>? actions = null, IReadOnlyList<string>? types = null, IReadOnlyList<SubsetDiagnostic>? reported = null) =>
		new(program, Array.Empty<SubsetDiagnostic>()) { Actions = actions ?? [], Types = types ?? [], Reported = reported ?? [] };

	public static SubsetResult Refuse(SubsetDiagnostic diagnostic, IReadOnlyList<string>? types = null, IReadOnlyList<SubsetDiagnostic>? reported = null) =>
		new(null, [diagnostic]) { Types = types ?? [], Reported = reported ?? [] };
}

public static partial class SubsetLowerer
{
	public const string NotAScreen = "subset.not-a-screen";
	public const string UnknownMember = "subset.unknown-member";
	public const string UnknownElement = "subset.unknown-element";
	public const string UnknownExpression = "subset.unknown-expression";
	public const string BadLambda = "subset.bad-lambda";
	public const string BadForeach = "subset.bad-foreach";
	public const string DuplicateKey = "subset.duplicate-key";
	public const string UnknownType = "subset.unknown-type";

	public static SubsetResult TryLower(string source, IReadOnlyList<string>? companions = null, IReadOnlyList<string>? callables = null, string? originFile = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		foreach (var diagnostic in tree.GetDiagnostics())
		{
			if (diagnostic.Severity == DiagnosticSeverity.Error)
				return SubsetResult.Refuse(At(NotAScreen, diagnostic.GetMessage(), diagnostic.Location, source));
		}

		var root = tree.GetCompilationUnitRoot();
		var members = MembersOf(root);
		if (members is null)
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", root, source));

		var records = new Dictionary<string, List<Field>>(StringComparer.Ordinal);
		var methods = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
		var enums = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
		foreach (var member in members)
		{
			if (member is not EnumDeclarationSyntax enumerated)
				continue;
			if (!RecordEnum(enumerated, enums, records, source, out var enumError))
				return SubsetResult.Refuse(enumError!);
		}

		ClassDeclarationSyntax? screen = null;
		foreach (var member in members)
		{
			switch (member)
			{
				case EnumDeclarationSyntax:
					break;
				case RecordDeclarationSyntax record:
					if (!RecordFields(record, records, methods, enums, null, source, out var recordError))
						return SubsetResult.Refuse(recordError!);
					break;
				case ClassDeclarationSyntax type when type.Modifiers.Any(SyntaxKind.StaticKeyword):
					if (screen is not null)
						return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", type, source));
					screen = type;
					break;
				case ClassDeclarationSyntax type:
					if (!RecordFields(type, records, methods, enums, null, source, out var classError))
						return SubsetResult.Refuse(classError!);
					break;
				default:
					return SubsetResult.Refuse(At(UnknownMember, "The screen subset does not include " + MemberName(member) + ".", member, source));
			}
		}

		if (screen is null)
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", root, source));
		MethodDeclarationSyntax? render = null;
		var consts = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var member in screen.Members)
		{
			if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == "Render")
			{
				if (render is not null)
					return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", method, source));
				render = method;
				continue;
			}

			if (member is MethodDeclarationSyntax helper)
			{
				if (helper.TypeParameterList is not null)
					return SubsetResult.Refuse(At(UnknownMember, "The screen subset does not include a generic method.", helper, source));
				methods[helper.Identifier.ValueText] = helper;
				continue;
			}

			if (member is FieldDeclarationSyntax field && ConstString(field, out var constName, out var constValue))
			{
				consts.Add(constName, constValue);
				continue;
			}

			return SubsetResult.Refuse(At(UnknownMember, "The screen subset does not include " + MemberName(member) + ".", member, source));
		}

		if (render is null || !IsRender(render, out var stateName, out var stateType, out var platformName))
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", render ?? (SyntaxNode)screen, source));
		var reported = new List<SubsetDiagnostic>();
		if (!CompanionRecords(companions, records, methods, enums, stateType, reported, out var companionError))
			return SubsetResult.Refuse(companionError!, TypeNames(records), reported);
		if (!records.TryGetValue(stateType, out var fields))
		{
			if (!Infer(render, stateName, source, out fields, out var inferred))
				return SubsetResult.Refuse(inferred!);
			records.Add(stateType, fields);
		}

		var components = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
		var calls = new Dictionary<string, AppSig>(StringComparer.Ordinal);
		if (!CollectExtras(companions, callables, components, calls, source, out var extraError))
			return SubsetResult.Refuse(extraError!);
		var lower = new Lower(source, stateName, platformName, fields, records, consts, methods, stateType, components, calls);
		lower.UseBindings(BindingCalls(source, companions));
		lower.SetOrigin(originFile);
		try
		{
			if (!lower.Body(render))
				return new SubsetResult(null, lower.Errors);
			return SubsetResult.Ready(lower.Finish(), lower.Actions, TypeNames(records), reported);
		}
		catch (InvalidOperationException ex)
		{
			return SubsetResult.Refuse(At(NotAScreen, ex.Message, render, source));
		}
	}

	public static string ToJson(SubsetResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Program is { } program && result.Diagnostics.Count == 0)
		{
			var hash = Convert.ToHexString(program.AsSpan(program.Length - 16)).ToLowerInvariant();
			return string.Create(
				CultureInfo.InvariantCulture,
				$"{{\"ok\":true,\"bytes\":{program.Length},\"hash\":\"{hash}\"}}");
		}

		var builder = new StringBuilder();
		builder.Append("{\"ok\":false,\"diagnostics\":[");
		for (var index = 0; index < result.Diagnostics.Count; index++)
		{
			if (index > 0)
				builder.Append(',');
			var diagnostic = result.Diagnostics[index];
			builder.Append("{\"id\":\"").Append(Escape(diagnostic.Id));
			builder.Append("\",\"message\":\"").Append(Escape(diagnostic.Message));
			builder.Append("\",\"start\":").Append(diagnostic.Start.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"end\":").Append(diagnostic.End.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"line\":").Append(diagnostic.Line.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"column\":").Append(diagnostic.Column.ToString(CultureInfo.InvariantCulture));
			builder.Append('}');
		}

		builder.Append("]}");
		return builder.ToString();
	}

	static string Escape(string value) =>
		value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

	static bool IsRender(MethodDeclarationSyntax method, out string stateName, out string stateType, out string platformName)
	{
		stateName = "";
		stateType = "";
		platformName = "";
		if (method.ReturnType.ToString() != "Element" || method.ParameterList.Parameters.Count != 2)
			return false;
		if (!method.Modifiers.Any(SyntaxKind.StaticKeyword) || !method.Modifiers.Any(SyntaxKind.PublicKeyword))
			return false;
		var state = method.ParameterList.Parameters[0];
		var platform = method.ParameterList.Parameters[1];
		if (platform.Type?.ToString() != "IPlatform")
			return false;
		stateName = state.Identifier.ValueText;
		stateType = state.Type?.ToString() ?? "";
		platformName = platform.Identifier.ValueText;
		return stateType.Length > 0 && stateName.Length > 0;
	}

	static bool ConstString(FieldDeclarationSyntax field, out string name, out string value)
	{
		name = "";
		value = "";
		if (field.Declaration.Type.ToString() != "string" || field.Declaration.Variables.Count != 1)
			return false;
		var variable = field.Declaration.Variables[0];
		var literal = variable.Initializer?.Value as LiteralExpressionSyntax;
		if (literal is null || !literal.IsKind(SyntaxKind.StringLiteralExpression))
			return false;
		var isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);
		var isReadonly = field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
		if (!isConst && !isReadonly)
			return false;
		if (!field.Modifiers.Any(SyntaxKind.PublicKeyword))
			return false;
		name = variable.Identifier.ValueText;
		value = literal.Token.ValueText;
		return true;
	}

	static bool Infer(MethodDeclarationSyntax method, string state, string source, out List<Field> fields, out SubsetDiagnostic? error)
	{
		fields = new List<Field>();
		error = null;
		var kinds = new Dictionary<string, ValueKind>(StringComparer.Ordinal);
		foreach (var member in method.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
		{
			if (member.Name.Identifier.ValueText == "Length"
				&& member.Expression is MemberAccessExpressionSyntax inner
				&& inner.Expression is IdentifierNameSyntax lengthOwner
				&& lengthOwner.Identifier.ValueText == state)
			{
				if (!Note(kinds, inner.Name.Identifier.ValueText, ValueKind.Str))
				{
					error = At(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", inner, source);
					return false;
				}

				continue;
			}

			if (member.Expression is not IdentifierNameSyntax owner || owner.Identifier.ValueText != state)
				continue;
			var name = member.Name.Identifier.ValueText;
			var kind = Hint(member);
			if (kind is null)
				continue;
			if (!Note(kinds, name, kind.Value))
			{
				error = At(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", member, source);
				return false;
			}
		}

		if (kinds.Count == 0)
		{
			error = At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", method, source);
			return false;
		}

		foreach (var pair in kinds)
			fields.Add(Field.Scalar(pair.Key, pair.Value, "", false, 0));
		return true;
	}

	static bool Note(Dictionary<string, ValueKind> kinds, string name, ValueKind kind)
	{
		if (!kinds.TryGetValue(name, out var existing))
		{
			kinds.Add(name, kind);
			return true;
		}

		if (existing == ValueKind.Str && kind == ValueKind.Int)
			return true;
		return existing == kind;
	}

	static ValueKind? Hint(MemberAccessExpressionSyntax member)
	{
		var parent = member.Parent;
		if (parent is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.PostIncrementExpression) && postfix.Operand == member)
			return ValueKind.Int;
		if (parent is IfStatementSyntax branch && branch.Condition == member)
			return ValueKind.Bool;
		if (parent is ConditionalExpressionSyntax choice && choice.Condition == member)
			return ValueKind.Bool;
		if (parent is ArgumentSyntax argument && argument.Parent is ArgumentListSyntax list && list.Parent is ObjectCreationExpressionSyntax creation)
		{
			var type = creation.Type.ToString();
			if (list.Arguments.Count > 0 && list.Arguments[0] == argument && type is "TextField" or "TextInput")
				return ValueKind.Str;
			if (list.Arguments.Count > 0 && list.Arguments[0] == argument && type == "Switch")
				return ValueKind.Bool;
		}

		if (parent is AssignmentExpressionSyntax assignment && assignment.Left == member)
		{
			if (assignment.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				return ValueKind.Str;
			if (assignment.Right is IdentifierNameSyntax)
				return null;
		}

		if (parent is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
			return ValueKind.Int;
		return null;
	}

	static HashSet<string> BindingCalls(string source, IReadOnlyList<string>? companions)
	{
		var found = new HashSet<string>(StringComparer.Ordinal);
		ScanBindings(source, found);
		if (companions is not null)
		{
			foreach (var companion in companions)
				ScanBindings(companion, found);
		}

		return found;
	}

	static void ScanBindings(string source, HashSet<string> found)
	{
		var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
		{
			if (!HasAttribute(type.AttributeLists, "JniType"))
				continue;
			foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
			{
				if (!HasAttribute(method.AttributeLists, "JniMethod"))
					continue;
				found.Add(type.Identifier.ValueText + "." + method.Identifier.ValueText);
			}
		}
	}

	static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name)
	{
		foreach (var list in lists)
		{
			foreach (var attribute in list.Attributes)
			{
				var text = attribute.Name.ToString();
				if (text == name || text == name + "Attribute" || text.EndsWith("." + name, StringComparison.Ordinal) || text.EndsWith("." + name + "Attribute", StringComparison.Ordinal))
					return true;
			}
		}

		return false;
	}

	static string[] TypeNames(Dictionary<string, List<Field>> records)
	{
		var names = records.Keys.ToArray();
		Array.Sort(names, StringComparer.Ordinal);
		return names;
	}

	static bool CompanionRecords(
		IReadOnlyList<string>? companions,
		Dictionary<string, List<Field>> records,
		Dictionary<string, MethodDeclarationSyntax> methods,
		Dictionary<string, Dictionary<string, int>> enums,
		string stateType,
		List<SubsetDiagnostic> reported,
		out SubsetDiagnostic? error)
	{
		error = null;
		if (companions is null)
			return true;
		var waiting = new List<(TypeDeclarationSyntax Type, string Source)>();
		foreach (var companion in companions)
		{
			if (!ParseMembers(companion, out var members, out error))
				return false;
			foreach (var member in members)
			{
				switch (member)
				{
					case EnumDeclarationSyntax enumerated:
						if (!RecordEnum(enumerated, enums, records, companion, out error))
							return false;
						break;
					case InterfaceDeclarationSyntax face:
						reported.Add(At(UnknownType, face.Identifier.ValueText + " is an interface. Change " + face.Identifier.ValueText + " to a class or record of string, bool, int, or list properties, or stop using " + face.Identifier.ValueText + " from the screen.", face, companion));
						break;
					case TypeDeclarationSyntax type when type.Modifiers.Any(SyntaxKind.StaticKeyword):
						reported.Add(At(UnknownType, type.Identifier.ValueText + " is static. Change " + type.Identifier.ValueText + " to a class or record of string, bool, int, or list properties, or stop using " + type.Identifier.ValueText + " from the screen.", type, companion));
						break;
					case TypeDeclarationSyntax type:
						waiting.Add((type, companion));
						break;
					default:
						reported.Add(At(UnknownMember, MemberName(member) + " is not a data class, record, or enum. Change it, or stop using it from the screen.", member, companion));
						break;
				}
			}
		}

		var pending = waiting;
		while (pending.Count > 0)
		{
			var next = new List<(TypeDeclarationSyntax Type, string Source)>();
			var recorded = 0;
			foreach (var (type, source) in pending)
			{
				if (RecordFields(type, records, methods, enums, type.Identifier.ValueText, source, out var typeError))
				{
					recorded++;
					continue;
				}

				DropType(type.Identifier.ValueText, records, methods);
				next.Add((type, source));
				error = typeError;
			}

			if (recorded == 0)
				break;
			pending = next;
		}

		foreach (var (type, source) in pending)
		{
			DropType(type.Identifier.ValueText, records, methods);
			if (!RecordFields(type, records, methods, enums, type.Identifier.ValueText, source, out error))
			{
				DropType(type.Identifier.ValueText, records, methods);
				if (error is null)
					error = At(UnknownType, type.Identifier.ValueText + " was not recorded. Change it to string, bool, int, or list properties, or stop using " + type.Identifier.ValueText + " from the screen.", type, source);
				if (type.Identifier.ValueText == stateType)
					return false;
				reported.Add(error);
				error = null;
			}
		}

		return true;
	}

	static void DropType(string name, Dictionary<string, List<Field>> records, Dictionary<string, MethodDeclarationSyntax> methods)
	{
		var prefix = name + ".";
		foreach (var key in methods.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
			methods.Remove(key);
		records.Remove(name);
	}

	static bool RecordEnum(
		EnumDeclarationSyntax enumerated,
		Dictionary<string, Dictionary<string, int>> enums,
		Dictionary<string, List<Field>> records,
		string source,
		out SubsetDiagnostic? error)
	{
		error = null;
		var name = enumerated.Identifier.ValueText;
		var values = new Dictionary<string, int>(StringComparer.Ordinal);
		var next = 0;
		foreach (var member in enumerated.Members)
		{
			if (member.EqualsValue is null)
			{
				values[member.Identifier.ValueText] = next;
				next++;
				continue;
			}

			if (member.EqualsValue.Value is LiteralExpressionSyntax literal
				&& literal.IsKind(SyntaxKind.NumericLiteralExpression)
				&& literal.Token.Value is int number)
			{
				next = number;
				values[member.Identifier.ValueText] = next;
				next++;
				continue;
			}

			error = At(UnknownType, name + "." + member.Identifier.ValueText + " is not an int constant. Change " + member.Identifier.ValueText + " to an int, or stop using " + name + " from the screen.", member, source);
			return false;
		}

		if (!enums.TryAdd(name, values) || !records.TryAdd(name, new List<Field>()))
		{
			error = At(UnknownMember, name + " is already recorded. Change the duplicate, or stop using " + name + " from the screen.", enumerated, source);
			return false;
		}

		return true;
	}

	static bool RecordFields(TypeDeclarationSyntax type, Dictionary<string, List<Field>> records, Dictionary<string, MethodDeclarationSyntax> methods, IReadOnlyDictionary<string, Dictionary<string, int>> enums, string? owner, string source, out SubsetDiagnostic? error)
	{
		error = null;
		var fields = new List<Field>();
		foreach (var member in type.Members)
		{
			if (member is MethodDeclarationSyntax method)
			{
				methods[type.Identifier.ValueText + "." + method.Identifier.ValueText] = method;
				continue;
			}

			if (member is ConstructorDeclarationSyntax)
				continue;

			if (member is FieldDeclarationSyntax stored && !stored.Modifiers.Any(SyntaxKind.PublicKeyword))
				continue;

			if (member is not PropertyDeclarationSyntax property)
			{
				var memberName = MemberName(member);
				error = owner is null
					? At(UnknownMember, "The screen subset does not include " + memberName + ".", member, source)
					: At(UnknownMember, owner + "." + memberName + " is not a property. Change " + memberName + " to a string, bool, int, or list property, or stop using " + owner + " from the screen.", member, source);
				return false;
			}

			if (!Property(property, records, enums, owner, source, out var field, out error) || field is null)
				return false;
			fields.Add(field);
		}

		Field? list = null;
		foreach (var field in fields)
		{
			if (field.Kind == ValueKind.List)
				list = field;
		}

		if (list is not null && records.TryGetValue(list.Item, out var itemFields))
		{
			var nameIndex = itemFields.FindIndex(item => item.Kind == ValueKind.Str && item.Name == "Name");
			if (nameIndex < 0)
				nameIndex = itemFields.FindIndex(item => item.Kind == ValueKind.Str);
			var flagIndex = itemFields.FindIndex(item => item.Kind == ValueKind.Bool);
			foreach (var field in fields)
			{
				if (field.Kind is ValueKind.FlagCount or ValueKind.ListText)
					field.BindList(list.Name, nameIndex, flagIndex);
			}
		}

		if (!records.TryAdd(type.Identifier.ValueText, fields))
		{
			error = At(UnknownMember, "The screen subset does not include " + type.Identifier.ValueText + ".", type, source);
			return false;
		}

		return true;
	}

	static bool Property(PropertyDeclarationSyntax property, Dictionary<string, List<Field>> records, IReadOnlyDictionary<string, Dictionary<string, int>> enums, string? owner, string source, out Field? field, out SubsetDiagnostic? error)
	{
		field = null;
		error = null;
		var name = property.Identifier.ValueText;
		if (IsList(property.Type, out var itemType))
		{
			if (!records.ContainsKey(itemType))
			{
				error = PlainType(property, owner, source);
				return false;
			}

			if (!Rows(property.Initializer?.Value, records[itemType], source, out var rows, out error) || rows is null)
				return false;
			field = Field.List(name, itemType, rows);
			return true;
		}

		var kind = KindOf(property.Type, enums);
		if (kind is null)
		{
			error = PlainType(property, owner, source);
			return false;
		}

		if (IsComputed(property))
		{
			if (kind == ValueKind.Int)
			{
				field = Field.Scalar(name, ValueKind.FlagCount, "", false, 0);
				return true;
			}

			if (kind == ValueKind.Str)
			{
				field = Field.Scalar(name, ValueKind.ListText, "", false, 0);
				return true;
			}

			error = PlainType(property, owner, source);
			return false;
		}

		var literal = property.Initializer?.Value;
		if (literal is null)
		{
			field = Field.Scalar(name, kind.Value, "", false, 0);
			return true;
		}

		if (literal is LiteralExpressionSyntax token)
		{
			if (kind == ValueKind.Str && token.IsKind(SyntaxKind.StringLiteralExpression))
			{
				field = Field.Scalar(name, kind.Value, token.Token.ValueText, false, 0);
				return true;
			}

			if (kind == ValueKind.Bool && token.IsKind(SyntaxKind.TrueLiteralExpression))
			{
				field = Field.Scalar(name, kind.Value, "", true, 0);
				return true;
			}

			if (kind == ValueKind.Bool && token.IsKind(SyntaxKind.FalseLiteralExpression))
			{
				field = Field.Scalar(name, kind.Value, "", false, 0);
				return true;
			}

			if (kind == ValueKind.Int && token.IsKind(SyntaxKind.NumericLiteralExpression) && token.Token.Value is int number)
			{
				field = Field.Scalar(name, kind.Value, "", false, number);
				return true;
			}
		}

		if (kind == ValueKind.Int
			&& literal is MemberAccessExpressionSyntax access
			&& access.Expression is IdentifierNameSyntax enumName
			&& enums.TryGetValue(enumName.Identifier.ValueText, out var values)
			&& values.TryGetValue(access.Name.Identifier.ValueText, out var enumValue))
		{
			field = Field.Scalar(name, kind.Value, "", false, enumValue);
			return true;
		}

		error = owner is null
			? At(UnknownExpression, "The screen subset does not include this expression.", literal, source)
			: At(UnknownExpression, owner + "." + name + " starts with an expression the subset cannot record. Change " + name + " to a literal, or stop using " + owner + " from the screen.", literal, source);
		return false;
	}

	static SubsetDiagnostic PlainType(PropertyDeclarationSyntax property, string? owner, string source)
	{
		if (owner is null)
			return At(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", property, source);
		var name = property.Identifier.ValueText;
		return At(UnknownType, owner + "." + name + " is not a string, a bool, an int, or a list of records. Change " + name + ", or stop using " + owner + " from the screen.", property, source);
	}

	static bool Rows(ExpressionSyntax? expression, List<Field> fields, string source, out List<string[]>? rows, out SubsetDiagnostic? error)
	{
		rows = new List<string[]>();
		error = null;
		if (expression is null)
			return true;
		var elements = ElementsOf(expression);
		if (elements is null)
		{
			error = At(UnknownExpression, "The screen subset does not include this expression.", expression, source);
			return false;
		}

		foreach (var element in elements)
		{
			if (!Row(element, fields, source, out var cells, out error) || cells is null)
				return false;
			rows.Add(cells);
		}

		return true;
	}

	static bool Row(ExpressionSyntax expression, List<Field> fields, string source, out string[]? cells, out SubsetDiagnostic? error)
	{
		cells = null;
		error = null;
		InitializerExpressionSyntax? initializer = expression switch
		{
			ImplicitObjectCreationExpressionSyntax created => created.Initializer,
			ObjectCreationExpressionSyntax created => created.Initializer,
			_ => null,
		};
		if (initializer is null)
		{
			error = At(UnknownExpression, "The screen subset does not include this expression.", expression, source);
			return false;
		}

		cells = new string[fields.Count];
		Array.Fill(cells, "");
		foreach (var assignment in initializer.Expressions)
		{
			if (assignment is not AssignmentExpressionSyntax assign
				|| assign.Left is not IdentifierNameSyntax name
				|| assign.Right is not LiteralExpressionSyntax literal)
			{
				error = At(UnknownExpression, "The screen subset does not include this expression.", assignment, source);
				return false;
			}

			var index = fields.FindIndex(field => field.Name == name.Identifier.ValueText);
			if (index < 0)
			{
				error = At(UnknownMember, "The screen subset does not include " + name.Identifier.ValueText + ".", name, source);
				return false;
			}

			if (fields[index].Kind == ValueKind.Str && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				cells[index] = literal.Token.ValueText;
				continue;
			}

			if (fields[index].Kind == ValueKind.Bool && literal.IsKind(SyntaxKind.TrueLiteralExpression))
			{
				cells[index] = "true";
				continue;
			}

			if (fields[index].Kind == ValueKind.Bool && literal.IsKind(SyntaxKind.FalseLiteralExpression))
			{
				cells[index] = "false";
				continue;
			}

			error = At(UnknownExpression, "The screen subset does not include this expression.", literal, source);
			return false;
		}

		return true;
	}

	static IReadOnlyList<ExpressionSyntax>? ElementsOf(ExpressionSyntax expression)
	{
		switch (expression)
		{
			case CollectionExpressionSyntax collection:
			{
				var elements = new List<ExpressionSyntax>();
				foreach (var element in collection.Elements)
				{
					if (element is not ExpressionElementSyntax item)
						return null;
					elements.Add(item.Expression);
				}

				return elements;
			}
			case ObjectCreationExpressionSyntax creation when creation.Initializer is not null:
			{
				var elements = new List<ExpressionSyntax>();
				foreach (var element in creation.Initializer.Expressions)
				{
					if (element is not ExpressionSyntax item)
						return null;
					elements.Add(item);
				}

				return elements;
			}
			case ObjectCreationExpressionSyntax creation when creation.Initializer is null && IsList(creation.Type, out _):
				return Array.Empty<ExpressionSyntax>();
			case ImplicitObjectCreationExpressionSyntax created when created.Initializer is null:
				return Array.Empty<ExpressionSyntax>();
			case ImplicitArrayCreationExpressionSyntax array when array.Initializer is not null:
				return array.Initializer.Expressions;
			default:
				return null;
		}
	}

	static bool IsList(TypeSyntax type, out string itemType)
	{
		itemType = "";
		if (type is not GenericNameSyntax generic || generic.Identifier.ValueText is not ("List" or "IReadOnlyList"))
			return false;
		if (generic.TypeArgumentList.Arguments.Count != 1)
			return false;
		itemType = generic.TypeArgumentList.Arguments[0].ToString();
		return true;
	}

	static bool IsComputed(PropertyDeclarationSyntax property)
	{
		if (property.Initializer is not null)
			return false;
		if (property.ExpressionBody is not null)
			return true;
		return property.AccessorList is { Accessors.Count: 1 } list
			&& list.Accessors[0].IsKind(SyntaxKind.GetAccessorDeclaration);
	}

	static ValueKind? KindOf(TypeSyntax type, IReadOnlyDictionary<string, Dictionary<string, int>> enums)
	{
		var text = type.ToString();
		switch (text)
		{
			case "string" or "string?":
				return ValueKind.Str;
			case "bool" or "bool?":
				return ValueKind.Bool;
			case "int":
				return ValueKind.Int;
			default:
				if (enums.ContainsKey(text) || (text.EndsWith('?') && enums.ContainsKey(text[..^1])))
					return ValueKind.Int;
				return null;
		}
	}

	static SyntaxList<MemberDeclarationSyntax>? MembersOf(CompilationUnitSyntax root)
	{
		if (root.Members is [FileScopedNamespaceDeclarationSyntax file])
			return file.Members;
		if (root.Members is [NamespaceDeclarationSyntax space])
			return space.Members;
		return null;
	}

	static string MemberName(SyntaxNode member) =>
		member switch
		{
			MethodDeclarationSyntax method => method.Identifier.ValueText,
			PropertyDeclarationSyntax property => property.Identifier.ValueText,
			FieldDeclarationSyntax field when field.Declaration.Variables.Count == 1 => field.Declaration.Variables[0].Identifier.ValueText,
			_ => member.Kind().ToString(),
		};

	static SubsetDiagnostic At(string id, string message, SyntaxNode node, string source) =>
		At(id, message, node.GetLocation(), source);

	static SubsetDiagnostic At(string id, string message, Microsoft.CodeAnalysis.Location location, string source)
	{
		var span = location.SourceSpan;
		var line = location.GetLineSpan().StartLinePosition;
		var start = span.Start;
		var end = span.End;
		if (start < 0 || end < start || end > source.Length)
		{
			start = 0;
			end = 0;
		}

		return new SubsetDiagnostic(id, message, start, end, line.Line + 1, line.Character + 1);
	}

	enum ValueKind
	{
		Str,
		Bool,
		Int,
		List,
		Double,
		FlagCount,
		ListText,
	}

	sealed class Field
	{
		Field(string name, ValueKind kind, string text, bool flag, int number, string item, List<string[]>? rows)
		{
			Name = name;
			Kind = kind;
			Text = text;
			Flag = flag;
			Number = number;
			Item = item;
			Rows = rows;
		}

		public string Name { get; }

		public ValueKind Kind { get; }

		public string Text { get; }

		public bool Flag { get; }

		public int Number { get; }

		public string Item { get; }

		public List<string[]>? Rows { get; }

		public string SourceList { get; private set; } = "";

		public int NameIndex { get; private set; } = -1;

		public int FlagIndex { get; private set; } = -1;

		public void BindList(string list, int nameIndex, int flagIndex)
		{
			SourceList = list;
			NameIndex = nameIndex;
			FlagIndex = flagIndex;
		}

		public static Field Scalar(string name, ValueKind kind, string text, bool flag, int number) =>
			new(name, kind, text, flag, number, "", null);

		public static Field List(string name, string item, List<string[]> rows) =>
			new(name, ValueKind.List, "", false, 0, item, rows);
	}

	sealed partial class Lower
	{
		readonly string _source;
		readonly string _state;
		readonly string _platform;
		readonly List<Field> _fields;
		readonly Dictionary<string, List<Field>> _records;
		readonly Dictionary<string, string> _consts;
		readonly Dictionary<string, MethodDeclarationSyntax> _methods;
		readonly string _stateType;
		readonly Builder _code = new();
		string _item = "";
		List<Field>? _itemFields;
		string _rows = "";
		Style? _pendingStyle;
		Motion? _pendingMotion;
		ExpressionSyntax? _pendingDescription;
		bool _pendingDecorative;
		int _pendingSide;
		string? _pendingEventName;
		byte[]? _pendingEventScript;
		readonly List<(byte Kind, byte[] Script)> _pendingGestures = new();
		Keyboard _pendingKeyboard;
		int _pendingLines;
		Submit _pendingSubmit;
		bool _pendingAutofill;
		byte[]? _pendingSubmitScript;
		string _listName = "";
		readonly Dictionary<string, LocalFn> _localFns = new(StringComparer.Ordinal);
		readonly HashSet<string> _bindings = new(StringComparer.Ordinal);
		readonly List<(bool Works, int Length)> _actions = new();
		string _originFile = "src/NativePoc.Screen/Screen.cs";
		Dictionary<string, (byte Slot, ValueKind Kind)>? _locals;
		byte _localSlots;

		public Lower(
			string source,
			string state,
			string platform,
			List<Field> fields,
			Dictionary<string, List<Field>> records,
			Dictionary<string, string> consts,
			Dictionary<string, MethodDeclarationSyntax> methods,
			string stateType,
			Dictionary<string, MethodDeclarationSyntax>? components = null,
			Dictionary<string, AppSig>? calls = null)
		{
			_source = source;
			_state = state;
			_platform = platform;
			_fields = fields;
			_records = records;
			_consts = consts;
			_methods = methods;
			_stateType = stateType;
			_components = components ?? new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
			_calls = calls ?? new Dictionary<string, AppSig>(StringComparer.Ordinal);
			_callTypes = new HashSet<string>(StringComparer.Ordinal);
			foreach (var call in _calls.Values)
				_callTypes.Add(call.Type);
		}

		public IReadOnlyList<(bool Works, int Length)> Actions => _actions;

		void EnsureLocals()
		{
			if (_locals is not null)
				return;
			_locals = new Dictionary<string, (byte Slot, ValueKind Kind)>(StringComparer.Ordinal);
			_localSlots = 0;
		}

		public void SetOrigin(string? path)
		{
			if (!string.IsNullOrEmpty(path))
				_originFile = path.Replace('\\', '/');
		}

		public List<SubsetDiagnostic> Errors { get; } = new();

		public bool Body(MethodDeclarationSyntax method)
		{
			if (!Seed() || !LowerRoutines())
				return false;
			if (method.ExpressionBody is not null)
				return Element(method.ExpressionBody.Expression, null) && Return();
			if (method.Body is null)
				return Fail(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", method);
			return Block(method.Body);
		}

		public byte[] Finish()
		{
			if (_consts.TryGetValue("ScreenId", out var id) && id.Length > 0)
			{
				_code.Prop(new RecordProgram.PropDef(
					RecordProgram.TypeScreen,
					_code.Intern("ScreenId"),
					_code.Intern(id),
					false,
					0,
					Array.Empty<int>(),
					Array.Empty<int[]>()));
			}

			return _code.Finish(_source);
		}

		bool Seed()
		{
			foreach (var field in _fields)
			{
				var name = _code.Intern(field.Name);
				switch (field.Kind)
				{
					case ValueKind.Str:
						_code.Prop(new RecordProgram.PropDef(RecordProgram.TypeString, name, _code.Intern(field.Text), false, 0, Array.Empty<int>(), Array.Empty<int[]>()));
						break;
					case ValueKind.Bool:
						_code.Prop(new RecordProgram.PropDef(RecordProgram.TypeBool, name, 0, field.Flag, 0, Array.Empty<int>(), Array.Empty<int[]>()));
						break;
					case ValueKind.Int:
						_code.Prop(new RecordProgram.PropDef(RecordProgram.TypeInt, name, 0, false, field.Number, Array.Empty<int>(), Array.Empty<int[]>()));
						break;
					case ValueKind.FlagCount:
					case ValueKind.ListText:
						break;
					case ValueKind.List:
						if (field.Rows is null || !_records.TryGetValue(field.Item, out var itemFields))
							return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
						var names = new int[itemFields.Count];
						for (var index = 0; index < itemFields.Count; index++)
						{
							if (itemFields[index].Kind is not (ValueKind.Str or ValueKind.Bool))
								return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
							names[index] = _code.Intern(itemFields[index].Name);
						}

						var rows = new int[field.Rows.Count][];
						for (var row = 0; row < field.Rows.Count; row++)
						{
							rows[row] = new int[names.Length];
							for (var cell = 0; cell < names.Length; cell++)
							{
								var text = cell < field.Rows[row].Length ? field.Rows[row][cell] : "";
								if (itemFields[cell].Kind == ValueKind.Bool && text.Length == 0)
									text = "false";
								rows[row][cell] = _code.Intern(text);
							}
						}

						_code.Prop(new RecordProgram.PropDef(RecordProgram.TypeList, name, 0, false, 0, names, rows));
						break;
					default:
						return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
				}
			}

			return Errors.Count == 0;
		}

		readonly struct LocalFn
		{
			public LocalFn(LocalFunctionStatementSyntax syntax)
			{
				Block = syntax.Body;
				Expression = syntax.ExpressionBody?.Expression;
			}

			public BlockSyntax? Block { get; }

			public ExpressionSyntax? Expression { get; }
		}

		bool HoistLocals(SyntaxNode root)
		{
			foreach (var localFn in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
			{
				if (localFn.Body is null && localFn.ExpressionBody is null)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", localFn);
				var name = localFn.Identifier.ValueText;
				if (_localFns.ContainsKey(name))
					return Fail(BadLambda, "The local function " + name + " is already in this screen.", localFn);
				_localFns.Add(name, new LocalFn(localFn));
			}

			return true;
		}

		bool EmitLocal(LocalFn function, string parameter, List<byte> script, SyntaxNode anchor)
		{
			EnsureLocals();
			if (function.Expression is not null)
				return Action(function.Expression, parameter, script);
			if (function.Block is null)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", anchor);
			foreach (var statement in function.Block.Statements)
			{
				if (statement is LocalFunctionStatementSyntax)
					continue;
				if (!Keep(statement, parameter, script))
					return false;
			}

			return true;
		}

		bool Block(BlockSyntax block)
		{
			if (!HoistLocals(block))
				return false;
			var statements = new List<StatementSyntax>();
			foreach (var statement in block.Statements)
			{
				if (statement is LocalFunctionStatementSyntax)
					continue;
				statements.Add(statement);
			}

			var rest = new List<StatementSyntax>();
			foreach (var statement in statements)
			{
				if (!TryOpen(statement, out var opened))
					return false;
				if (!opened)
					rest.Add(statement);
			}

			statements = rest;
			var index = 0;
			while (index < statements.Count)
			{
				if (!TryBinding(statements[index], out var consumed))
					return false;
				if (!consumed && !TryEffect(statements[index], out consumed))
					return false;
				if (!consumed && !TrySeed(statements[index], out consumed))
					return false;
				if (!consumed && !TryLocalCall(statements[index], out consumed))
					return false;
				if (!consumed)
					break;
				index++;
			}

			while (index < statements.Count
				&& statements[index] is IfStatementSyntax branch
				&& branch.Else is null
				&& ReturnOf(branch.Statement) is ReturnStatementSyntax whenTrue)
			{
				if (!Condition(branch.Condition))
					return false;
				var jump = _code.EmitJump(RecordProgram.OpJumpIfFalse);
				if (!Returned(whenTrue))
					return false;
				_code.Patch(jump);
				index++;
			}

			if (index == statements.Count - 1
				&& statements[index] is IfStatementSyntax choice
				&& choice.Else is not null
				&& ReturnOf(choice.Statement) is ReturnStatementSyntax whenChosen
				&& ReturnOf(choice.Else.Statement) is ReturnStatementSyntax whenOther)
			{
				if (!Condition(choice.Condition))
					return false;
				var skip = _code.EmitJump(RecordProgram.OpJumpIfFalse);
				if (!Returned(whenChosen))
					return false;
				_code.Patch(skip);
				return Returned(whenOther);
			}

			if (index < statements.Count && statements[index] is LocalDeclarationStatementSyntax)
				return ListBlock(statements, index);
			if (index == statements.Count - 1 && statements[index] is ReturnStatementSyntax only)
				return Returned(only);
			if (statements.Count == 1 && statements[0] is ReturnStatementSyntax single)
				return Returned(single);
			return Fail(UnknownExpression, "The screen subset does not include this expression.", block);
		}

		internal void UseBindings(IEnumerable<string> bindings)
		{
			foreach (var binding in bindings)
				_bindings.Add(binding);
		}

		bool TryBinding(StatementSyntax statement, out bool consumed)
		{
			consumed = false;
			if (statement is not ExpressionStatementSyntax expr || expr.Expression is not InvocationExpressionSyntax invocation)
				return true;
			if (invocation.Expression is not MemberAccessExpressionSyntax member || member.Expression is not IdentifierNameSyntax owner)
				return true;
			if (!_bindings.Contains(owner.Identifier.ValueText + "." + member.Name.Identifier.ValueText))
				return true;
			return Fail(BadLambda, "The call " + owner.Identifier.ValueText + "." + member.Name.Identifier.ValueText + "() is compiled into the binary. The reload program does not skip it.", invocation);
		}

		bool TryEffect(StatementSyntax statement, out bool consumed)
		{
			consumed = false;
			if (statement is not ExpressionStatementSyntax expr || expr.Expression is not InvocationExpressionSyntax invocation)
				return true;
			if (invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _platform)
				return true;
			var name = member.Name.Identifier.ValueText;
			if (name is not ("Effect" or "Every"))
				return true;
			consumed = true;
			if (name == "Every")
			{
				if (invocation.ArgumentList.Arguments.Count != 1
					|| invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax tick
					|| !tick.IsKind(SyntaxKind.StringLiteralExpression))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
				var property = tick.Token.ValueText;
				if (_fields.All(field => field.Name != property || field.Kind != ValueKind.Int))
					return Fail(UnknownMember, "The screen subset does not include " + property + ".", invocation);
				_code.Op(RecordProgram.OpEvery);
				_code.U16(_code.Intern(property));
				return true;
			}

			if (invocation.ArgumentList.Arguments.Count != 2
				|| invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax propertyLiteral
				|| !propertyLiteral.IsKind(SyntaxKind.StringLiteralExpression)
				|| invocation.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax resourceLiteral
				|| !resourceLiteral.IsKind(SyntaxKind.StringLiteralExpression))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var target = propertyLiteral.Token.ValueText;
			if (_fields.All(field => field.Name != target || field.Kind != ValueKind.Str))
				return Fail(UnknownMember, "The screen subset does not include " + target + ".", invocation);
			_code.Op(RecordProgram.OpEffect);
			_code.U16(_code.Intern(target));
			_code.U16(_code.Intern(resourceLiteral.Token.ValueText));
			return true;
		}

		bool TrySeed(StatementSyntax statement, out bool consumed)
		{
			consumed = false;
			if (statement is not ExpressionStatementSyntax expr || expr.Expression is not InvocationExpressionSyntax invocation)
				return true;
			if (invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != "FeedModule"
				|| member.Name.Identifier.ValueText != "Generate")
				return true;
			consumed = true;
			if (invocation.ArgumentList.Arguments.Count != 2
				|| invocation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax platformName
				|| platformName.Identifier.ValueText != _platform
				|| invocation.ArgumentList.Arguments[1].Expression is not IdentifierNameSyntax stateName
				|| stateName.Identifier.ValueText != _state)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var list = _fields.FirstOrDefault(field => field.Name == "Generated" && field.Kind == ValueKind.List);
			if (list is null || !_records.TryGetValue(list.Item, out var itemFields))
				return Fail(UnknownMember, "The screen subset does not include Generated.", invocation);
			var idField = itemFields.FindIndex(field => field.Name == "Id");
			var titleField = itemFields.FindIndex(field => field.Name == "Title");
			if (idField < 0 || titleField < 0)
				return Fail(UnknownMember, "The screen subset does not include Generated.", invocation);
			_code.Op(RecordProgram.OpSeedList);
			_code.U16(_code.Intern(list.Name));
			_code.U16(1000);
			_code.U16(itemFields.Count);
			_code.U16(idField);
			_code.U16(titleField);
			_code.U16(_code.Intern("Row "));
			return true;
		}

		bool TryOpen(StatementSyntax statement, out bool consumed)
		{
			consumed = false;
			if (statement is not ExpressionStatementSyntax expr || expr.Expression is not InvocationExpressionSyntax invocation)
				return true;
			if (invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _platform
				|| member.Name.Identifier.ValueText != "OnOpen")
				return true;
			consumed = true;
			var args = invocation.ArgumentList.Arguments;
			if (args.Count != 2)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			string? screen = null;
			if (args[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				screen = literal.Token.ValueText;
			else if (args[0].Expression is IdentifierNameSyntax named && _consts.TryGetValue(named.Identifier.ValueText, out var constant))
				screen = constant;
			if (string.IsNullOrEmpty(screen))
				return Fail(BadLambda, "OnOpen takes the screen id and a local function.", invocation);
			if (args[1].Expression is not IdentifierNameSyntax called || !_localFns.TryGetValue(called.Identifier.ValueText, out var body))
				return Fail(BadLambda, "OnOpen takes the screen id and a local function.", args[1]);
			var script = new List<byte>();
			if (!EmitLocal(body, "", script, args[1]))
				return false;

			if (script.Count > ushort.MaxValue)
				return Fail(BadLambda, "OnOpen takes the screen id and a local function.", invocation);
			_code.Op(RecordProgram.OpOnOpen);
			_code.U16(_code.Intern(screen));
			_code.U16(script.Count);
			_code.Bytes(script.ToArray());
			return true;
		}

		bool TryLocalCall(StatementSyntax statement, out bool consumed)
		{
			consumed = false;
			if (statement is not ExpressionStatementSyntax expr || expr.Expression is not InvocationExpressionSyntax invocation)
				return true;
			if (invocation.Expression is not IdentifierNameSyntax called)
				return true;
			var name = called.Identifier.ValueText;
			if (!_localFns.TryGetValue(name, out var body))
				return Fail(BadLambda, "The call " + name + "() is not a local function in this screen. The reload program does not skip it.", invocation);
			if (invocation.ArgumentList.Arguments.Count != 0)
				return Fail(BadLambda, "The call " + name + "() takes arguments the screen subset cannot pass.", invocation);
			var script = new List<byte>();
			if (!EmitLocal(body, "", script, invocation))
				return false;

			if (script.Count > ushort.MaxValue)
				return Fail(BadLambda, "The call " + name + "() is too large to lower.", invocation);
			consumed = true;
			_code.Op(RecordProgram.OpRun);
			_code.U16(script.Count);
			if (script.Count > 0)
				_code.Bytes(script.ToArray());
			return true;
		}

		bool ListBlock(IReadOnlyList<StatementSyntax> statements, int index)
		{
			if (statements[index] is not LocalDeclarationStatementSyntax local)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", statements[index]);
			if (local.Declaration.Variables.Count != 1)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", local);
			var variable = local.Declaration.Variables[0];
			_rows = variable.Identifier.ValueText;
			if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax created || !created.Type.ToString().StartsWith("List<Element>", StringComparison.Ordinal))
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", variable);
			var prefix = new List<ExpressionSyntax>();
			var suffix = new List<ExpressionSyntax>();
			ForEachStatementSyntax? loop = null;
			for (var cursor = index + 1; cursor < statements.Count - 1; cursor++)
			{
				if (statements[cursor] is ForEachStatementSyntax found)
				{
					if (loop is not null)
						return Fail(BadForeach, "A foreach walks a list property and adds one element.", found);
					loop = found;
					continue;
				}

				if (!Added(statements[cursor], out var element))
					return false;
				if (loop is null)
					prefix.Add(element);
				else
					suffix.Add(element);
			}

			if (loop is null || statements[^1] is not ReturnStatementSyntax ret)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", local);
			return Foreach(local, loop, ret, prefix, suffix);
		}

		bool Added(StatementSyntax statement, out ExpressionSyntax element)
		{
			element = null!;
			if (statement is not ExpressionStatementSyntax expr
				|| expr.Expression is not InvocationExpressionSyntax add
				|| add.Expression is not MemberAccessExpressionSyntax target
				|| target.Name.Identifier.ValueText != "Add"
				|| target.Expression is not IdentifierNameSyntax rows
				|| rows.Identifier.ValueText != _rows
				|| add.ArgumentList.Arguments.Count != 1)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", statement);
			element = add.ArgumentList.Arguments[0].Expression;
			return true;
		}

		bool Foreach(LocalDeclarationStatementSyntax local, ForEachStatementSyntax loop, ReturnStatementSyntax ret, List<ExpressionSyntax> prefix, List<ExpressionSyntax> suffix)
		{
			_ = local;
			if (loop.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _state)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", loop.Expression);
			var list = _fields.FirstOrDefault(field => field.Name == member.Name.Identifier.ValueText && field.Kind == ValueKind.List);
			if (list is null || !_records.TryGetValue(list.Item, out var itemFields))
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", member);
			var statement = loop.Statement is BlockSyntax body && body.Statements.Count == 1 ? body.Statements[0] : loop.Statement;
			if (!Added(statement, out var added))
				return false;
			var keys = new KeySet();
			_code.Op(RecordProgram.OpMark);
			foreach (var element in prefix)
			{
				if (!Element(element, keys))
					return false;
			}

			_code.Op(RecordProgram.OpForEach);
			_code.U16(_code.Intern(list.Name));
			var lengthAt = _code.Here;
			_code.U16(0);
			var start = _code.Here;
			_item = loop.Identifier.ValueText;
			_itemFields = itemFields;
			_listName = list.Name;
			if (!Element(added, keys))
				return false;
			_item = "";
			_itemFields = null;
			_listName = "";
			var length = _code.Here - start;
			_code.PatchAt(lengthAt, length);
			if (!RefreshOf(ret, out var refresh))
				return false;
			if (refresh is { Length: > 0 })
			{
				_code.Op(RecordProgram.OpGesture);
				_code.U8(Gestures.Refresh);
				_code.U16(refresh.Length);
				_code.Bytes(refresh);
			}
			foreach (var element in suffix)
			{
				if (!Element(element, keys))
					return false;
			}

			if (ret.Expression is null)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", ret);
			return Gathered(ret.Expression, keys) && Return();
		}

		bool RefreshOf(ReturnStatementSyntax ret, out byte[]? script)
		{
			script = null;
			if (ret.Expression is not InvocationExpressionSyntax window
				|| window.Expression is not MemberAccessExpressionSyntax windowCall
				|| windowCall.Name.Identifier.ValueText != "Window"
				|| windowCall.Expression is not IdentifierNameSyntax windowType
				|| windowType.Identifier.ValueText != "FeedRows")
				return true;
			if (window.ArgumentList.Arguments.Count == 2)
				return true;
			if (window.ArgumentList.Arguments.Count != 3
				|| window.ArgumentList.Arguments[2].Expression is not LambdaExpressionSyntax lambda)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", window);
			if (!Script(lambda, out var body) || body.Length == 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", lambda);
			script = body;
			return true;
		}

		bool Gathered(ExpressionSyntax expression, KeySet keys)
		{
			if (expression is InvocationExpressionSyntax window
				&& window.Expression is MemberAccessExpressionSyntax windowCall
				&& windowCall.Name.Identifier.ValueText == "Window"
				&& windowCall.Expression is IdentifierNameSyntax windowType
				&& windowType.Identifier.ValueText == "FeedRows"
				&& window.ArgumentList.Arguments.Count is 2 or 3
				&& window.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax windowRows
				&& windowRows.Identifier.ValueText == _rows
				&& window.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax windowCount
				&& windowCount.IsKind(SyntaxKind.NumericLiteralExpression))
			{
				if (!keys.Close(this, window))
					return false;
				_code.Op(RecordProgram.OpGather);
				_code.U16(_code.Intern("feed"));
				EmitStyle();
				return true;
			}

			if (expression is ObjectCreationExpressionSyntax creation && TypeName(creation) == "ScrollView")
			{
				if (creation.ArgumentList?.Arguments.Count != 1)
					return Fail(UnknownElement, "The screen subset does not include ScrollView.", creation);
				if (!Gathered(creation.ArgumentList.Arguments[0].Expression, keys))
					return false;
				if (!KeyOf(creation, out var key, out var dynamic, out _))
					return false;
				if (dynamic)
					return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
				_code.Op(RecordProgram.OpMakeScroll);
				_code.U16(key);
				EmitStyle();
				return true;
			}

			if (expression is ObjectCreationExpressionSyntax listed && TypeName(listed) == "ListView")
				return GatherRows(listed, keys, RecordProgram.OpMakeList);
			if (expression is not ObjectCreationExpressionSyntax column || TypeName(column) != "Column")
				return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			if (column.ArgumentList?.Arguments.Count != 1
				|| column.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax name
				|| name.Identifier.ValueText != _rows)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", column);
			if (!KeyOf(column, out var columnKey, out var columnDynamic, out _))
				return false;
			if (columnDynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", column);
			if (!keys.Close(this, column))
				return false;
			_code.Op(RecordProgram.OpGather);
			_code.U16(columnKey);
			EmitStyle();
			return true;
		}

		bool GatherRows(ObjectCreationExpressionSyntax creation, KeySet keys, byte op)
		{
			if (creation.ArgumentList?.Arguments.Count != 1
				|| creation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax name
				|| name.Identifier.ValueText != _rows)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", creation);
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Close(this, creation))
				return false;
			_code.Op(op);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		static ReturnStatementSyntax? ReturnOf(StatementSyntax statement)
		{
			if (statement is ReturnStatementSyntax direct)
				return direct;
			if (statement is BlockSyntax block && block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax nested)
				return nested;
			return null;
		}

		bool Returned(ReturnStatementSyntax statement)
		{
			if (statement.Expression is null)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", statement);
			return Element(statement.Expression, new KeySet()) && Return();
		}

		bool Return()
		{
			_code.Op(RecordProgram.OpReturn);
			return true;
		}

		bool Element(ExpressionSyntax expression, KeySet? keys)
		{
			if (expression is ParenthesizedExpressionSyntax paren)
				return Element(paren.Expression, keys);
			if (expression is ConditionalExpressionSyntax conditional)
				return Choice(conditional, arm => Element(arm, new KeySet()));
			if (expression is InvocationExpressionSyntax localRow
				&& localRow.Expression is IdentifierNameSyntax localName
				&& _localFns.TryGetValue(localName.Identifier.ValueText, out var localBody)
				&& localRow.ArgumentList.Arguments.Count == 1
				&& localRow.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax localArg
				&& localArg.Identifier.ValueText == _item
				&& _item.Length > 0)
			{
				if (localBody.Expression is not null)
					return Element(localBody.Expression, keys);
				if (localBody.Block is null
					|| localBody.Block.Statements.Count != 1
					|| localBody.Block.Statements[0] is not ReturnStatementSyntax localReturn
					|| localReturn.Expression is null)
					return Fail(UnknownElement, "The screen subset does not include " + localName.Identifier.ValueText + ".", localRow);
				return Element(localReturn.Expression, keys);
			}

			if (expression is InvocationExpressionSyntax helper
				&& helper.Expression is IdentifierNameSyntax called
				&& _routines.TryGetValue(called.Identifier.ValueText, out var routine)
				&& routine.Element)
			{
				if (helper.ArgumentList.Arguments.Count != 1
					|| helper.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax argument
					|| argument.Identifier.ValueText != _item)
					return Fail(UnknownElement, "The screen subset does not include " + called.Identifier.ValueText + ".", helper);
				_code.Op(RecordProgram.OpCall);
				_code.U16(routine.Index);
				return true;
			}

			if (expression is InvocationExpressionSyntax componentCall
				&& componentCall.Expression is MemberAccessExpressionSyntax componentMember
				&& componentMember.Expression is IdentifierNameSyntax componentType
				&& _components.TryGetValue(componentType.Identifier.ValueText + "." + componentMember.Name.Identifier.ValueText, out var component))
				return EmitComponent(component, componentCall, keys);

			if (expression is not ObjectCreationExpressionSyntax creation)
				return Fail(UnknownElement, "The screen subset does not include " + expression.ToString() + ".", expression);
			var type = TypeName(creation);
			var set = keys ?? new KeySet();
			switch (type)
			{
				case "Text":
					return Text(creation, set);
				case "Button":
					return Control(creation, set, RecordProgram.OpMakeButton, 3);
				case "TextField":
				case "TextInput":
					return Control(creation, set, RecordProgram.OpMakeField, 4);
				case "Switch":
					return Control(creation, set, RecordProgram.OpMakeSwitch, 3);
				case "Column":
					return Column(creation, set);
				case "ScrollView":
					return Scroll(creation, set);
				case "ListView":
					return List(creation, set);
				case "Image":
					return Image(creation, set);
				case "HostView":
					return Host(creation, set);
				default:
					return Fail(UnknownElement, "The screen subset does not include " + type + ".", creation);
			}
		}

		bool Text(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			if (creation.ArgumentList?.Arguments.Count != 1)
				return Fail(UnknownElement, "The screen subset does not include Text.", creation);
			if (!TextValue(creation.ArgumentList.Arguments[0].Expression))
				return false;
			if (!KeyOf(creation, out var key, out var dynamic, out var field))
				return false;
			if (!keys.Add(this, key, dynamic, creation))
				return false;
			if (dynamic)
			{
				_code.Op(RecordProgram.OpItemStr);
				_code.U16(field);
				_code.Op(RecordProgram.OpMakeTextKey);
				EmitStyle();
				return true;
			}

			_code.Op(RecordProgram.OpMakeText);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		bool Image(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			if (creation.ArgumentList?.Arguments.Count != 1)
				return Fail(UnknownElement, "The screen subset does not include Image.", creation);
			var source = creation.ArgumentList.Arguments[0].Expression;
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			if (_pendingDescription is { } description)
			{
				if (!TextValue(description))
					return false;
			}
			else
			{
				_code.Op(RecordProgram.OpPushStr);
				_code.U16(_code.Intern(""));
			}

			if (!TextValue(source))
				return false;
			_code.Op(RecordProgram.OpMakeImage);
			_code.U16(key);
			_code.U8((byte)(_pendingDecorative ? 1 : 0));
			_code.U8((byte)Math.Clamp(_pendingSide, 0, 255));
			EmitStyle();
			return true;
		}

		bool Host(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			if (creation.ArgumentList is not { } arguments || arguments.Arguments.Count != 3)
				return Fail(UnknownElement, "A host view takes a kind, a prop name, and a value.", creation);
			var args = arguments.Arguments;
			if (args[0].Expression is not LiteralExpressionSyntax kind || !kind.IsKind(SyntaxKind.StringLiteralExpression))
				return Fail(UnknownExpression, "The screen subset does not include this expression.", args[0].Expression);
			if (args[1].Expression is not LiteralExpressionSyntax prop || !prop.IsKind(SyntaxKind.StringLiteralExpression))
				return Fail(UnknownExpression, "The screen subset does not include this expression.", args[1].Expression);
			if (!PushLiteral(kind.Token.ValueText) || !PushLiteral(prop.Token.ValueText))
				return false;
			if (!TextValue(args[2].Expression))
				return false;
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			_code.Op(RecordProgram.OpMakeHost);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		bool Control(ObjectCreationExpressionSyntax creation, KeySet keys, byte op, int count)
		{
			if (creation.ArgumentList?.Arguments.Count != count)
				return Fail(UnknownElement, "The screen subset does not include " + TypeName(creation) + ".", creation);
			var args = creation.ArgumentList.Arguments;
			byte[] script;
			if (op == RecordProgram.OpMakeField)
			{
				if (!TextValue(args[0].Expression))
					return false;
				if (args[1].Expression is not LambdaExpressionSyntax fieldLambda)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				if (!TextValue(args[2].Expression) || !TextValue(args[3].Expression))
					return false;
				if (!Script(fieldLambda, out script))
					return false;
			}
			else if (op == RecordProgram.OpMakeSwitch)
			{
				if (!Bool(args[0].Expression))
					return false;
				if (args[1].Expression is not LambdaExpressionSyntax switchLambda)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				if (!TextValue(args[2].Expression))
					return false;
				if (!Script(switchLambda, out script))
					return false;
			}
			else
			{
				if (!TextValue(args[0].Expression))
					return false;
				if (args[1].Expression is not LambdaExpressionSyntax buttonLambda)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				if (!TextValue(args[2].Expression))
					return false;
				if (!Script(buttonLambda, out script))
					return false;
			}

			return FinishControl(creation, keys, op, script);
		}

		byte[] Stamp(byte[] script, SyntaxNode node)
		{
			var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
			var text = _originFile + ":" + line.ToString(CultureInfo.InvariantCulture);
			var id = _code.Intern(text);
			var stamped = new byte[script.Length + 3];
			stamped[0] = RecordProgram.ActSource;
			stamped[1] = (byte)id;
			stamped[2] = (byte)(id >> 8);
			script.CopyTo(stamped, 3);
			return stamped;
		}

		bool FinishControl(ObjectCreationExpressionSyntax creation, KeySet keys, byte op, byte[] script)
		{
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			if (script.Length == 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", creation);
			script = Stamp(script, creation);
			_code.Op(op);
			_code.U16(key);
			_code.U16(script.Length);
			_code.Bytes(script);
			EmitStyle();
			EmitFieldMode();
			return true;
		}

		bool Column(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			var children = ColumnChildren(creation);
			if (children is null)
				return Fail(UnknownElement, "The screen subset does not include Column.", creation);
			var nested = new KeySet();
			foreach (var child in children)
			{
				if (!Element(child, nested))
					return false;
			}

			if (!nested.Close(this, creation))
				return false;
			if (!KeyOf(creation, out var key, out var dynamic, out var field))
				return false;
			if (!keys.Add(this, key, dynamic, creation))
				return false;
			if (children.Count > RecordProgram.MaxRows)
				return Fail(UnknownElement, "The screen subset does not include Column.", creation);
			if (dynamic)
			{
				_code.Op(RecordProgram.OpItemStr);
				_code.U16(field);
				_code.Op(RecordProgram.OpMakeColumnKey);
				_code.U8((byte)children.Count);
				EmitStyle();
				return true;
			}

			_code.Op(RecordProgram.OpMakeColumn);
			_code.U8((byte)children.Count);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		bool List(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			var children = ColumnChildren(creation);
			if (children is null)
				return Fail(UnknownElement, "The screen subset does not include ListView.", creation);
			if (children.Count > RecordProgram.MaxRows)
				return Fail(UnknownElement, "The screen subset does not include ListView.", creation);
			_code.Op(RecordProgram.OpMark);
			var nested = new KeySet();
			foreach (var child in children)
			{
				if (!Element(child, nested))
					return false;
			}

			if (!nested.Close(this, creation))
				return false;
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			_code.Op(RecordProgram.OpMakeList);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		bool Scroll(ObjectCreationExpressionSyntax creation, KeySet keys)
		{
			if (creation.ArgumentList?.Arguments.Count != 1)
				return Fail(UnknownElement, "The screen subset does not include ScrollView.", creation);
			if (!Element(creation.ArgumentList.Arguments[0].Expression, null))
				return false;
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			_code.Op(RecordProgram.OpMakeScroll);
			_code.U16(key);
			EmitStyle();
			return true;
		}

		IReadOnlyList<ExpressionSyntax>? ColumnChildren(ObjectCreationExpressionSyntax creation)
		{
			if (creation.ArgumentList is null)
				return null;
			if (creation.ArgumentList.Arguments.Count == 1 && creation.ArgumentList.Arguments[0].Expression is CollectionExpressionSyntax)
				return ElementsOf(creation.ArgumentList.Arguments[0].Expression);
			var children = new List<ExpressionSyntax>();
			foreach (var argument in creation.ArgumentList.Arguments)
				children.Add(argument.Expression);
			return children;
		}

		bool TextValue(ExpressionSyntax expression)
		{
			switch (expression)
			{
				case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
					_code.Op(RecordProgram.OpPushStr);
					_code.U16(_code.Intern(literal.Token.ValueText));
					return true;
				case InterpolatedStringExpressionSyntax interpolated:
					return Interpolated(interpolated);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
					return TextValue(binary.Left) && TextValue(binary.Right) && Concat();
				case ConditionalExpressionSyntax conditional:
					return Choice(conditional, TextValue);
				case ParenthesizedExpressionSyntax paren:
					return TextValue(paren.Expression);
				case MemberAccessExpressionSyntax member:
					return MemberText(member);
				case IdentifierNameSyntax name when _consts.TryGetValue(name.Identifier.ValueText, out var constant):
					return PushLiteral(constant);
				case InvocationExpressionSyntax invocation:
					return InvocationText(invocation);
				default:
					return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			}
		}

		bool InvocationText(InvocationExpressionSyntax invocation)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != "L")
				return Fail(UnknownExpression, "The screen subset does not include this expression.", invocation);
			var name = member.Name.Identifier.ValueText;
			var args = invocation.ArgumentList.Arguments;
			if (name == "Get" && args.Count == 1)
			{
				if (!TextValue(args[0].Expression))
					return false;
				_code.Op(RecordProgram.OpRes);
				return true;
			}

			if (name == "Plural" && args.Count == 3)
			{
				if (!TextValue(args[0].Expression) || !TextValue(args[1].Expression) || !TextValue(args[2].Expression))
					return false;
				_code.Op(RecordProgram.OpPlural);
				return true;
			}

			if (name == "Format" && args.Count >= 2 && args.Count <= 9)
			{
				for (var index = 0; index < args.Count; index++)
				{
					if (!TextValue(args[index].Expression))
						return false;
				}

				_code.Op(RecordProgram.OpFormat);
				_code.U8((byte)(args.Count - 1));
				return true;
			}

			return Fail(UnknownExpression, "The screen subset does not include this expression.", invocation);
		}

		bool Bool(ExpressionSyntax expression)
		{
			switch (expression)
			{
				case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
					_code.Op(RecordProgram.OpPushBool);
					_code.U8(1);
					return true;
				case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
					_code.Op(RecordProgram.OpPushBool);
					_code.U8(0);
					return true;
				case ConditionalExpressionSyntax conditional:
					return Choice(conditional, Bool);
				case ParenthesizedExpressionSyntax paren:
					return Bool(paren.Expression);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression):
					return EmptyOrEqual(binary);
				case MemberAccessExpressionSyntax member when ItemBool(member):
					return true;
				case MemberAccessExpressionSyntax member when Member(member, out var field) && field.Kind == ValueKind.Bool:
					_code.Op(RecordProgram.OpLoadBool);
					_code.U16(_code.Intern(field.Name));
					return true;
				default:
					return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			}
		}

		bool EmptyOrEqual(BinaryExpressionSyntax binary)
		{
			if (binary.Left is MemberAccessExpressionSyntax length
				&& length.Name.Identifier.ValueText == "Length"
				&& binary.Right is LiteralExpressionSyntax zero
				&& zero.IsKind(SyntaxKind.NumericLiteralExpression)
				&& zero.Token.ValueText == "0")
			{
				if (!TextValue(length.Expression))
					return false;
				_code.Op(RecordProgram.OpStrEmpty);
				return true;
			}

			if (binary.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				if (!TextValue(binary.Left))
					return false;
				if (!PushLiteral(literal.Token.ValueText))
					return false;
				_code.Op(RecordProgram.OpEqStr);
				return true;
			}

			return Fail(UnknownExpression, "The screen subset does not include this expression.", binary);
		}

		bool ItemBool(MemberAccessExpressionSyntax member)
		{
			if (_itemFields is null || member.Expression is not IdentifierNameSyntax item || item.Identifier.ValueText != _item)
				return false;
			var index = _itemFields.FindIndex(field => field.Name == member.Name.Identifier.ValueText && field.Kind == ValueKind.Bool);
			if (index < 0)
				return false;
			_code.Op(RecordProgram.OpItemBool);
			_code.U16(index);
			return true;
		}

		bool Condition(ExpressionSyntax expression)
		{
			var errors = Errors.Count;
			var at = _code.Here;
			if (Bool(expression) && Errors.Count == errors)
				return true;
			if (Errors.Count > errors)
				Errors.RemoveRange(errors, Errors.Count - errors);
			_code.Truncate(at);
			var buf = new VmBuf();
			if (!WriteExpr(expression, buf, out var kind) || kind != RecordProgram.VmKindBool || buf.Bytes.Count > ushort.MaxValue)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			_code.Op(RecordProgram.OpVmBool);
			_code.U16(buf.Bytes.Count);
			foreach (var item in buf.Bytes)
				_code.Op(item);
			return true;
		}

		bool Choice(ConditionalExpressionSyntax conditional, Func<ExpressionSyntax, bool> push)
		{
			if (!Condition(conditional.Condition))
				return false;
			var fail = _code.EmitJump(RecordProgram.OpJumpIfFalse);
			if (!push(conditional.WhenTrue))
				return false;
			var done = _code.EmitJump(RecordProgram.OpJump);
			_code.Patch(fail);
			if (!push(conditional.WhenFalse))
				return false;
			_code.Patch(done);
			return true;
		}

		bool Concat()
		{
			_code.Op(RecordProgram.OpConcat);
			return true;
		}

		bool Interpolated(InterpolatedStringExpressionSyntax interpolated)
		{
			if (interpolated.Contents.Count == 0)
			{
				_code.Op(RecordProgram.OpPushStr);
				_code.U16(_code.Intern(""));
				return true;
			}

			var started = false;
			foreach (var content in interpolated.Contents)
			{
				var pushed = content switch
				{
					InterpolatedStringTextSyntax text => PushLiteral(text.TextToken.ValueText),
					InterpolationSyntax hole => TextValue(hole.Expression),
					_ => Fail(UnknownExpression, "The screen subset does not include this expression.", content),
				};
				if (!pushed)
					return false;
				if (started && !Concat())
					return false;
				started = true;
			}

			return true;
		}

		bool PushLiteral(string text)
		{
			_code.Op(RecordProgram.OpPushStr);
			_code.U16(_code.Intern(text));
			return true;
		}

		bool MemberText(MemberAccessExpressionSyntax member)
		{
			if (member.Name.Identifier.ValueText == "Count"
				&& member.Expression is MemberAccessExpressionSyntax list
				&& Member(list, out var listed)
				&& listed.Kind == ValueKind.List)
			{
				_code.Op(RecordProgram.OpListCount);
				_code.U16(_code.Intern(listed.Name));
				return true;
			}

			if (_itemFields is not null
				&& member.Expression is IdentifierNameSyntax item
				&& item.Identifier.ValueText == _item)
			{
				var index = _itemFields.FindIndex(field => field.Name == member.Name.Identifier.ValueText && field.Kind == ValueKind.Str);
				if (index < 0)
					return Fail(UnknownMember, "The screen subset does not include " + member.Name.Identifier.ValueText + ".", member);
				_code.Op(RecordProgram.OpItemStr);
				_code.U16(index);
				return true;
			}

			if (!Member(member, out var field))
				return Fail(UnknownMember, "The screen subset does not include " + member.Name.Identifier.ValueText + ".", member);
			if (field.Kind == ValueKind.Str)
			{
				_code.Op(RecordProgram.OpLoadStr);
				_code.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.Int)
			{
				_code.Op(RecordProgram.OpLoadInt);
				_code.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.FlagCount)
			{
				_code.Op(RecordProgram.OpListFlagCount);
				_code.U16(_code.Intern(field.SourceList));
				_code.U16(field.FlagIndex);
				return true;
			}

			return Fail(UnknownExpression, "The screen subset does not include this expression.", member);
		}

		bool Member(MemberAccessExpressionSyntax member, out Field field)
		{
			field = null!;
			if (member.Expression is not IdentifierNameSyntax owner || owner.Identifier.ValueText != _state)
				return false;
			var found = _fields.FirstOrDefault(item => item.Name == member.Name.Identifier.ValueText);
			if (found is null)
				return false;
			field = found;
			return true;
		}

		bool KeyOf(ObjectCreationExpressionSyntax creation, out int key, out bool dynamic, out int field)
		{
			key = RecordProgram.NoKey;
			dynamic = false;
			field = 0;
			_pendingStyle = null;
			_pendingMotion = null;
			_pendingDescription = null;
			_pendingDecorative = false;
			_pendingSide = 0;
			_pendingGestures.Clear();
			_pendingEventName = null;
			_pendingEventScript = null;
			_pendingKeyboard = Keyboard.Text;
			_pendingLines = 0;
			_pendingSubmit = Submit.None;
			_pendingAutofill = false;
			_pendingSubmitScript = null;
			if (creation.Initializer is null)
				return true;
			foreach (var expression in creation.Initializer.Expressions)
			{
				if (expression is not AssignmentExpressionSyntax assignment || assignment.Left is not IdentifierNameSyntax name)
					return Fail(UnknownMember, "The screen subset does not include an element initializer.", creation.Initializer);
				if (name.Identifier.ValueText == "Description" && TypeName(creation) == "Image")
				{
					_pendingDescription = assignment.Right;
					continue;
				}

				if (name.Identifier.ValueText == "Decorative" && TypeName(creation) == "Image")
				{
					if (assignment.Right is not LiteralExpressionSyntax decorative
						|| (!decorative.IsKind(SyntaxKind.TrueLiteralExpression) && !decorative.IsKind(SyntaxKind.FalseLiteralExpression)))
						return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
					_pendingDecorative = decorative.IsKind(SyntaxKind.TrueLiteralExpression);
					continue;
				}

				if (name.Identifier.ValueText == "Side" && TypeName(creation) == "Image")
				{
					if (!Integer(assignment.Right, out var side))
						return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
					_pendingSide = side;
					continue;
				}

				if (name.Identifier.ValueText == "Style")
				{
					if (!StyleOf(assignment.Right, out var style))
						return false;
					_pendingStyle = style;
					continue;
				}

				if (name.Identifier.ValueText == "Motion")
				{
					if (!MotionOf(assignment.Right, out var motion))
						return false;
					_pendingMotion = motion;
					continue;
				}

				if (name.Identifier.ValueText is "Event" or "OnEvent")
				{
					if (TypeName(creation) != "HostView")
						return Fail(UnknownMember, "The screen subset does not include an element initializer.", assignment);
					if (name.Identifier.ValueText == "Event")
					{
						if (assignment.Right is not LiteralExpressionSyntax eventName || !eventName.IsKind(SyntaxKind.StringLiteralExpression))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						_pendingEventName = eventName.Token.ValueText;
						continue;
					}

					if (assignment.Right is not LambdaExpressionSyntax eventLambda || !Script(eventLambda, out var eventScript) || eventScript.Length == 0)
						return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment.Right);
					_pendingEventScript = eventScript;
					continue;
				}

				if (name.Identifier.ValueText is "OnTap" or "OnLongPress" or "OnSwipe" or "OnRefresh")
				{
					if (assignment.Right is not LambdaExpressionSyntax gestureLambda)
						return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment.Right);
					if (!Script(gestureLambda, out var gestureScript) || gestureScript.Length == 0)
						return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
					var kind = name.Identifier.ValueText switch
					{
						"OnTap" => Gestures.Tap,
						"OnLongPress" => Gestures.LongPress,
						"OnSwipe" => Gestures.Swipe,
						"OnRefresh" => Gestures.Refresh,
						_ => (byte)0,
					};
					_pendingGestures.Add((kind, gestureScript));
					continue;
				}

				if (name.Identifier.ValueText is "Keyboard" or "Lines" or "Submit" or "OnSubmit" or "Autofill")
				{
					if (TypeName(creation) is not ("TextField" or "TextInput"))
						return Fail(UnknownMember, "The screen subset does not include an element initializer.", assignment);
					if (!FieldMode(name.Identifier.ValueText, assignment.Right))
						return false;
					continue;
				}

				if (name.Identifier.ValueText != "Key")
					return Fail(UnknownMember, "The screen subset does not include an element initializer.", assignment);
				if (assignment.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				{
					key = _code.Intern(literal.Token.ValueText);
					continue;
				}

				if (assignment.Right is MemberAccessExpressionSyntax member
					&& _itemFields is not null
					&& member.Expression is IdentifierNameSyntax item
					&& item.Identifier.ValueText == _item)
				{
					var index = _itemFields.FindIndex(candidate => candidate.Name == member.Name.Identifier.ValueText && candidate.Kind == ValueKind.Str);
					if (index < 0)
						return Fail(UnknownMember, "The screen subset does not include " + member.Name.Identifier.ValueText + ".", member);
					dynamic = true;
					field = index;
					continue;
				}

				return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
			}

			return true;
		}

		void EmitStyle()
		{
			if (_pendingStyle is Style style)
			{
				var bytes = new byte[StyleCodec.Bytes];
				StyleCodec.Write(style, bytes);
				_code.Op(RecordProgram.OpStyle);
				_code.Bytes(bytes);
				_pendingStyle = null;
			}

			EmitMotion();
		}

		void EmitMotion()
		{
			if (_pendingMotion is Motion motion && !MotionCodec.IsRest(motion))
			{
				var bytes = new byte[MotionCodec.Bytes];
				MotionCodec.Write(motion, bytes);
				_code.Op(RecordProgram.OpMotion);
				_code.Bytes(bytes);
			}

			_pendingMotion = null;
			EmitGestures();
		}

		void EmitGestures()
		{
			foreach (var (kind, script) in _pendingGestures)
			{
				_code.Op(RecordProgram.OpGesture);
				_code.U8(kind);
				_code.U16(script.Length);
				_code.Bytes(script);
			}

			_pendingGestures.Clear();
			if (_pendingEventScript is not null)
			{
				_code.Op(RecordProgram.OpHostEvent);
				_code.U16(_code.Intern(_pendingEventName ?? ""));
				_code.U16(_pendingEventScript.Length);
				_code.Bytes(_pendingEventScript);
			}

			_pendingEventName = null;
			_pendingEventScript = null;
		}

		bool MotionOf(ExpressionSyntax expression, out Motion motion)
		{
			motion = Motion.Rest;
			if (expression is not ObjectCreationExpressionSyntax creation || TypeName(creation) != "Motion" || creation.Initializer is null)
				return Fail(UnknownMember, "The screen subset does not include an element initializer.", expression);
			var fromOpacity = 100;
			var fromX = 0;
			var fromY = 0;
			var fromScale = 100;
			var opacity = 100;
			var x = 0;
			var y = 0;
			var scale = 100;
			var duration = 0;
			var easing = 0;
			foreach (var item in creation.Initializer.Expressions)
			{
				if (item is not AssignmentExpressionSyntax assignment || assignment.Left is not IdentifierNameSyntax name)
					return Fail(UnknownMember, "The screen subset does not include an element initializer.", item);
				if (!Integer(assignment.Right, out var number))
					return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
				switch (name.Identifier.ValueText)
				{
					case "FromOpacity":
						fromOpacity = number;
						break;
					case "FromX":
						fromX = number;
						break;
					case "FromY":
						fromY = number;
						break;
					case "FromScale":
						fromScale = number;
						break;
					case "Opacity":
						opacity = number;
						break;
					case "X":
						x = number;
						break;
					case "Y":
						y = number;
						break;
					case "Scale":
						scale = number;
						break;
					case "Duration":
						duration = number;
						break;
					case "Easing":
						easing = number;
						break;
					default:
						return Fail(UnknownMember, "The screen subset does not include " + name.Identifier.ValueText + ".", assignment);
				}
			}

			motion = new Motion
			{
				FromOpacity = fromOpacity,
				FromX = fromX,
				FromY = fromY,
				FromScale = fromScale,
				Opacity = opacity,
				X = x,
				Y = y,
				Scale = scale,
				Duration = duration,
				Easing = easing,
			};
			return true;
		}

		bool FieldMode(string name, ExpressionSyntax expression)
		{
			switch (name)
			{
				case "Keyboard":
					return EnumValue<Keyboard>(expression, out _pendingKeyboard)
						|| Fail(UnknownMember, "The screen subset does not include an element initializer.", expression);
				case "Lines":
					return Integer(expression, out _pendingLines)
						|| Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
				case "Submit":
					return EnumValue<Submit>(expression, out _pendingSubmit)
						|| Fail(UnknownMember, "The screen subset does not include an element initializer.", expression);
				case "OnSubmit":
					if (expression is not LambdaExpressionSyntax lambda || !Script(lambda, out var script))
						return expression is LambdaExpressionSyntax ? false : Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
					_pendingSubmitScript = script;
					return true;
				case "Autofill":
					if (expression is not LiteralExpressionSyntax flag
						|| (!flag.IsKind(SyntaxKind.TrueLiteralExpression) && !flag.IsKind(SyntaxKind.FalseLiteralExpression)))
						return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
					_pendingAutofill = flag.IsKind(SyntaxKind.TrueLiteralExpression);
					return true;
				default:
					return Fail(UnknownMember, "The screen subset does not include " + name + ".", expression);
			}
		}

		void EmitFieldMode()
		{
			var script = _pendingSubmitScript;
			if (_pendingKeyboard == Keyboard.Text && _pendingLines <= 1 && _pendingSubmit == Submit.None && !_pendingAutofill && (script is null || script.Length == 0))
				return;
			_code.Op(RecordProgram.OpFieldMode);
			_code.U8((byte)_pendingKeyboard);
			_code.U8((byte)Math.Max(_pendingLines, 0));
			_code.U8((byte)_pendingSubmit);
			_code.U8((byte)(_pendingAutofill ? 1 : 0));
			_code.U16(script?.Length ?? 0);
			if (script is { Length: > 0 })
				_code.Bytes(script);
			_pendingSubmitScript = null;
		}

		bool StyleOf(ExpressionSyntax expression, out Style style)
		{
			style = default;
			if (expression is not ObjectCreationExpressionSyntax creation || TypeName(creation) != "Style" || creation.Initializer is null)
				return Fail(UnknownMember, "The screen subset does not include an element initializer.", expression);
			var padding = 0;
			var margin = 0;
			var gap = 0;
			var grow = 0;
			var fontSize = 0;
			var radius = 0;
			var align = Align.Start;
			var weight = FontWeight.Regular;
			uint color = 0;
			uint background = 0;
			var across = 0;
			uint wash = 0;
			uint stroke = 0;
			foreach (var item in creation.Initializer.Expressions)
			{
				if (item is not AssignmentExpressionSyntax assignment || assignment.Left is not IdentifierNameSyntax name)
					return Fail(UnknownMember, "The screen subset does not include an element initializer.", item);
				switch (name.Identifier.ValueText)
				{
					case "Padding":
						if (!Integer(assignment.Right, out padding))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Margin":
						if (!Integer(assignment.Right, out margin))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Gap":
						if (!Integer(assignment.Right, out gap))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Grow":
						if (!Integer(assignment.Right, out grow))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "FontSize":
						if (!Integer(assignment.Right, out fontSize))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Radius":
						if (!Integer(assignment.Right, out radius))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Align":
						if (!EnumValue<Align>(assignment.Right, out align))
							return Fail(UnknownMember, "The screen subset does not include an element initializer.", assignment.Right);
						break;
					case "Weight":
						if (!EnumValue<FontWeight>(assignment.Right, out weight))
							return Fail(UnknownMember, "The screen subset does not include an element initializer.", assignment.Right);
						break;
					case "Color":
						if (!ColorValue(assignment.Right, out color))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Background":
						if (!ColorValue(assignment.Right, out background))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Across":
						if (!Integer(assignment.Right, out across))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Wash":
						if (!ColorValue(assignment.Right, out wash))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					case "Stroke":
						if (!ColorValue(assignment.Right, out stroke))
							return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
						break;
					default:
						return Fail(UnknownMember, "The screen subset does not include " + name.Identifier.ValueText + ".", assignment);
				}
			}

			style = new Style
			{
				Padding = padding,
				Margin = margin,
				Gap = gap,
				Grow = grow,
				FontSize = fontSize,
				Radius = radius,
				Align = align,
				Weight = weight,
				Color = color,
				Background = background,
				Across = across,
				Wash = wash,
				Stroke = stroke,
			};
			return true;
		}

		static bool Integer(ExpressionSyntax expression, out int value)
		{
			value = 0;
			if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
				return false;
			switch (literal.Token.Value)
			{
				case int number:
					value = number;
					return true;
				case uint number when number <= int.MaxValue:
					value = (int)number;
					return true;
				default:
					return false;
			}
		}

		static bool ColorValue(ExpressionSyntax expression, out uint value)
		{
			value = 0;
			if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
				return false;
			switch (literal.Token.Value)
			{
				case int number:
					value = unchecked((uint)number);
					return true;
				case uint number:
					value = number;
					return true;
				case long number when number >= 0 && number <= uint.MaxValue:
					value = (uint)number;
					return true;
				default:
					return false;
			}
		}

		static bool EnumValue<TEnum>(ExpressionSyntax expression, out TEnum value)
			where TEnum : struct, Enum
		{
			value = default;
			if (expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != typeof(TEnum).Name)
				return false;
			return Enum.TryParse(member.Name.Identifier.ValueText, out value);
		}

		static bool HasBareReturn(SyntaxNode node)
		{
			foreach (var child in node.DescendantNodesAndSelf())
			{
				if (child is ReturnStatementSyntax returned && returned.Expression is null)
					return true;
			}

			return false;
		}

		bool EmitReturning(BlockSyntax block, string parameter, out byte[] script)
		{
			script = Array.Empty<byte>();
			var saved = _returnPatches;
			_returnPatches = new List<int>();
			try
			{
				var buf = new VmBuf();
				foreach (var statement in block.Statements)
				{
					if (!WriteVmStatement(statement, parameter, buf))
						return false;
				}

				foreach (var patch in _returnPatches)
					buf.Patch(patch);
				var bytes = new List<byte>();
				if (!EmitVm(bytes, buf))
					return false;
				script = bytes.ToArray();
				return script.Length > 0;
			}
			finally
			{
				_returnPatches = saved;
			}
		}

		bool Script(LambdaExpressionSyntax lambda, out byte[] script)
		{
			script = Array.Empty<byte>();
			var parameter = lambda switch
			{
				SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
				ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1 => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
				ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 2 => parenthesized.ParameterList.Parameters[1].Identifier.ValueText,
				ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 0 => "",
				_ => null,
			};
			if (parameter is null)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", lambda);
			var savedLocals = _locals;
			var savedSlots = _localSlots;
			var savedBound = _boundRows;
			_locals = new Dictionary<string, (byte Slot, ValueKind Kind)>(StringComparer.Ordinal);
			_localSlots = 0;
			_boundRows = null;
			try
			{
			var bytes = new List<byte>();
			if (lambda.Body is ExpressionSyntax single)
			{
				if (!Action(single, parameter, bytes))
					return false;
			}
			else if (lambda.Body is BlockSyntax block && HasBareReturn(block))
			{
				if (!EmitReturning(block, parameter, out script))
					return false;
				_actions.Add((true, script.Length));
				return true;
			}
			else if (lambda.Body is BlockSyntax statements)
			{
				foreach (var statement in statements.Statements)
				{
					if (!Keep(statement, parameter, bytes))
						return false;
				}
			}
			else
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", lambda);

			script = bytes.ToArray();
			var works = LambdaWorks(lambda);
			_actions.Add((works, script.Length));
			if (works && script.Length == 0)
				return Fail(BadLambda, "The reload program does not skip this statement.", lambda);
			return true;
			}
			finally
			{
				_locals = savedLocals;
				_localSlots = savedSlots;
				_boundRows = savedBound;
			}
		}

		bool Action(ExpressionSyntax expression, string parameter, List<byte> script)
		{
			switch (expression)
			{
				case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
					if (Assign(assignment, parameter, script))
						return true;
					return assignment.Left is IdentifierNameSyntax && VmExprStatement(assignment, parameter, script);
				case PostfixUnaryExpressionSyntax postfix when postfix.Operand is MemberAccessExpressionSyntax telemetry
					&& telemetry.Expression is IdentifierNameSyntax telemetryOwner
					&& telemetryOwner.Identifier.ValueText == "PackingTelemetry":
					return Telemetry(telemetry.Name.Identifier.ValueText, script);
				case AssignmentExpressionSyntax compound when compound.IsKind(SyntaxKind.AddAssignmentExpression) || compound.IsKind(SyntaxKind.SubtractAssignmentExpression):
					return Compound(compound, script) || VmExprStatement(compound, parameter, script);
				case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression):
					return Increment(postfix.Operand, script) || VmExprStatement(postfix, parameter, script);
				case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostDecrementExpression):
					return Decrement(postfix.Operand, script) || VmExprStatement(postfix, parameter, script);
				case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression):
					return Increment(prefix.Operand, script) || VmExprStatement(prefix, parameter, script);
				case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreDecrementExpression):
					return Decrement(prefix.Operand, script) || VmExprStatement(prefix, parameter, script);
				case InvocationExpressionSyntax invocation:
					return Call(invocation, parameter, script) || (UserCall(invocation, out _) && VmExprStatement(invocation, parameter, script));
				default:
					return VmExprStatement(expression, parameter, script);
			}
		}

		bool Assign(AssignmentExpressionSyntax assignment, string parameter, List<byte> script)
		{
			if (assignment.Left is MemberAccessExpressionSyntax flipped
				&& _itemFields is not null
				&& flipped.Expression is IdentifierNameSyntax flippedItem
				&& flippedItem.Identifier.ValueText == _item
				&& assignment.Right is PrefixUnaryExpressionSyntax flip
				&& flip.IsKind(SyntaxKind.LogicalNotExpression)
				&& flip.Operand is MemberAccessExpressionSyntax same
				&& same.Expression is IdentifierNameSyntax sameItem
				&& sameItem.Identifier.ValueText == _item
				&& same.Name.Identifier.ValueText == flipped.Name.Identifier.ValueText)
			{
				var index = _itemFields.FindIndex(candidate => candidate.Name == flipped.Name.Identifier.ValueText && candidate.Kind == ValueKind.Bool);
				if (index < 0 || _listName.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
				Act(script, RecordProgram.ActNotRowBool, _code.Intern(_listName));
				U16(script, index);
				return true;
			}

			if (assignment.Left is MemberAccessExpressionSyntax itemMember
				&& _itemFields is not null
				&& itemMember.Expression is IdentifierNameSyntax item
				&& item.Identifier.ValueText == _item
				&& assignment.Right is IdentifierNameSyntax argument
				&& argument.Identifier.ValueText == parameter)
			{
				var index = _itemFields.FindIndex(candidate => candidate.Name == itemMember.Name.Identifier.ValueText && candidate.Kind == ValueKind.Bool);
				if (index < 0 || _listName.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
				Act(script, RecordProgram.ActSetRowBool, _code.Intern(_listName));
				U16(script, index);
				return true;
			}

			if (assignment.Left is MemberAccessExpressionSyntax left
				&& Member(left, out var target)
				&& assignment.Right is MemberAccessExpressionSyntax right
				&& _itemFields is not null
				&& right.Expression is IdentifierNameSyntax row
				&& row.Identifier.ValueText == _item)
			{
				var index = _itemFields.FindIndex(candidate => candidate.Name == right.Name.Identifier.ValueText);
				if (index < 0 || _listName.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
				if (target.Kind == ValueKind.Str && _itemFields[index].Kind == ValueKind.Str)
				{
					Act(script, RecordProgram.ActTakeStr, _code.Intern(target.Name));
					U16(script, _code.Intern(_listName));
					U16(script, index);
					return true;
				}

				if (target.Kind == ValueKind.Bool && _itemFields[index].Kind == ValueKind.Bool)
				{
					Act(script, RecordProgram.ActTakeBool, _code.Intern(target.Name));
					U16(script, _code.Intern(_listName));
					U16(script, index);
					return true;
				}

				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
			}

			if (assignment.Left is not MemberAccessExpressionSyntax member || !Member(member, out var field))
				return false;
			if (field.Kind == ValueKind.Str && Bare(assignment.Right) is AwaitExpressionSyntax awaited)
				return AwaitStore(field.Name, awaited, script);
			if (assignment.Right is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter && parameter.Length > 0)
			{
				if (field.Kind == ValueKind.Str)
					return Act(script, RecordProgram.ActSetStr, _code.Intern(field.Name));
				if (field.Kind == ValueKind.Bool)
					return Act(script, RecordProgram.ActSetBool, _code.Intern(field.Name));
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", assignment);
			}

			if (assignment.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) && field.Kind == ValueKind.Str)
			{
				Act(script, RecordProgram.ActSetStrLit, _code.Intern(field.Name));
				U16(script, _code.Intern(literal.Token.ValueText));
				return true;
			}

			if (field.Kind == ValueKind.Bool && assignment.Right is LiteralExpressionSyntax truth && (truth.IsKind(SyntaxKind.TrueLiteralExpression) || truth.IsKind(SyntaxKind.FalseLiteralExpression)))
			{
				Act(script, RecordProgram.ActSetBoolLit, _code.Intern(field.Name));
				script.Add(truth.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
				return true;
			}

			if (field.Kind == ValueKind.Str
				&& assignment.Right is ConditionalExpressionSyntax choice
				&& choice.Condition is BinaryExpressionSyntax empty
				&& empty.IsKind(SyntaxKind.EqualsExpression)
				&& LengthOf(empty, out var emptySource)
				&& choice.WhenTrue is LiteralExpressionSyntax whenEmpty
				&& whenEmpty.IsKind(SyntaxKind.StringLiteralExpression)
				&& choice.WhenFalse is LiteralExpressionSyntax whenFull
				&& whenFull.IsKind(SyntaxKind.StringLiteralExpression))
			{
				Act(script, RecordProgram.ActSetStrEmpty, _code.Intern(field.Name));
				U16(script, _code.Intern(emptySource));
				U16(script, _code.Intern(whenEmpty.Token.ValueText));
				U16(script, _code.Intern(whenFull.Token.ValueText));
				return true;
			}

			if (field.Kind == ValueKind.Bool
				&& assignment.Right is BinaryExpressionSyntax present
				&& present.IsKind(SyntaxKind.NotEqualsExpression)
				&& LengthOf(present, out var presentSource))
			{
				Act(script, RecordProgram.ActSetBoolLen, _code.Intern(field.Name));
				U16(script, _code.Intern(presentSource));
				return true;
			}

			if (assignment.Right is ConditionalExpressionSyntax branch)
			{
				var mark = Errors.Count;
				if (Ternary(field, branch, script))
					return true;
				if (Errors.Count > mark)
					return false;
			}
			if (field.Kind == ValueKind.Int && WriteIntExpr(assignment.Right, out var intExpr))
			{
				script.Add(RecordProgram.ActSetInt);
				U16(script, _code.Intern(field.Name));
				script.AddRange(intExpr);
				return true;
			}

			if (field.Kind == ValueKind.Str && WriteStrCat(assignment.Right, out var strExpr, out var strCount))
			{
				script.Add(RecordProgram.ActSetStrCat);
				U16(script, _code.Intern(field.Name));
				script.Add(strCount);
				script.AddRange(strExpr);
				return true;
			}

			if (field.Kind == ValueKind.Bool && Bare(assignment.Right) is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
				return WriteNot(field, negation.Operand, script);
			if (field.Kind == ValueKind.Bool && BoolSource(assignment.Right, script, field.Name))
				return true;

			return VmStore(field, assignment.Right, script);
		}

		bool LocalLength(BinaryExpressionSyntax binary, out byte slot)
		{
			slot = 0;
			if (!binary.IsKind(SyntaxKind.EqualsExpression) && !binary.IsKind(SyntaxKind.NotEqualsExpression))
				return false;
			if (binary.Left is not MemberAccessExpressionSyntax length
				|| length.Name.Identifier.ValueText != "Length"
				|| binary.Right is not LiteralExpressionSyntax zero
				|| !zero.IsKind(SyntaxKind.NumericLiteralExpression)
				|| zero.Token.ValueText != "0"
				|| length.Expression is not IdentifierNameSyntax name)
				return false;
			return Local(name.Identifier.ValueText, ValueKind.Str, out slot);
		}

		bool LengthOf(BinaryExpressionSyntax binary, out string source)
		{
			source = "";
			if (binary.Left is not MemberAccessExpressionSyntax length
				|| length.Name.Identifier.ValueText != "Length"
				|| binary.Right is not LiteralExpressionSyntax zero
				|| !zero.IsKind(SyntaxKind.NumericLiteralExpression)
				|| zero.Token.ValueText != "0"
				|| length.Expression is not MemberAccessExpressionSyntax member
				|| !Member(member, out var field)
				|| field.Kind != ValueKind.Str)
				return false;
			source = field.Name;
			return true;
		}

		bool Increment(ExpressionSyntax operand, List<byte> script)
		{
			if (operand is not MemberAccessExpressionSyntax member || !Member(member, out var field) || field.Kind != ValueKind.Int)
				return false;
			return Act(script, RecordProgram.ActInc, _code.Intern(field.Name));
		}

		bool Call(InvocationExpressionSyntax invocation, string parameter, List<byte> script)
		{
			_ = parameter;
			if (invocation.Expression is IdentifierNameSyntax localCall
				&& invocation.ArgumentList.Arguments.Count == 0
				&& _localFns.TryGetValue(localCall.Identifier.ValueText, out var localBody))
				return EmitLocal(localBody, "", script, invocation);

			if (UserCall(invocation, out _))
				return false;
			if (invocation.Expression is MemberAccessExpressionSyntax listCall
				&& listCall.Expression is MemberAccessExpressionSyntax listTarget
				&& listTarget.Expression is IdentifierNameSyntax listOwner
				&& listOwner.Identifier.ValueText == _state
				&& ListField(listTarget.Name.Identifier.ValueText, out var listField))
				return ListMutate(listField, listCall.Name.Identifier.ValueText, invocation, script);
			if (invocation.Expression is not MemberAccessExpressionSyntax member || member.Expression is not IdentifierNameSyntax owner)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (_bindings.Contains(owner.Identifier.ValueText + "." + member.Name.Identifier.ValueText))
				return JniCall(owner.Identifier.ValueText, member.Name.Identifier.ValueText, invocation, script);
			if (owner.Identifier.ValueText == _state && member.Name.Identifier.ValueText == "Drop")
				return DropRow(invocation, script);
			if (owner.Identifier.ValueText == _state && _methods.ContainsKey(_stateType + "." + member.Name.Identifier.ValueText))
				return StateCall(member.Name.Identifier.ValueText, script, invocation);
			if (owner.Identifier.ValueText == "PackingTelemetry")
				return Telemetry(member.Name.Identifier.ValueText, script);
			if (owner.Identifier.ValueText == "PersistRows" && member.Name.Identifier.ValueText == "Fill")
				return FillRows(invocation, script);
			if (owner.Identifier.ValueText is "Clipboard" or "Haptics" or "Share" or "Storage" or "Theme" or "FeedModule" or "Animate" or "Animated" or "Location" or "Nav")
				return ModuleCall(owner.Identifier.ValueText, member.Name.Identifier.ValueText, invocation, script);
			if (owner.Identifier.ValueText != _platform)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (member.Name.Identifier.ValueText == "RequestRender" && invocation.ArgumentList.Arguments.Count == 0)
			{
				script.Add(RecordProgram.ActRender);
				return true;
			}

			if (member.Name.Identifier.ValueText == "Pulse" && invocation.ArgumentList.Arguments.Count == 0)
			{
				script.Add(RecordProgram.ActPulse);
				return true;
			}

			if (member.Name.Identifier.ValueText == "CopyNonce" && invocation.ArgumentList.Arguments.Count == 1)
				return Copy(invocation.ArgumentList.Arguments[0].Expression, script);
			if (member.Name.Identifier.ValueText == "CopyText" && invocation.ArgumentList.Arguments.Count == 2)
				return CopyRows(invocation, script);
			if (member.Name.Identifier.ValueText == "Push" && invocation.ArgumentList.Arguments.Count == 1)
			{
				if (invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax pushed
					|| !pushed.IsKind(SyntaxKind.StringLiteralExpression)
					|| pushed.Token.ValueText.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
				Act(script, RecordProgram.ActPush, _code.Intern(pushed.Token.ValueText));
				return true;
			}

			if (member.Name.Identifier.ValueText == "Push" && invocation.ArgumentList.Arguments.Count == 2)
				return PushRow(invocation, script);
			if (member.Name.Identifier.ValueText == "WriteBack" && invocation.ArgumentList.Arguments.Count == 0)
			{
				script.Add(RecordProgram.ActWriteBack);
				return true;
			}

			if (member.Name.Identifier.ValueText == "Pop" && invocation.ArgumentList.Arguments.Count == 0)
			{
				script.Add(RecordProgram.ActPop);
				return true;
			}

			if (member.Name.Identifier.ValueText == "Effect" && invocation.ArgumentList.Arguments.Count == 2)
				return EffectAction(invocation, script);

			return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
		}

		static int RowId(List<Field> fields)
		{
			var named = fields.FindIndex(field => field.Name == "Id" && field.Kind == ValueKind.Str);
			if (named >= 0)
				return named;
			return fields.FindIndex(field => field.Kind == ValueKind.Str);
		}

		bool PushRow(InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args[0].Expression is not LiteralExpressionSyntax pushed
				|| !pushed.IsKind(SyntaxKind.StringLiteralExpression)
				|| pushed.Token.ValueText.Length == 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (args[1].Expression is ElementAccessExpressionSyntax access)
				return PushIndex(access, pushed.Token.ValueText, script, invocation);
			if (args[1].Expression is not IdentifierNameSyntax item
				|| item.Identifier.ValueText != _item
				|| _item.Length == 0
				|| _listName.Length == 0
				|| _itemFields is null)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var id = RowId(_itemFields);
			if (id < 0)
				return Fail(BadLambda, "Push copies same-named fields onto the destination screen. The row needs a string property.", invocation);
			var strings = new List<Field>();
			var flags = new List<Field>();
			foreach (var field in _itemFields)
			{
				if (field.Kind == ValueKind.Str)
					strings.Add(field);
				else if (field.Kind == ValueKind.Bool)
					flags.Add(field);
			}

			if (strings.Count > byte.MaxValue || flags.Count > byte.MaxValue)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			script.Add(RecordProgram.ActPushRow);
			U16(script, _code.Intern(pushed.Token.ValueText));
			U16(script, _code.Intern(_listName));
			U16(script, id);
			script.Add((byte)strings.Count);
			foreach (var field in strings)
			{
				U16(script, _itemFields.IndexOf(field));
				U16(script, _code.Intern(field.Name));
			}

			script.Add((byte)flags.Count);
			foreach (var field in flags)
			{
				U16(script, _itemFields.IndexOf(field));
				U16(script, _code.Intern(field.Name));
			}

			return true;
		}

		bool PushIndex(ElementAccessExpressionSyntax access, string screen, List<byte> script, InvocationExpressionSyntax invocation)
		{
			if (access.ArgumentList.Arguments.Count != 1
				|| access.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _state
				|| !Integer(access.ArgumentList.Arguments[0].Expression, out var index)
				|| index < 0
				|| index > ushort.MaxValue)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var list = _fields.FirstOrDefault(field => field.Name == member.Name.Identifier.ValueText && field.Kind == ValueKind.List);
			if (list is null || !_records.TryGetValue(list.Item, out var itemFields))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var id = RowId(itemFields);
			if (id < 0)
				return Fail(BadLambda, "Push copies same-named fields onto the destination screen. The row needs a string property.", invocation);
			var strings = new List<Field>();
			var flags = new List<Field>();
			foreach (var field in itemFields)
			{
				if (field.Kind == ValueKind.Str)
					strings.Add(field);
				else if (field.Kind == ValueKind.Bool)
					flags.Add(field);
			}

			if (strings.Count > byte.MaxValue || flags.Count > byte.MaxValue)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			script.Add(RecordProgram.ActPushIndex);
			U16(script, _code.Intern(screen));
			U16(script, _code.Intern(list.Name));
			U16(script, index);
			U16(script, id);
			script.Add((byte)strings.Count);
			foreach (var field in strings)
			{
				U16(script, itemFields.IndexOf(field));
				U16(script, _code.Intern(field.Name));
			}

			script.Add((byte)flags.Count);
			foreach (var field in flags)
			{
				U16(script, itemFields.IndexOf(field));
				U16(script, _code.Intern(field.Name));
			}

			return true;
		}

		bool DropRow(InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args.Count != 1
				|| args[0].Expression is not IdentifierNameSyntax item
				|| item.Identifier.ValueText != _item
				|| _item.Length == 0
				|| _listName.Length == 0
				|| _itemFields is null)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var id = _itemFields.FindIndex(field => field.Name == "Id");
			if (id < 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			Act(script, RecordProgram.ActDropRow, _code.Intern(_listName));
			U16(script, id);
			return true;
		}

		bool EffectAction(InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args[0].Expression is not LiteralExpressionSyntax property
				|| !property.IsKind(SyntaxKind.StringLiteralExpression)
				|| args[1].Expression is not LiteralExpressionSyntax resource
				|| !resource.IsKind(SyntaxKind.StringLiteralExpression))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var name = property.Token.ValueText;
			if (_fields.All(field => field.Name != name || field.Kind != ValueKind.Str))
				return Fail(UnknownMember, "The screen subset does not include " + name + ".", invocation);
			script.Add(RecordProgram.ActEffect);
			U16(script, _code.Intern(name));
			U16(script, _code.Intern(resource.Token.ValueText));
			return true;
		}

		bool ModuleCall(string module, string method, InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args.Count == 0 || args[0].Expression is not IdentifierNameSyntax platform || platform.Identifier.ValueText != _platform)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			var encoded = new List<(byte Kind, int Index)>();
			if (module == "Haptics" && method == "Confirm" && args.Count == 1)
			{
			}
			else if (module == "Share" && method == "Text" && args.Count == 2)
			{
				if (!ModuleArg(args[1].Expression, encoded))
					return false;
			}
			else if (module == "Clipboard" && method == "Copy" && args.Count == 3)
			{
				if (args[1].Expression is not LiteralExpressionSyntax label || !label.IsKind(SyntaxKind.StringLiteralExpression))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				encoded.Add((0, _code.Intern(label.Token.ValueText)));
				if (!ModuleArg(args[2].Expression, encoded))
					return false;
			}
			else if (module == "Storage" && method == "Set" && args.Count == 3)
			{
				if (args[1].Expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				encoded.Add((0, _code.Intern(key.Token.ValueText)));
				if (!ModuleArg(args[2].Expression, encoded))
					return false;
			}
			else if (module == "Theme" && method == "Mode" && args.Count == 2)
			{
				if (!ModuleArg(args[1].Expression, encoded))
					return false;
			}
			else if (module == "Theme" && method == "Scale" && args.Count == 2)
			{
				if (!ModuleArg(args[1].Expression, encoded))
					return false;
			}
			else if (module == "FeedModule" && method == "Apply" && args.Count == 3)
			{
				if (!ModuleArg(args[2].Expression, encoded))
					return false;
			}
			else if (module == "Nav" && method == "Pop" && args.Count == 1)
			{
			}
			else if (module == "Nav" && method == "Push" && args.Count == 2)
			{
				if (args[1].Expression is not LiteralExpressionSyntax screen
					|| !screen.IsKind(SyntaxKind.StringLiteralExpression)
					|| screen.Token.ValueText.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				encoded.Add((0, _code.Intern(screen.Token.ValueText)));
			}
			else if (module == "Animate" && method == "Play" && args.Count == 2)
			{
				if (args[1].Expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				encoded.Add((0, _code.Intern(key.Token.ValueText)));
			}
			else if (module == "Animated" && method is "Timing" or "Spring" && args.Count == 5)
			{
				for (var index = 1; index < args.Count; index++)
				{
					if (!ModuleArg(args[index].Expression, encoded))
						return false;
				}
			}
			else
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);

			script.Add(RecordProgram.ActModule);
			U16(script, _code.Intern(module == "FeedModule" ? "feed" : module.ToLowerInvariant()));
			U16(script, _code.Intern(method));
			script.Add((byte)encoded.Count);
			foreach (var (kind, index) in encoded)
			{
				script.Add(kind);
				U16(script, index);
			}

			return true;
		}

		static ExpressionSyntax? LambdaBody(LambdaExpressionSyntax lambda)
		{
			if (lambda.Body is ExpressionSyntax expression)
				return expression;
			if (lambda.Body is BlockSyntax block
				&& block.Statements.Count == 1
				&& block.Statements[0] is ReturnStatementSyntax returned)
				return returned.Expression;
			return null;
		}

		static bool TryEncodeOrder(ExpressionSyntax body, List<Field> fields, out int[] order)
		{
			order = [];
			var elements = new List<ExpressionSyntax>();
			if (!TryArrayElements(body, elements))
				return false;
			order = new int[elements.Count];
			for (var index = 0; index < elements.Count; index++)
			{
				if (!TryFieldIndex(elements[index], fields, out order[index]))
					return false;
			}

			return order.Length > 0;
		}

		static bool TryArrayElements(ExpressionSyntax expression, List<ExpressionSyntax> elements)
		{
			switch (expression)
			{
				case ImplicitArrayCreationExpressionSyntax array when array.Initializer is not null:
					elements.AddRange(array.Initializer.Expressions);
					return elements.Count > 0;
				case ArrayCreationExpressionSyntax array when array.Initializer is not null:
					elements.AddRange(array.Initializer.Expressions);
					return elements.Count > 0;
				case CollectionExpressionSyntax collection:
					foreach (var item in collection.Elements)
					{
						if (item is not ExpressionElementSyntax expressionElement)
							return false;
						elements.Add(expressionElement.Expression);
					}

					return elements.Count > 0;
				default:
					return false;
			}
		}

		static bool TryFillMap(ExpressionSyntax expression, List<Field> fields, out int[] map)
		{
			map = new int[fields.Count];
			Array.Fill(map, -1);
			if (expression is not ObjectCreationExpressionSyntax creation || creation.Initializer is null)
				return false;
			foreach (var item in creation.Initializer.Expressions)
			{
				if (item is not AssignmentExpressionSyntax assignment || assignment.Left is not IdentifierNameSyntax name)
					return false;
				if (!TryWireIndex(assignment.Right, out var wire))
					return false;
				var slot = fields.FindIndex(field => field.Name == name.Identifier.ValueText);
				if (slot < 0)
					return false;
				map[slot] = wire;
			}

			foreach (var slot in map)
			{
				if (slot < 0)
					return false;
			}

			return map.Length > 0;
		}

		static bool TryFieldIndex(ExpressionSyntax expression, List<Field> fields, out int index)
		{
			var access = expression;
			if (expression is ConditionalExpressionSyntax conditional)
				access = conditional.Condition;
			if (access is not MemberAccessExpressionSyntax member)
			{
				index = -1;
				return false;
			}

			index = fields.FindIndex(field => field.Name == member.Name.Identifier.ValueText);
			return index >= 0;
		}

		static bool TryWireIndex(ExpressionSyntax expression, out int index)
		{
			switch (expression)
			{
				case ConditionalExpressionSyntax conditional:
					return TryWireIndex(conditional.WhenTrue, out index);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.LogicalAndExpression):
					return TryWireIndex(binary.Right, out index) || TryWireIndex(binary.Left, out index);
				case ElementAccessExpressionSyntax access when access.ArgumentList.Arguments.Count == 1
					&& access.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
					&& literal.Token.Value is int value
					&& value >= 0:
					index = value;
					return true;
				default:
					index = -1;
					return false;
			}
		}

		bool FillRows(InvocationExpressionSyntax invocation, List<byte> script)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args.Count is not (2 or 3)
				|| args[0].Expression is not MemberAccessExpressionSyntax listMember
				|| !Member(listMember, out var list)
				|| list.Kind != ValueKind.List
				|| args[1].Expression is not MemberAccessExpressionSyntax textMember
				|| !Member(textMember, out var text)
				|| text.Kind != ValueKind.Str
				|| (args.Count == 3 && args[2].Expression is not LambdaExpressionSyntax))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (!_records.TryGetValue(list.Item, out var itemFields))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			int[] map;
			if (args.Count == 2)
			{
				map = new int[itemFields.Count];
				for (var index = 0; index < map.Length; index++)
					map[index] = index;
			}
			else if (args[2].Expression is not LambdaExpressionSyntax lambda || LambdaBody(lambda) is not { } body || !TryFillMap(body, itemFields, out map))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (map.Length > byte.MaxValue)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			script.Add(RecordProgram.ActFillRows);
			U16(script, _code.Intern(list.Name));
			U16(script, _code.Intern(text.Name));
			script.Add((byte)map.Length);
			foreach (var slot in map)
				U16(script, slot);
			return true;
		}

		bool ModuleArg(ExpressionSyntax expression, List<(byte Kind, int Index)> encoded)
		{
			if (expression is InvocationExpressionSyntax encode
				&& encode.Expression is MemberAccessExpressionSyntax encodeMember
				&& encodeMember.Expression is IdentifierNameSyntax encodeType
				&& encodeType.Identifier.ValueText == "PersistRows"
				&& encodeMember.Name.Identifier.ValueText == "Encode"
				&& encode.ArgumentList.Arguments.Count is 1 or 2
				&& encode.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax listMember
				&& Member(listMember, out var list)
				&& list.Kind == ValueKind.List
				&& (encode.ArgumentList.Arguments.Count == 1 || encode.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax))
			{
				if (encode.ArgumentList.Arguments.Count == 1)
				{
					encoded.Add((3, _code.Intern(list.Name)));
					return true;
				}

				if (encode.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax lambda
					|| LambdaBody(lambda) is not { } body
					|| !_records.TryGetValue(list.Item, out var itemFields)
					|| !TryEncodeOrder(body, itemFields, out var order))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
				encoded.Add((4, _code.Intern(list.Name + "\n" + string.Join(',', order))));
				return true;
			}

			if (expression is MemberAccessExpressionSyntax theme
				&& theme.Expression is IdentifierNameSyntax themeType
				&& themeType.Identifier.ValueText == "ThemeMode")
			{
				var text = theme.Name.Identifier.ValueText switch
				{
					"Dark" => "dark",
					"Light" => "light",
					"System" => "system",
					_ => "",
				};
				if (text.Length == 0)
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
				encoded.Add((0, _code.Intern(text)));
				return true;
			}

			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				encoded.Add((0, _code.Intern(literal.Token.ValueText)));
				return true;
			}

			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Str)
			{
				encoded.Add((1, _code.Intern(field.Name)));
				return true;
			}

			if (expression is MemberAccessExpressionSyntax flag && Member(flag, out var boolean) && boolean.Kind == ValueKind.Bool)
			{
				encoded.Add((2, _code.Intern(boolean.Name)));
				return true;
			}

			if (expression is LiteralExpressionSyntax number && number.IsKind(SyntaxKind.NumericLiteralExpression) && number.Token.Value is int ms && ms >= 0)
			{
				encoded.Add((0, _code.Intern(ms.ToString(CultureInfo.InvariantCulture))));
				return true;
			}

			return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
		}

		bool Telemetry(string name, List<byte> script)
		{
			script.Add(RecordProgram.ActModule);
			U16(script, _code.Intern("telemetry"));
			U16(script, _code.Intern(name));
			script.Add(0);
			return true;
		}

		bool JniCall(string owner, string method, InvocationExpressionSyntax invocation, List<byte> script)
		{
			var call = owner + "." + method;
			var encoded = new List<(byte Kind, int Index)>();
			foreach (var argument in invocation.ArgumentList.Arguments)
			{
				if (!JniArg(argument.Expression, encoded, call))
					return false;
			}

			script.Add(RecordProgram.ActModule);
			U16(script, _code.Intern("jni"));
			U16(script, _code.Intern(call));
			script.Add((byte)encoded.Count);
			foreach (var (kind, index) in encoded)
			{
				script.Add(kind);
				U16(script, index);
			}

			return true;
		}

		bool JniArg(ExpressionSyntax expression, List<(byte Kind, int Index)> encoded, string call)
		{
			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				encoded.Add((0, _code.Intern(literal.Token.ValueText)));
				return true;
			}

			if (expression is LiteralExpressionSyntax number && number.IsKind(SyntaxKind.NumericLiteralExpression) && number.Token.Value is int value && value >= 0)
			{
				encoded.Add((0, _code.Intern(value.ToString(CultureInfo.InvariantCulture))));
				return true;
			}

			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Str)
			{
				encoded.Add((1, _code.Intern(field.Name)));
				return true;
			}

			if (expression is MemberAccessExpressionSyntax flag && Member(flag, out var boolean) && boolean.Kind == ValueKind.Bool)
			{
				encoded.Add((2, _code.Intern(boolean.Name)));
				return true;
			}

			return Fail(BadLambda, "Not in the subset: " + call + "(). Pass a string, a number, or a state property.", expression);
		}

		bool AwaitLocal(byte slot, AwaitExpressionSyntax awaited, List<byte> script)
		{
			if (awaited.Expression is not InvocationExpressionSyntax invocation
				|| invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", awaited);
			var encoded = new List<(byte Kind, int Index)>();
			if (!AwaitArgs(owner.Identifier.ValueText, member.Name.Identifier.ValueText, invocation, encoded))
				return false;
			script.Add(RecordProgram.ActAwaitLocal);
			U16(script, _code.Intern(owner.Identifier.ValueText.ToLowerInvariant()));
			U16(script, _code.Intern(member.Name.Identifier.ValueText));
			script.Add((byte)encoded.Count);
			foreach (var (kind, index) in encoded)
			{
				script.Add(kind);
				U16(script, index);
			}

			script.Add(slot);
			return true;
		}

		bool AwaitStore(string dest, AwaitExpressionSyntax awaited, List<byte> script)
		{
			if (awaited.Expression is not InvocationExpressionSyntax invocation
				|| invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", awaited);
			var encoded = new List<(byte Kind, int Index)>();
			if (!AwaitArgs(owner.Identifier.ValueText, member.Name.Identifier.ValueText, invocation, encoded))
				return false;
			script.Add(RecordProgram.ActAwait);
			U16(script, _code.Intern(owner.Identifier.ValueText.ToLowerInvariant()));
			U16(script, _code.Intern(member.Name.Identifier.ValueText));
			script.Add((byte)encoded.Count);
			foreach (var (kind, index) in encoded)
			{
				script.Add(kind);
				U16(script, index);
			}

			U16(script, _code.Intern(dest));
			return true;
		}

		bool AwaitArgs(string module, string method, InvocationExpressionSyntax invocation, List<(byte Kind, int Index)> encoded)
		{
			var args = invocation.ArgumentList.Arguments;
			if (args.Count == 0 || args[0].Expression is not IdentifierNameSyntax platform || platform.Identifier.ValueText != _platform)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			if (module == "Http" && method == "GetJson" && args.Count == 2)
				return ModuleArg(args[1].Expression, encoded);
			if (module == "Http" && method == "PostJson" && args.Count == 3)
				return ModuleArg(args[1].Expression, encoded) && ModuleArg(args[2].Expression, encoded);
			if (module == "Storage" && method == "Get" && args.Count == 2)
			{
				if (args[1].Expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression))
					return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", args[1].Expression);
				encoded.Add((0, _code.Intern(key.Token.ValueText)));
				return true;
			}

			if (module == "Timer" && method == "After" && args.Count == 2)
				return ModuleArg(args[1].Expression, encoded);
			if (module == "Location" && method == "Read" && args.Count == 1)
				return true;
			if (module == "Persist" && method == "Get" && args.Count == 2)
				return PersistKey(args[1].Expression, encoded);
			if (module == "Persist" && method == "Put" && args.Count == 3)
				return PersistKey(args[1].Expression, encoded) && ModuleArg(args[2].Expression, encoded);
			if (module == "Persist" && method == "GetRecord" && args.Count == 2)
				return PersistKey(args[1].Expression, encoded);
			if (module == "Persist" && method == "PutRecord" && args.Count == 5)
				return PersistKey(args[1].Expression, encoded)
					&& ModuleArg(args[2].Expression, encoded)
					&& ModuleArg(args[3].Expression, encoded)
					&& ModuleArg(args[4].Expression, encoded);
			return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
		}

		bool PersistKey(ExpressionSyntax expression, List<(byte Kind, int Index)> encoded)
		{
			if (expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression) || key.Token.ValueText.Length == 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
			encoded.Add((0, _code.Intern(key.Token.ValueText)));
			return true;
		}

		bool StateCall(string name, List<byte> script, InvocationExpressionSyntax invocation)
		{
			if (name == "Commit")
				return Commit(script);
			if (_routines.ContainsKey(_stateType + "." + name))
				return false;
			if (!_methods.TryGetValue(_stateType + "." + name, out var method) || method.Body is null)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", invocation);
			foreach (var statement in method.Body.Statements)
			{
				if (!Statement(statement, "", script))
					return false;
			}

			return true;
		}

		bool Commit(List<byte> script)
		{
			var list = _fields.FirstOrDefault(field => field.Kind == ValueKind.List);
			if (list is null || !_records.TryGetValue(list.Item, out var itemFields))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", script.Count.ToString(CultureInfo.InvariantCulture));
			var id = itemFields.FindIndex(field => field.Name == "Id");
			var rowName = itemFields.FindIndex(field => field.Name == "Name");
			var packed = itemFields.FindIndex(field => field.Name == "Packed");
			if (id < 0 || rowName < 0 || packed < 0)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", list.Name);
			script.Add(RecordProgram.ActCommitDraft);
			U16(script, _code.Intern(list.Name));
			U16(script, id);
			U16(script, rowName);
			U16(script, packed);
			U16(script, _code.Intern("DraftName"));
			U16(script, _code.Intern("DraftPacked"));
			U16(script, _code.Intern("EditingId"));
			U16(script, _code.Intern("NextNumber"));
			U16(script, _code.Intern("Editing"));
			U16(script, _code.Intern("item-"));
			return true;
		}

		bool CopyRows(InvocationExpressionSyntax invocation, List<byte> script)
		{
			var label = invocation.ArgumentList.Arguments[0].Expression;
			var rows = invocation.ArgumentList.Arguments[1].Expression;
			if (label is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", label);
			if (rows is not MemberAccessExpressionSyntax member || !Member(member, out var field) || field.Kind != ValueKind.ListText)
				return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", rows);
			script.Add(RecordProgram.ActCopyRows);
			U16(script, _code.Intern(field.SourceList));
			U16(script, field.NameIndex);
			U16(script, field.FlagIndex);
			U16(script, _code.Intern(literal.Token.ValueText));
			return true;
		}

		bool Copy(ExpressionSyntax expression, List<byte> script)
		{
			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				return Act(script, RecordProgram.ActCopyLit, _code.Intern(literal.Token.ValueText));
			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Str)
				return Act(script, RecordProgram.ActCopyProp, _code.Intern(field.Name));
			if (expression is BinaryExpressionSyntax binary
				&& binary.IsKind(SyntaxKind.AddExpression)
				&& binary.Left is LiteralExpressionSyntax prefix
				&& prefix.IsKind(SyntaxKind.StringLiteralExpression)
				&& binary.Right is MemberAccessExpressionSyntax number
				&& Member(number, out var count)
				&& count.Kind == ValueKind.Int)
			{
				Act(script, RecordProgram.ActCopyPrefixInt, _code.Intern(prefix.Token.ValueText));
				U16(script, _code.Intern(count.Name));
				return true;
			}

			return Fail(BadLambda, "A screen action can update state, branch, loop, or call a method compiled into the binary.", expression);
		}

		static bool Act(List<byte> script, byte op, int name)
		{
			script.Add(op);
			U16(script, name);
			return true;
		}

		static void U16(List<byte> script, int value)
		{
			script.Add((byte)value);
			script.Add((byte)(value >> 8));
		}

		public bool Fail(string id, string message, SyntaxNode node)
		{
			if (id == BadLambda || id == UnknownExpression)
			{
				var snip = Snip(node);
				if (snip.Length > 0)
					message += " Not in the subset: " + snip + ".";
			}

			if (id == UnknownExpression)
				message += " Use a property, string concat, a comparison, or list Add/Remove.";

			Errors.Add(At(id, message, node, _source));
			return false;
		}

		static string Snip(SyntaxNode node)
		{
			while (node.Parent is not null && node.ToString().Length < 12)
				node = node.Parent;
			var text = node.ToString().Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("\t", " ", StringComparison.Ordinal);
			while (text.Contains("  ", StringComparison.Ordinal))
				text = text.Replace("  ", " ", StringComparison.Ordinal);
			text = text.Trim();
			if (text.Length > 80)
				text = text.Substring(0, 77) + "...";
			return text;
		}

		bool Fail(string id, string message, string name)
		{
			Errors.Add(new SubsetDiagnostic(id, message, 0, 0, 1, 1));
			_ = name;
			return false;
		}

		static string TypeName(ObjectCreationExpressionSyntax creation) => creation.Type.ToString();
	}

	sealed class KeySet
	{
		readonly HashSet<string> _keys = new(StringComparer.Ordinal);

		public bool Add(Lower lower, int key, bool dynamic, SyntaxNode node)
		{
			if (!dynamic && key != RecordProgram.NoKey)
			{
				var text = node is ObjectCreationExpressionSyntax creation
					&& creation.Initializer?.Expressions.FirstOrDefault() is AssignmentExpressionSyntax assignment
					&& assignment.Right is LiteralExpressionSyntax literal
					? literal.Token.ValueText
					: "";
				if (text.Length > 0 && !_keys.Add(text))
					return lower.Fail(DuplicateKey, "Duplicate element key \"" + text + "\". Give this child a different Key. Inside a list, use Key = item.Id.", node);
			}

			return true;
		}

		public bool Close(Lower lower, SyntaxNode node)
		{
			_ = lower;
			_ = node;
			return true;
		}
	}

	sealed class Builder
	{
		readonly List<string> _strings = new();
		readonly List<byte> _code = new();
		readonly List<RecordProgram.PropDef> _props = new();

		public int Here => _code.Count;

		public int Intern(string text)
		{
			var index = _strings.IndexOf(text);
			if (index >= 0)
				return index;
			_strings.Add(text);
			return _strings.Count - 1;
		}

		public void Prop(RecordProgram.PropDef prop) => _props.Add(prop);

		public void Op(byte op) => _code.Add(op);

		public void U8(byte value) => _code.Add(value);

		public void U16(int value)
		{
			_code.Add((byte)value);
			_code.Add((byte)(value >> 8));
		}

		public int EmitJump(byte op)
		{
			Op(op);
			var at = _code.Count;
			_code.Add(0);
			_code.Add(0);
			return at;
		}

		public void Patch(int at) => PatchAt(at, _code.Count - (at + 2));

		public void PatchAt(int at, int relative)
		{
			_code[at] = (byte)relative;
			_code[at + 1] = (byte)(relative >> 8);
		}

		public void Truncate(int at)
		{
			if (at < _code.Count)
				_code.RemoveRange(at, _code.Count - at);
		}

		public byte ByteAt(int at) => _code[at];

		public void Bytes(byte[] script)
		{
			foreach (var item in script)
				_code.Add(item);
		}

		public byte[] Finish(string source) => RecordProgram.Encode(_strings, _props, _code.ToArray(), source);
	}
}
