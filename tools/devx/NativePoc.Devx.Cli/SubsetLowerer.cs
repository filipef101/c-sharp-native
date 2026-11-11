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

	public static SubsetResult Ready(byte[] program) => new(program, Array.Empty<SubsetDiagnostic>());

	public static SubsetResult Refuse(SubsetDiagnostic diagnostic) => new(null, [diagnostic]);
}

static class SubsetLowerer
{
	public const string NotAScreen = "subset.not-a-screen";
	public const string UnknownMember = "subset.unknown-member";
	public const string UnknownElement = "subset.unknown-element";
	public const string UnknownExpression = "subset.unknown-expression";
	public const string BadLambda = "subset.bad-lambda";
	public const string BadForeach = "subset.bad-foreach";
	public const string DuplicateKey = "subset.duplicate-key";
	public const string MixedKeys = "subset.mixed-keys";
	public const string UnknownType = "subset.unknown-type";

	public static SubsetResult TryLower(string source)
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
		ClassDeclarationSyntax? screen = null;
		foreach (var member in members)
		{
			switch (member)
			{
				case RecordDeclarationSyntax record:
					if (!RecordFields(record, records, source, out var recordError))
						return SubsetResult.Refuse(recordError!);
					break;
				case ClassDeclarationSyntax type when type.Modifiers.Any(SyntaxKind.StaticKeyword):
					if (screen is not null)
						return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", type, source));
					screen = type;
					break;
				case ClassDeclarationSyntax type:
					if (!RecordFields(type, records, source, out var classError))
						return SubsetResult.Refuse(classError!);
					break;
				default:
					return SubsetResult.Refuse(At(UnknownMember, "The screen subset does not include " + MemberName(member) + ".", member, source));
			}
		}

		if (screen is null)
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", root, source));
		MethodDeclarationSyntax? render = null;
		foreach (var member in screen.Members)
		{
			if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == "Render")
			{
				if (render is not null)
					return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", method, source));
				render = method;
				continue;
			}

			return SubsetResult.Refuse(At(UnknownMember, "The screen subset does not include " + MemberName(member) + ".", member, source));
		}

		if (render is null || !IsRender(render, out var stateName, out var stateType, out var platformName))
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", render ?? (SyntaxNode)screen, source));
		if (!records.TryGetValue(stateType, out var fields))
			return SubsetResult.Refuse(At(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", render, source));

		var lower = new Lower(source, stateName, platformName, fields, records);
		try
		{
			if (!lower.Body(render))
				return new SubsetResult(null, lower.Errors);
			return SubsetResult.Ready(lower.Finish());
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

	static bool RecordFields(TypeDeclarationSyntax type, Dictionary<string, List<Field>> records, string source, out SubsetDiagnostic? error)
	{
		error = null;
		var fields = new List<Field>();
		foreach (var member in type.Members)
		{
			if (member is not PropertyDeclarationSyntax property)
			{
				error = At(UnknownMember, "The screen subset does not include " + MemberName(member) + ".", member, source);
				return false;
			}

			if (!Property(property, records, source, out var field, out error) || field is null)
				return false;
			fields.Add(field);
		}

		if (!records.TryAdd(type.Identifier.ValueText, fields))
		{
			error = At(UnknownMember, "The screen subset does not include " + type.Identifier.ValueText + ".", type, source);
			return false;
		}

		return true;
	}

	static bool Property(PropertyDeclarationSyntax property, Dictionary<string, List<Field>> records, string source, out Field? field, out SubsetDiagnostic? error)
	{
		field = null;
		error = null;
		var name = property.Identifier.ValueText;
		if (IsList(property.Type, out var itemType))
		{
			if (!records.ContainsKey(itemType))
			{
				error = At(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", property, source);
				return false;
			}

			if (!Rows(property.Initializer?.Value, records[itemType], source, out var rows, out error) || rows is null)
				return false;
			field = Field.List(name, itemType, rows);
			return true;
		}

		var kind = KindOf(property.Type);
		if (kind is null)
		{
			error = At(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", property, source);
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

		error = At(UnknownExpression, "The screen subset does not include this expression.", literal, source);
		return false;
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
				|| assign.Right is not LiteralExpressionSyntax literal
				|| !literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				error = At(UnknownExpression, "The screen subset does not include this expression.", assignment, source);
				return false;
			}

			var index = fields.FindIndex(field => field.Name == name.Identifier.ValueText && field.Kind == ValueKind.Str);
			if (index < 0)
			{
				error = At(UnknownMember, "The screen subset does not include " + name.Identifier.ValueText + ".", name, source);
				return false;
			}

			cells[index] = literal.Token.ValueText;
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

	static ValueKind? KindOf(TypeSyntax type) =>
		type.ToString() switch
		{
			"string" => ValueKind.Str,
			"bool" => ValueKind.Bool,
			"int" => ValueKind.Int,
			_ => null,
		};

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

	static SubsetDiagnostic At(string id, string message, Location location, string source)
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

		public static Field Scalar(string name, ValueKind kind, string text, bool flag, int number) =>
			new(name, kind, text, flag, number, "", null);

		public static Field List(string name, string item, List<string[]> rows) =>
			new(name, ValueKind.List, "", false, 0, item, rows);
	}

	sealed class Lower
	{
		readonly string _source;
		readonly string _state;
		readonly string _platform;
		readonly List<Field> _fields;
		readonly Dictionary<string, List<Field>> _records;
		readonly Builder _code = new();
		string _item = "";
		List<Field>? _itemFields;
		string _rows = "";

		public Lower(string source, string state, string platform, List<Field> fields, Dictionary<string, List<Field>> records)
		{
			_source = source;
			_state = state;
			_platform = platform;
			_fields = fields;
			_records = records;
		}

		public List<SubsetDiagnostic> Errors { get; } = new();

		public bool Body(MethodDeclarationSyntax method)
		{
			if (!Seed())
				return false;
			if (method.ExpressionBody is not null)
				return Element(method.ExpressionBody.Expression, null) && Return();
			if (method.Body is null)
				return Fail(NotAScreen, "A subset screen is a static class with Render(state, IPlatform).", method);
			return Block(method.Body);
		}

		public byte[] Finish() => _code.Finish(_source);

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
					case ValueKind.List:
						if (field.Rows is null || !_records.TryGetValue(field.Item, out var itemFields))
							return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
						var names = new int[itemFields.Count];
						for (var index = 0; index < itemFields.Count; index++)
						{
							if (itemFields[index].Kind != ValueKind.Str)
								return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
							names[index] = _code.Intern(itemFields[index].Name);
						}

						var rows = new int[field.Rows.Count][];
						for (var row = 0; row < field.Rows.Count; row++)
						{
							rows[row] = new int[names.Length];
							for (var cell = 0; cell < names.Length; cell++)
								rows[row][cell] = _code.Intern(cell < field.Rows[row].Length ? field.Rows[row][cell] : "");
						}

						_code.Prop(new RecordProgram.PropDef(RecordProgram.TypeList, name, 0, false, 0, names, rows));
						break;
					default:
						return Fail(UnknownType, "A screen property is a string, a bool, an int, or a list of records.", field.Name);
				}
			}

			return Errors.Count == 0;
		}

		bool Block(BlockSyntax block)
		{
			var statements = block.Statements;
			if (statements.Count == 1 && statements[0] is ReturnStatementSyntax only)
				return Returned(only);
			if (statements.Count == 2
				&& statements[0] is IfStatementSyntax branch
				&& branch.Else is null
				&& branch.Statement is ReturnStatementSyntax whenTrue
				&& statements[1] is ReturnStatementSyntax whenFalse)
			{
				if (!Bool(branch.Condition))
					return false;
				var jump = _code.EmitJump(RecordProgram.OpJumpIfFalse);
				if (!Returned(whenTrue))
					return false;
				_code.Patch(jump);
				return Returned(whenFalse);
			}

			if (statements.Count == 3
				&& statements[0] is LocalDeclarationStatementSyntax local
				&& statements[1] is ForEachStatementSyntax loop
				&& statements[2] is ReturnStatementSyntax ret)
				return Foreach(local, loop, ret);
			return Fail(UnknownExpression, "The screen subset does not include this expression.", block);
		}

		bool Foreach(LocalDeclarationStatementSyntax local, ForEachStatementSyntax loop, ReturnStatementSyntax ret)
		{
			if (local.Declaration.Variables.Count != 1)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", local);
			var variable = local.Declaration.Variables[0];
			_rows = variable.Identifier.ValueText;
			if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax created || !created.Type.ToString().StartsWith("List<Element>", StringComparison.Ordinal))
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", variable);
			if (loop.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _state)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", loop.Expression);
			var list = _fields.FirstOrDefault(field => field.Name == member.Name.Identifier.ValueText && field.Kind == ValueKind.List);
			if (list is null || !_records.TryGetValue(list.Item, out var itemFields))
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", member);
			var statement = loop.Statement is BlockSyntax body && body.Statements.Count == 1 ? body.Statements[0] : loop.Statement;
			if (statement is not ExpressionStatementSyntax expr
				|| expr.Expression is not InvocationExpressionSyntax add
				|| add.Expression is not MemberAccessExpressionSyntax target
				|| target.Name.Identifier.ValueText != "Add"
				|| target.Expression is not IdentifierNameSyntax rows
				|| rows.Identifier.ValueText != _rows
				|| add.ArgumentList.Arguments.Count != 1)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", loop);
			_code.Op(RecordProgram.OpMark);
			_code.Op(RecordProgram.OpForEach);
			_code.U16(_code.Intern(list.Name));
			var lengthAt = _code.Here;
			_code.U16(0);
			var start = _code.Here;
			_item = loop.Identifier.ValueText;
			_itemFields = itemFields;
			var keys = new KeySet();
			if (!Element(add.ArgumentList.Arguments[0].Expression, keys))
				return false;
			_item = "";
			_itemFields = null;
			var length = _code.Here - start;
			_code.PatchAt(lengthAt, length);
			if (ret.Expression is null)
				return Fail(BadForeach, "A foreach walks a list property and adds one element.", ret);
			return Gathered(ret.Expression, keys) && Return();
		}

		bool Gathered(ExpressionSyntax expression, KeySet keys)
		{
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
				return true;
			}

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
			return true;
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
					return Control(creation, set, RecordProgram.OpMakeField, 4);
				case "Switch":
					return Control(creation, set, RecordProgram.OpMakeSwitch, 3);
				case "Column":
					return Column(creation, set);
				case "ScrollView":
					return Scroll(creation, set);
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
				return true;
			}

			_code.Op(RecordProgram.OpMakeText);
			_code.U16(key);
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
					return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", args[1].Expression);
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
					return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", args[1].Expression);
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
					return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", args[1].Expression);
				if (!TextValue(args[2].Expression))
					return false;
				if (!Script(buttonLambda, out script))
					return false;
			}

			return FinishControl(creation, keys, op, script);
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
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", creation);
			_code.Op(op);
			_code.U16(key);
			_code.U16(script.Length);
			_code.Bytes(script);
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
			if (!KeyOf(creation, out var key, out var dynamic, out _))
				return false;
			if (dynamic)
				return Fail(UnknownExpression, "The screen subset does not include this expression.", creation);
			if (!keys.Add(this, key, false, creation))
				return false;
			if (children.Count > ScreenProgram.MaxChildren)
				return Fail(UnknownElement, "The screen subset does not include Column.", creation);
			_code.Op(RecordProgram.OpMakeColumn);
			_code.U8((byte)children.Count);
			_code.U16(key);
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
				default:
					return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			}
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
				case MemberAccessExpressionSyntax member when Member(member, out var field) && field.Kind == ValueKind.Bool:
					_code.Op(RecordProgram.OpLoadBool);
					_code.U16(_code.Intern(field.Name));
					return true;
				default:
					return Fail(UnknownExpression, "The screen subset does not include this expression.", expression);
			}
		}

		bool Choice(ConditionalExpressionSyntax conditional, Func<ExpressionSyntax, bool> push)
		{
			if (!Bool(conditional.Condition))
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

			return Fail(UnknownExpression, "The screen subset does not include this expression.", member);
		}

		bool Member(MemberAccessExpressionSyntax member, out Field field)
		{
			field = null!;
			if (member.Expression is not IdentifierNameSyntax owner || owner.Identifier.ValueText != _state)
				return false;
			var found = _fields.FirstOrDefault(item => item.Name == member.Name.Identifier.ValueText && item.Kind != ValueKind.List);
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
			if (creation.Initializer is null)
				return true;
			if (creation.Initializer.Expressions.Count != 1
				|| creation.Initializer.Expressions[0] is not AssignmentExpressionSyntax assignment
				|| assignment.Left is not IdentifierNameSyntax name
				|| name.Identifier.ValueText != "Key")
				return Fail(UnknownMember, "The screen subset does not include an element initializer.", creation.Initializer);
			if (assignment.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				key = _code.Intern(literal.Token.ValueText);
				return true;
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
				return true;
			}

			return Fail(UnknownExpression, "The screen subset does not include this expression.", assignment.Right);
		}

		bool Script(LambdaExpressionSyntax lambda, out byte[] script)
		{
			script = Array.Empty<byte>();
			var parameter = lambda switch
			{
				SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
				ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1 => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
				ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 0 => "",
				_ => null,
			};
			if (parameter is null)
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", lambda);
			var bytes = new List<byte>();
			if (lambda.Body is ExpressionSyntax single)
			{
				if (!Action(single, parameter, bytes))
					return false;
			}
			else if (lambda.Body is BlockSyntax block)
			{
				foreach (var statement in block.Statements)
				{
					if (statement is not ExpressionStatementSyntax expr || !Action(expr.Expression, parameter, bytes))
						return false;
				}
			}
			else
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", lambda);

			script = bytes.ToArray();
			return true;
		}

		bool Action(ExpressionSyntax expression, string parameter, List<byte> script)
		{
			switch (expression)
			{
				case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
					return Assign(assignment, parameter, script);
				case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression):
					return Increment(postfix.Operand, script);
				case InvocationExpressionSyntax invocation:
					return Call(invocation, script);
				default:
					return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", expression);
			}
		}

		bool Assign(AssignmentExpressionSyntax assignment, string parameter, List<byte> script)
		{
			if (assignment.Left is not MemberAccessExpressionSyntax member || !Member(member, out var field))
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", assignment);
			if (assignment.Right is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter && parameter.Length > 0)
			{
				if (field.Kind == ValueKind.Str)
					return Act(script, RecordProgram.ActSetStr, _code.Intern(field.Name));
				if (field.Kind == ValueKind.Bool)
					return Act(script, RecordProgram.ActSetBool, _code.Intern(field.Name));
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", assignment);
			}

			if (assignment.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) && field.Kind == ValueKind.Str)
			{
				Act(script, RecordProgram.ActSetStrLit, _code.Intern(field.Name));
				U16(script, _code.Intern(literal.Token.ValueText));
				return true;
			}

			if (field.Kind == ValueKind.Bool && assignment.Right is LiteralExpressionSyntax flag && (flag.IsKind(SyntaxKind.TrueLiteralExpression) || flag.IsKind(SyntaxKind.FalseLiteralExpression)))
			{
				Act(script, RecordProgram.ActSetBoolLit, _code.Intern(field.Name));
				script.Add(flag.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
				return true;
			}

			return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", assignment);
		}

		bool Increment(ExpressionSyntax operand, List<byte> script)
		{
			if (operand is not MemberAccessExpressionSyntax member || !Member(member, out var field) || field.Kind != ValueKind.Int)
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", operand);
			return Act(script, RecordProgram.ActInc, _code.Intern(field.Name));
		}

		bool Call(InvocationExpressionSyntax invocation, List<byte> script)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax member
				|| member.Expression is not IdentifierNameSyntax owner
				|| owner.Identifier.ValueText != _platform)
				return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", invocation);
			if (member.Name.Identifier.ValueText == "RequestRender" && invocation.ArgumentList.Arguments.Count == 0)
			{
				script.Add(RecordProgram.ActRender);
				return true;
			}

			if (member.Name.Identifier.ValueText == "CopyNonce" && invocation.ArgumentList.Arguments.Count == 1)
				return Copy(invocation.ArgumentList.Arguments[0].Expression, script);
			return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", invocation);
		}

		bool Copy(ExpressionSyntax expression, List<byte> script)
		{
			if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
				return Act(script, RecordProgram.ActCopyLit, _code.Intern(literal.Token.ValueText));
			if (expression is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Str)
				return Act(script, RecordProgram.ActCopyProp, _code.Intern(field.Name));
			return Fail(BadLambda, "A screen action can assign state, request a render, or copy text.", expression);
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
			Errors.Add(At(id, message, node, _source));
			return false;
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
		bool _keyed;
		bool _plain;
		int _count;

		public bool Add(Lower lower, int key, bool dynamic, SyntaxNode node)
		{
			_count++;
			if (dynamic || key != RecordProgram.NoKey)
			{
				_keyed = true;
				if (_plain)
					return lower.Fail(MixedKeys, "Keyed children cannot be mixed with unkeyed children.", node);
			}
			else
			{
				_plain = true;
				if (_keyed)
					return lower.Fail(MixedKeys, "Keyed children cannot be mixed with unkeyed children.", node);
			}

			if (!dynamic && key != RecordProgram.NoKey)
			{
				var text = node is ObjectCreationExpressionSyntax creation
					&& creation.Initializer?.Expressions.FirstOrDefault() is AssignmentExpressionSyntax assignment
					&& assignment.Right is LiteralExpressionSyntax literal
					? literal.Token.ValueText
					: "";
				if (text.Length > 0 && !_keys.Add(text))
					return lower.Fail(DuplicateKey, "Duplicate element key " + text + ".", node);
			}

			return true;
		}

		public bool Close(Lower lower, SyntaxNode node)
		{
			if (_count > 0 && _keyed && _plain)
				return lower.Fail(MixedKeys, "Keyed children cannot be mixed with unkeyed children.", node);
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

		public void Bytes(byte[] script)
		{
			foreach (var item in script)
				_code.Add(item);
		}

		public byte[] Finish(string source) => RecordProgram.Encode(_strings, _props, _code.ToArray(), source);
	}
}
