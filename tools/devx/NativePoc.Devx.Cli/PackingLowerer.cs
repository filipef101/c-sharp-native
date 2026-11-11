using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NativePoc;

namespace NativePoc.Devx;

static class PackingLowerer
{
	enum Place
	{
		Row,
		Editor,
		List,
	}

	abstract record Piece(string Key);

	sealed record LiteralText(string Key, string Value) : Piece(Key);

	sealed record ItemName(string Key) : Piece(Key);

	sealed record ItemPacked(string Key, string Prefix, bool WithName) : Piece(Key);

	sealed record ItemEdit(string Key, string Title, string Prefix, bool WithName) : Piece(Key);

	sealed record EditorTitle(string Key, string Newer, string Older) : Piece(Key);

	sealed record DraftField(string Key, string Placeholder, string Label, List<PackingActionLowerer.Act> Script, bool Note) : Piece(Key);

	sealed record DraftPacked(string Key, string Label, List<PackingActionLowerer.Act> Script) : Piece(Key);

	sealed record ActionButton(string Key, string Title, string Label, byte Code) : Piece(Key);

	sealed record Progress(string Key, string Mid, string Suffix) : Piece(Key);

	sealed record Rows(List<Piece> Children, List<PackingActionLowerer.Act> Packed) : Piece("");

	const byte RemoveCode = 8;

	readonly record struct Built(string Key, List<Piece> Pieces);

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
			|| space.Name.ToString() != "NativePoc.Packing")
			return LowerResult.Refuse("The packing program is the NativePoc.Packing file.");

		InterfaceDeclarationSyntax? host = null;
		ClassDeclarationSyntax? row = null;
		ClassDeclarationSyntax? editor = null;
		ClassDeclarationSyntax? list = null;
		ClassDeclarationSyntax? actions = null;
		ClassDeclarationSyntax? screen = null;
		foreach (var member in space.Members)
		{
			switch (member)
			{
				case InterfaceDeclarationSyntax iface when iface.Identifier.ValueText == "IPackingHost" && host is null:
					host = iface;
					break;
				case ClassDeclarationSyntax type when type.Identifier.ValueText == "ItemRow" && row is null:
					row = type;
					break;
				case ClassDeclarationSyntax type when type.Identifier.ValueText == "ItemEditor" && editor is null:
					editor = type;
					break;
				case ClassDeclarationSyntax type when type.Identifier.ValueText == "PackingList" && list is null:
					list = type;
					break;
				case ClassDeclarationSyntax type when type.Identifier.ValueText == "PackingActions" && actions is null:
					actions = type;
					break;
				case ClassDeclarationSyntax type when type.Identifier.ValueText == "PackingScreen" && screen is null:
					screen = type;
					break;
				default:
					return LowerResult.Refuse("A packing program cannot carry " + MemberName(member) + ".");
			}
		}

		if (host is null || row is null || editor is null || list is null || actions is null || screen is null)
			return LowerResult.Refuse("The packing program is missing a screen.");
		if (!HostShape(host))
			return LowerResult.Refuse("The packing host is not the program host.");
		if (Unexpected(row, "View") is string rowMember)
			return LowerResult.Refuse(rowMember == "" ? "The row needs View." : "A packing program cannot carry " + rowMember + ".");
		if (Unexpected(editor, "View") is string editorMember)
			return LowerResult.Refuse(editorMember == "" ? "The editor needs View." : "A packing program cannot carry " + editorMember + ".");
		if (Unexpected(list, "View") is string listMember)
			return LowerResult.Refuse(listMember == "" ? "The packing list needs View." : "A packing program cannot carry " + listMember + ".");
		if (Unexpected(actions, "BeginAdd", "BeginEdit", "Save", "Back", "Copy", "Find") is string actionMember && actionMember != "Remove")
			return LowerResult.Refuse(actionMember == "" ? "The packing actions are incomplete." : "A packing program cannot carry " + actionMember + ".");
		if (!Routes(screen, out var routeReason))
			return LowerResult.Refuse(routeReason ?? "The packing screen has to show the list and the editor.");

		var rowMethod = Method(row, "View");
		var editorMethod = Method(editor, "View");
		var listMethod = Method(list, "View");
		if (rowMethod is null || editorMethod is null || listMethod is null)
			return LowerResult.Refuse("The packing program is missing a screen.");
		if (!RowBody(rowMethod, out var rowPieces, out var rowReason) || rowPieces is null)
			return LowerResult.Refuse(rowReason ?? "The row has to be the column of its fields.");
		if (RequireRow(rowPieces) is string rowProblem)
			return LowerResult.Refuse(rowProblem);
		if (!EditorBody(editorMethod, out var editorBuilt, out var editorReason) || editorBuilt is null)
			return LowerResult.Refuse(editorReason ?? "The editor has to be the column of its fields.");
		var editorBody = editorBuilt.Value;
		if (RequireEditor(editorBody.Pieces) is string editorProblem)
			return LowerResult.Refuse(editorProblem);
		if (!ListBody(listMethod, rowPieces, out var listBuilt, out var listReason) || listBuilt is null)
			return LowerResult.Refuse(listReason ?? "The packing list has to build its column.");
		var listBody = listBuilt.Value;
		if (RequireList(listBody.Pieces) is string listProblem)
			return LowerResult.Refuse(listProblem);
		if (!PackingActionLowerer.TryMethods(actions, out var actionsScripts, out var actionReason))
			return LowerResult.Refuse(actionReason ?? "A packing action can toggle, rename, add, or remove.");

		try
		{
			return LowerResult.Ready(Emit(source, editorBody, listBody, actionsScripts));
		}
		catch (InvalidOperationException ex)
		{
			return LowerResult.Refuse(ex.Message);
		}
	}

	static byte[] Emit(string source, Built editor, Built list, Dictionary<string, List<PackingActionLowerer.Act>> actions)
	{
		var writer = new ScreenProgram.Writer();
		writer.PushEditor();
		var listAt = writer.JumpIfFalse();
		foreach (var piece in editor.Pieces)
			EmitEditor(writer, piece, actions);
		writer.MakeKeyedColumn((byte)editor.Pieces.Count, writer.Intern(editor.Key));
		writer.Return();
		writer.Patch(listAt, writer.Here);
		writer.Mark();
		foreach (var piece in list.Pieces)
		{
			if (piece is Rows rows)
			{
				var items = writer.BeginItems();
				foreach (var child in rows.Children)
					EmitRow(writer, child, rows, actions);
				writer.MakeItemColumn((byte)rows.Children.Count);
				writer.EndItems(items);
				continue;
			}

			EmitList(writer, piece, actions);
		}

		writer.MakeDynamicColumn(writer.Intern(list.Key));
		writer.Return();
		return writer.Finish(source, 1);
	}

	static void EmitEditor(ScreenProgram.Writer writer, Piece piece, Dictionary<string, List<PackingActionLowerer.Act>> actions)
	{
		switch (piece)
		{
			case EditorTitle title:
				writer.PushEditingId();
				writer.IsEmpty();
				var editTitle = writer.JumpIfFalse();
				writer.PushString(writer.Intern(title.Newer));
				var join = writer.Jump();
				writer.Patch(editTitle, writer.Here);
				writer.PushString(writer.Intern(title.Older));
				writer.Patch(join, writer.Here);
				writer.MakeText(writer.Intern(title.Key));
				return;
			case DraftField field:
				if (field.Note)
					writer.PushDraftNote();
				else
					writer.PushDraftName();
				writer.PushString(writer.Intern(field.Placeholder));
				writer.PushString(writer.Intern(field.Label));
				writer.MakeScriptField(writer.Intern(field.Key), PackingActionLowerer.Encode(writer, field.Script));
				return;
			case DraftPacked packed:
				writer.PushDraftPacked();
				writer.PushString(writer.Intern(packed.Label));
				writer.MakeScriptSwitch(writer.Intern(packed.Key), PackingActionLowerer.Encode(writer, packed.Script));
				return;
			case ActionButton button:
				EmitAction(writer, button, actions);
				return;
			case LiteralText text:
				EmitText(writer, text);
				return;
			default:
				throw new InvalidOperationException("The editor has an element this program cannot carry.");
		}
	}

	static void EmitList(ScreenProgram.Writer writer, Piece piece, Dictionary<string, List<PackingActionLowerer.Act>> actions)
	{
		switch (piece)
		{
			case LiteralText text:
				EmitText(writer, text);
				return;
			case Progress progress:
				writer.PushPackedCount();
				writer.PushString(writer.Intern(progress.Mid));
				writer.Concat();
				writer.PushItemCount();
				writer.Concat();
				writer.PushString(writer.Intern(progress.Suffix));
				writer.Concat();
				writer.MakeText(writer.Intern(progress.Key));
				return;
			case ActionButton button:
				EmitAction(writer, button, actions);
				return;
			default:
				throw new InvalidOperationException("The packing list has an element this program cannot carry.");
		}
	}

	static void EmitRow(ScreenProgram.Writer writer, Piece piece, Rows rows, Dictionary<string, List<PackingActionLowerer.Act>> actions)
	{
		switch (piece)
		{
			case ItemName name:
				writer.PushItemName();
				writer.MakeText(writer.Intern(name.Key));
				return;
			case ItemPacked packed:
				writer.PushItemPacked();
				EmitLabel(writer, packed.Prefix, packed.WithName);
				writer.MakeScriptSwitch(writer.Intern(packed.Key), PackingActionLowerer.Encode(writer, rows.Packed));
				return;
			case ItemEdit edit:
				writer.PushString(writer.Intern(edit.Title));
				EmitLabel(writer, edit.Prefix, edit.WithName);
				writer.MakeScriptButton(writer.Intern(edit.Key), PackingActionLowerer.Encode(writer, actions["BeginEdit"]));
				return;
			case ActionButton button when button.Code == RemoveCode:
				writer.PushString(writer.Intern(button.Title));
				writer.PushString(writer.Intern(button.Label));
				writer.MakeScriptButton(writer.Intern(button.Key), PackingActionLowerer.Encode(writer, actions["Remove"]));
				return;
			case LiteralText text:
				EmitText(writer, text);
				return;
			default:
				throw new InvalidOperationException("The row has an element this program cannot carry.");
		}
	}

	static void EmitLabel(ScreenProgram.Writer writer, string prefix, bool withName)
	{
		if (!withName)
		{
			writer.PushString(writer.Intern(prefix));
			return;
		}

		writer.PushString(writer.Intern(prefix));
		writer.PushItemName();
		writer.Concat();
	}

	static void EmitAction(ScreenProgram.Writer writer, ActionButton button, Dictionary<string, List<PackingActionLowerer.Act>> actions)
	{
		var name = button.Code switch
		{
			ScreenProgram.PackSave => "Save",
			ScreenProgram.PackBack => "Back",
			ScreenProgram.PackAdd => "BeginAdd",
			ScreenProgram.PackCopy => "Copy",
			RemoveCode => "Remove",
			_ => throw new InvalidOperationException("A packing action can toggle, rename, add, or remove."),
		};
		writer.PushString(writer.Intern(button.Title));
		writer.PushString(writer.Intern(button.Label));
		writer.MakeScriptButton(writer.Intern(button.Key), PackingActionLowerer.Encode(writer, actions[name]));
	}

	static void EmitText(ScreenProgram.Writer writer, LiteralText text)
	{
		writer.PushString(writer.Intern(text.Value));
		writer.MakeText(writer.Intern(text.Key));
	}

	static bool RowBody(MethodDeclarationSyntax method, out List<Piece>? pieces, out string? reason)
	{
		pieces = null;
		reason = null;
		if (method.ParameterList.Parameters.Count != 3)
		{
			reason = "The row takes the item, the edit, and the packed change.";
			return false;
		}

		var item = method.ParameterList.Parameters[0].Identifier.ValueText;
		var onEdit = method.ParameterList.Parameters[1].Identifier.ValueText;
		var onPacked = method.ParameterList.Parameters[2].Identifier.ValueText;
		if (method.ExpressionBody?.Expression is not ExpressionSyntax expression)
		{
			reason = "The row has to be the column of its fields.";
			return false;
		}

		if (!TryColumn(expression, item, out _, out var elements, out reason) || elements is null)
			return false;
		var built = new List<Piece>();
		foreach (var element in elements)
		{
			if (!TryPiece(element, Place.Row, item, onEdit, onPacked, "", "", "", out var piece, out reason) || piece is null)
				return false;
			built.Add(piece);
		}

		pieces = built;
		return true;
	}

	static bool EditorBody(MethodDeclarationSyntax method, out Built? built, out string? reason)
	{
		built = null;
		reason = null;
		if (method.Body is null || method.Body.Statements.Count != 2)
		{
			reason = "The editor has to be the column of its fields.";
			return false;
		}

		if (method.Body.Statements[0] is not LocalDeclarationStatementSyntax local || local.Declaration.Variables.Count != 1)
		{
			reason = "The editor title has to follow the editing id.";
			return false;
		}

		var title = local.Declaration.Variables[0];
		if (title.Initializer?.Value is not ExpressionSyntax value || !IsEditingTitle(value, out var newer, out var older))
		{
			reason = "The editor title has to follow the editing id.";
			return false;
		}

		if (method.Body.Statements[1] is not ReturnStatementSyntax ret || ret.Expression is null)
		{
			reason = "The editor has to be the column of its fields.";
			return false;
		}

		if (!TryColumn(ret.Expression, null, out var key, out var elements, out reason) || elements is null || key is null)
			return false;
		var pieces = new List<Piece>();
		foreach (var element in elements)
		{
			if (!TryPiece(element, Place.Editor, "", "", "", title.Identifier.ValueText, newer, older, out var piece, out reason) || piece is null)
				return false;
			pieces.Add(piece);
		}

		built = new Built(key, pieces);
		return true;
	}

	static bool ListBody(MethodDeclarationSyntax method, List<Piece> row, out Built? built, out string? reason)
	{
		built = null;
		reason = null;
		if (method.Body is null || method.Body.Statements.Count < 2)
		{
			reason = "The packing list has to build its column.";
			return false;
		}

		if (method.Body.Statements[0] is not LocalDeclarationStatementSyntax local || local.Declaration.Variables.Count != 1)
		{
			reason = "The packing list has to collect its rows.";
			return false;
		}

		var variable = local.Declaration.Variables[0];
		if (variable.Initializer?.Value is not ExpressionSyntax creation || !IsListCreation(creation, out var header) || header is null)
		{
			reason = "The packing list has to collect its rows.";
			return false;
		}

		var name = variable.Identifier.ValueText;
		var pieces = new List<Piece>();
		foreach (var element in header)
		{
			if (!TryPiece(element, Place.List, "", "", "", "", "", "", out var piece, out reason) || piece is null)
				return false;
			pieces.Add(piece);
		}

		var statements = method.Body.Statements;
		for (var index = 1; index < statements.Count - 1; index++)
		{
			var statement = statements[index];
			if (statement is ForEachStatementSyntax)
			{
				if (!ValidLoop(statement, name, out var packed, out reason) || packed is null)
					return false;
				pieces.Add(new Rows(row, packed));
				continue;
			}

			if (!TryAdd(statement, name, out var added, out reason) || added is null)
				return false;
			pieces.Add(added);
		}

		if (!IsListReturn(statements[^1], name, out var key) || key is null)
		{
			reason = "The packing list has to return its column.";
			return false;
		}

		built = new Built(key, pieces);
		return true;
	}

	static bool TryPiece(
		ExpressionSyntax expression,
		Place place,
		string item,
		string onEdit,
		string onPacked,
		string titleLocal,
		string newer,
		string older,
		out Piece? piece,
		out string? reason)
	{
		piece = null;
		reason = null;
		if (expression is not ObjectCreationExpressionSyntax creation)
		{
			reason = "A packing element has to be constructed in place.";
			return false;
		}

		switch (TypeName(creation.Type))
		{
			case "Text":
				return TryText(creation, place, item, titleLocal, newer, older, out piece, out reason);
			case "Switch":
				return TrySwitch(creation, place, item, onPacked, out piece, out reason);
			case "Button":
				return TryButton(creation, place, item, onEdit, out piece, out reason);
			case "TextField":
				return TryField(creation, place, out piece, out reason);
			case "":
				reason = "A packing element has to be constructed in place.";
				return false;
			default:
				reason = "A packing program cannot carry " + TypeName(creation.Type) + ".";
				return false;
		}
	}

	static bool TryText(
		ObjectCreationExpressionSyntax creation,
		Place place,
		string item,
		string titleLocal,
		string newer,
		string older,
		out Piece? piece,
		out string? reason)
	{
		piece = null;
		if (!OneArg(creation, out var arg) || arg is null)
		{
			reason = "A text element needs one value.";
			return false;
		}

		var key = StringKey(creation);
		if (key is null)
		{
			reason = "A packing element needs a key.";
			return false;
		}

		if (arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
		{
			piece = new LiteralText(key, literal.Token.ValueText);
			reason = null;
			return true;
		}

		if (place == Place.Row && IsMember(arg, item, "Name"))
		{
			piece = new ItemName(key);
			reason = null;
			return true;
		}

		if (place == Place.Editor && arg is IdentifierNameSyntax name && name.Identifier.ValueText == titleLocal && titleLocal.Length > 0)
		{
			piece = new EditorTitle(key, newer, older);
			reason = null;
			return true;
		}

		if (place == Place.List && IsProgress(arg, out var mid, out var suffix))
		{
			piece = new Progress(key, mid, suffix);
			reason = null;
			return true;
		}

		reason = "A packing program cannot carry that text.";
		return false;
	}

	static bool TrySwitch(
		ObjectCreationExpressionSyntax creation,
		Place place,
		string item,
		string onPacked,
		out Piece? piece,
		out string? reason)
	{
		piece = null;
		if (creation.ArgumentList?.Arguments.Count != 3)
		{
			reason = "A switch needs its value, its change, and its label.";
			return false;
		}

		var key = StringKey(creation);
		if (key is null)
		{
			reason = "A packing element needs a key.";
			return false;
		}

		var value = creation.ArgumentList.Arguments[0].Expression;
		var change = creation.ArgumentList.Arguments[1].Expression;
		var label = creation.ArgumentList.Arguments[2].Expression;
		if (place == Place.Row && IsMember(value, item, "Packed") && change is IdentifierNameSyntax handler && handler.Identifier.ValueText == onPacked)
		{
			if (!LabelOf(label, item, "Name", out var prefix, out var withName))
			{
				reason = "The packed switch label has to name the item.";
				return false;
			}

			piece = new ItemPacked(key, prefix, withName);
			reason = null;
			return true;
		}

		if (place == Place.Editor
			&& IsMember(value, "state", "DraftPacked")
			&& change is LambdaExpressionSyntax lambda
			&& Assigns(lambda, "DraftPacked"))
		{
			if (label is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				reason = "The packed switch needs a label.";
				return false;
			}

			if (!PackingActionLowerer.TryLambda(lambda, out var script, out reason) || script is null)
				return false;
			piece = new DraftPacked(key, literal.Token.ValueText, script);
			reason = null;
			return true;
		}

		reason = "A packing program cannot carry that switch.";
		return false;
	}

	static bool TryButton(
		ObjectCreationExpressionSyntax creation,
		Place place,
		string item,
		string onEdit,
		out Piece? piece,
		out string? reason)
	{
		piece = null;
		if (creation.ArgumentList?.Arguments.Count != 3)
		{
			reason = "A button needs its title, its action, and its label.";
			return false;
		}

		var key = StringKey(creation);
		if (key is null)
		{
			reason = "A packing element needs a key.";
			return false;
		}

		var titleExpr = creation.ArgumentList.Arguments[0].Expression;
		var action = creation.ArgumentList.Arguments[1].Expression;
		var labelExpr = creation.ArgumentList.Arguments[2].Expression;
		if (titleExpr is not LiteralExpressionSyntax title || !title.IsKind(SyntaxKind.StringLiteralExpression))
		{
			reason = "A button title has to be a literal.";
			return false;
		}

		if (place == Place.Row)
		{
			if (action is LambdaExpressionSyntax remove && Calls(remove, "Remove"))
			{
				if (labelExpr is not LiteralExpressionSyntax removeLabel || !removeLabel.IsKind(SyntaxKind.StringLiteralExpression))
				{
					reason = "A button label has to be a literal.";
					return false;
				}

				piece = new ActionButton(key, title.Token.ValueText, removeLabel.Token.ValueText, RemoveCode);
				reason = null;
				return true;
			}

			if (action is not IdentifierNameSyntax handler || handler.Identifier.ValueText != onEdit)
			{
				reason = "The edit button has to use the row edit.";
				return false;
			}

			if (!LabelOf(labelExpr, item, "Name", out var prefix, out var withName))
			{
				reason = "The edit button label has to name the item.";
				return false;
			}

			piece = new ItemEdit(key, title.Token.ValueText, prefix, withName);
			reason = null;
			return true;
		}

		if (action is not LambdaExpressionSyntax lambda)
		{
			reason = "The button is not a packing action.";
			return false;
		}

		if (labelExpr is not LiteralExpressionSyntax label || !label.IsKind(SyntaxKind.StringLiteralExpression))
		{
			reason = "A button label has to be a literal.";
			return false;
		}

		byte code;
		if (place == Place.Editor && Calls(lambda, "Save"))
			code = ScreenProgram.PackSave;
		else if (place == Place.Editor && Calls(lambda, "Back"))
			code = ScreenProgram.PackBack;
		else if (place == Place.List && Calls(lambda, "BeginAdd"))
			code = ScreenProgram.PackAdd;
		else if (place == Place.List && Calls(lambda, "Copy"))
			code = ScreenProgram.PackCopy;
		else
		{
			reason = "The button is not a packing action.";
			return false;
		}

		piece = new ActionButton(key, title.Token.ValueText, label.Token.ValueText, code);
		reason = null;
		return true;
	}

	static bool TryField(ObjectCreationExpressionSyntax creation, Place place, out Piece? piece, out string? reason)
	{
		piece = null;
		if (place != Place.Editor || creation.ArgumentList?.Arguments.Count != 4)
		{
			reason = "A packing program cannot carry that text field.";
			return false;
		}

		var key = StringKey(creation);
		if (key is null)
		{
			reason = "A packing element needs a key.";
			return false;
		}

		var args = creation.ArgumentList.Arguments;
		if (args[1].Expression is not LambdaExpressionSyntax lambda
			|| args[2].Expression is not LiteralExpressionSyntax placeholder
			|| !placeholder.IsKind(SyntaxKind.StringLiteralExpression)
			|| args[3].Expression is not LiteralExpressionSyntax label
			|| !label.IsKind(SyntaxKind.StringLiteralExpression))
		{
			reason = "A packing program cannot carry that text field.";
			return false;
		}

		var note = IsMember(args[0].Expression, "state", "DraftNote");
		var name = IsMember(args[0].Expression, "state", "DraftName");
		if (name && !Assigns(lambda, "DraftName") || note && !Assigns(lambda, "DraftNote") || (!name && !note))
		{
			reason = "A packing program cannot carry that text field.";
			return false;
		}

		if (!PackingActionLowerer.TryLambda(lambda, out var script, out reason) || script is null)
			return false;
		piece = new DraftField(key, placeholder.Token.ValueText, label.Token.ValueText, script, note);
		reason = null;
		return true;
	}

	static bool TryColumn(
		ExpressionSyntax expression,
		string? item,
		out string? key,
		out List<ExpressionSyntax>? elements,
		out string? reason)
	{
		key = null;
		elements = null;
		reason = null;
		if (expression is not ObjectCreationExpressionSyntax creation || TypeName(creation.Type) != "Column")
		{
			reason = "The view has to be a column.";
			return false;
		}

		if (item is not null)
		{
			if (!IsItemKey(creation, item))
			{
				reason = "The row key has to be the item id.";
				return false;
			}
		}
		else
		{
			key = StringKey(creation);
			if (key is null)
			{
				reason = "A packing column needs a key.";
				return false;
			}
		}

		if (creation.ArgumentList?.Arguments.Count != 1
			|| creation.ArgumentList.Arguments[0].Expression is not CollectionExpressionSyntax collection)
		{
			reason = "The column has to list its children.";
			return false;
		}

		var found = new List<ExpressionSyntax>();
		foreach (var element in collection.Elements)
		{
			if (element is not ExpressionElementSyntax child)
			{
				reason = "The column has to list its children.";
				return false;
			}

			found.Add(child.Expression);
		}

		if (found.Count == 0 || found.Count > ScreenProgram.MaxChildren)
		{
			reason = "A packing column has too many children.";
			return false;
		}

		elements = found;
		return true;
	}

	static bool IsListCreation(ExpressionSyntax expression, out List<ExpressionSyntax>? elements)
	{
		elements = null;
		if (expression is not ObjectCreationExpressionSyntax creation)
			return false;
		if (creation.Type is not GenericNameSyntax generic || generic.Identifier.ValueText != "List")
			return false;
		if (creation.Initializer is null)
			return false;
		var found = new List<ExpressionSyntax>();
		foreach (var item in creation.Initializer.Expressions)
			found.Add(item);
		elements = found;
		return true;
	}

	static bool ValidLoop(StatementSyntax statement, string variable, out List<PackingActionLowerer.Act>? packed, out string? reason)
	{
		packed = null;
		reason = null;
		if (statement is not ForEachStatementSyntax loop || !IsMember(loop.Expression, "state", "Items"))
		{
			reason = "The packing list walks state.Items.";
			return false;
		}

		if (loop.Statement is not BlockSyntax block || block.Statements.Count != 2)
		{
			reason = "The item loop has to copy the item and add its row.";
			return false;
		}

		if (block.Statements[0] is not LocalDeclarationStatementSyntax local || local.Declaration.Variables.Count != 1)
		{
			reason = "The item loop has to copy the item and add its row.";
			return false;
		}

		var current = local.Declaration.Variables[0];
		if (current.Initializer?.Value is not IdentifierNameSyntax itemName || itemName.Identifier.ValueText != loop.Identifier.ValueText)
		{
			reason = "The item loop has to copy the item and add its row.";
			return false;
		}

		if (block.Statements[1] is not ExpressionStatementSyntax expr
			|| expr.Expression is not InvocationExpressionSyntax add
			|| !IsAdd(add, variable)
			|| add.ArgumentList.Arguments.Count != 1
			|| add.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax view
			|| !IsCall(view, "ItemRow", "View")
			|| view.ArgumentList.Arguments.Count != 3)
		{
			reason = "The item loop has to add the row.";
			return false;
		}

		var args = view.ArgumentList.Arguments;
		if (args[0].Expression is not IdentifierNameSyntax passed || passed.Identifier.ValueText != current.Identifier.ValueText)
		{
			reason = "The item loop has to add the row.";
			return false;
		}

		if (args[1].Expression is not LambdaExpressionSyntax edit || !Calls(edit, "BeginEdit"))
		{
			reason = "The item loop has to edit that item.";
			return false;
		}

		if (args[2].Expression is not LambdaExpressionSyntax packedLambda || !Assigns(packedLambda, "Packed"))
		{
			reason = "The item loop has to write the packed flag.";
			return false;
		}

		if (!PackingActionLowerer.TryLambda(packedLambda, out packed, out reason) || packed is null)
			return false;
		return true;
	}

	static bool TryAdd(StatementSyntax statement, string variable, out Piece? piece, out string? reason)
	{
		piece = null;
		if (statement is not ExpressionStatementSyntax expr
			|| expr.Expression is not InvocationExpressionSyntax add
			|| !IsAdd(add, variable)
			|| add.ArgumentList.Arguments.Count != 1)
		{
			reason = "The packing list has an extra statement.";
			return false;
		}

		return TryPiece(add.ArgumentList.Arguments[0].Expression, Place.List, "", "", "", "", "", "", out piece, out reason);
	}

	static bool IsListReturn(StatementSyntax statement, string variable, out string? key)
	{
		key = null;
		if (statement is not ReturnStatementSyntax ret || ret.Expression is not ObjectCreationExpressionSyntax creation)
			return false;
		if (TypeName(creation.Type) != "Column")
			return false;
		if (creation.ArgumentList?.Arguments.Count != 1)
			return false;
		if (creation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax name || name.Identifier.ValueText != variable)
			return false;
		key = StringKey(creation);
		return key is not null;
	}

	static bool IsEditingTitle(ExpressionSyntax expression, out string newer, out string older)
	{
		newer = "";
		older = "";
		if (expression is not ConditionalExpressionSyntax conditional)
			return false;
		if (conditional.Condition is not IsPatternExpressionSyntax pattern)
			return false;
		if (!IsMember(pattern.Expression, "state", "EditingId"))
			return false;
		if (pattern.Pattern is not ConstantPatternSyntax constant || !constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
			return false;
		if (conditional.WhenTrue is not LiteralExpressionSyntax whenNew || !whenNew.IsKind(SyntaxKind.StringLiteralExpression))
			return false;
		if (conditional.WhenFalse is not LiteralExpressionSyntax whenOld || !whenOld.IsKind(SyntaxKind.StringLiteralExpression))
			return false;
		newer = whenNew.Token.ValueText;
		older = whenOld.Token.ValueText;
		return true;
	}

	static bool IsProgress(ExpressionSyntax expression, out string mid, out string suffix)
	{
		mid = "";
		suffix = "";
		if (expression is not BinaryExpressionSyntax outer || !outer.IsKind(SyntaxKind.AddExpression))
			return false;
		if (outer.Right is not LiteralExpressionSyntax suffixLiteral || !suffixLiteral.IsKind(SyntaxKind.StringLiteralExpression))
			return false;
		if (outer.Left is not BinaryExpressionSyntax countAdd || !countAdd.IsKind(SyntaxKind.AddExpression))
			return false;
		if (!IsMember(countAdd.Right, "state", "Items", "Count"))
			return false;
		if (countAdd.Left is not BinaryExpressionSyntax midAdd || !midAdd.IsKind(SyntaxKind.AddExpression))
			return false;
		if (!IsMember(midAdd.Left, "state", "PackedCount"))
			return false;
		if (midAdd.Right is not LiteralExpressionSyntax midLiteral || !midLiteral.IsKind(SyntaxKind.StringLiteralExpression))
			return false;
		mid = midLiteral.Token.ValueText;
		suffix = suffixLiteral.Token.ValueText;
		return true;
	}

	static bool LabelOf(ExpressionSyntax label, string item, string member, out string prefix, out bool withName)
	{
		prefix = "";
		withName = false;
		if (label is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
		{
			prefix = literal.Token.ValueText;
			return true;
		}

		if (label is BinaryExpressionSyntax binary
			&& binary.IsKind(SyntaxKind.AddExpression)
			&& binary.Left is LiteralExpressionSyntax left
			&& left.IsKind(SyntaxKind.StringLiteralExpression)
			&& IsMember(binary.Right, item, member))
		{
			prefix = left.Token.ValueText;
			withName = true;
			return true;
		}

		return false;
	}

	static bool Routes(ClassDeclarationSyntax screen, out string? reason)
	{
		reason = null;
		if (Unexpected(screen, "Render", "Unknown") is string extra)
		{
			reason = extra == ""
				? "The packing screen has to show the list and the editor."
				: "A packing program cannot carry " + extra + ".";
			return false;
		}

		var render = Method(screen, "Render");
		if (render?.Body is null || render.Body.Statements.Count != 1 || render.Body.Statements[0] is not SwitchStatementSyntax statement)
		{
			reason = "The packing screen has to show the list and the editor.";
			return false;
		}

		if (!IsMember(statement.Expression, "state", "Route"))
		{
			reason = "The packing screen has to show the list and the editor.";
			return false;
		}

		var list = false;
		var editor = false;
		foreach (var section in statement.Sections)
		{
			var callsList = CallsNamed(section, "PackingList", "View");
			var callsEditor = CallsNamed(section, "ItemEditor", "View");
			foreach (var label in section.Labels)
			{
				if (label is not CaseSwitchLabelSyntax caseLabel)
					continue;
				if (IsMember(caseLabel.Value, "PackingRoute", "List"))
					list = callsList && !callsEditor;
				else if (IsMember(caseLabel.Value, "PackingRoute", "Editor"))
					editor = callsEditor && !callsList;
			}
		}

		if (!list || !editor)
		{
			reason = "The packing screen has to show the list and the editor.";
			return false;
		}

		return true;
	}

	static bool HostShape(InterfaceDeclarationSyntax host)
	{
		if (host.Members.Count != 2)
			return false;
		var method = false;
		var property = false;
		foreach (var member in host.Members)
		{
			switch (member)
			{
				case MethodDeclarationSyntax methodDecl when methodDecl.Identifier.ValueText == "RequestRender":
					method = true;
					break;
				case PropertyDeclarationSyntax propertyDecl when propertyDecl.Identifier.ValueText == "Native":
					property = true;
					break;
				default:
					return false;
			}
		}

		return method && property;
	}

	static string? Unexpected(ClassDeclarationSyntax type, params string[] allowed)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var member in type.Members)
		{
			var name = MemberName(member);
			if (!seen.Add(name))
				return name;
			var known = false;
			foreach (var allow in allowed)
			{
				if (allow == name)
				{
					known = true;
					break;
				}
			}

			if (!known)
				return name;
		}

		foreach (var allow in allowed)
		{
			if (!seen.Contains(allow))
				return "";
		}

		return null;
	}

	static MethodDeclarationSyntax? Method(ClassDeclarationSyntax type, string name)
	{
		foreach (var member in type.Members)
		{
			if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == name)
				return method;
		}

		return null;
	}

	static string MemberName(MemberDeclarationSyntax member) =>
		member switch
		{
			MethodDeclarationSyntax method => method.Identifier.ValueText,
			PropertyDeclarationSyntax property => property.Identifier.ValueText,
			FieldDeclarationSyntax field when field.Declaration.Variables.Count == 1 => field.Declaration.Variables[0].Identifier.ValueText,
			ClassDeclarationSyntax nested => nested.Identifier.ValueText,
			_ => member.Kind().ToString(),
		};

	static string? RequireRow(List<Piece> pieces)
	{
		if (Count<ItemName>(pieces) != 1)
			return "The row needs its name.";
		if (Count<ItemPacked>(pieces) != 1)
			return "The row needs its packed switch.";
		if (Count<ItemEdit>(pieces) != 1)
			return "The row needs its edit button.";
		foreach (var piece in pieces)
		{
			switch (piece)
			{
				case ItemName:
				case ItemPacked:
				case ItemEdit:
				case LiteralText:
					break;
				case ActionButton button when button.Code == RemoveCode:
					break;
				default:
					return "The row has an element this program cannot carry.";
			}
		}

		return Duplicate(pieces);
	}

	static string? RequireEditor(List<Piece> pieces)
	{
		if (Count<EditorTitle>(pieces) != 1)
			return "The editor title has to follow the editing id.";
		var names = 0;
		var notes = 0;
		foreach (var piece in pieces)
		{
			if (piece is DraftField field && field.Note)
				notes++;
			else if (piece is DraftField)
				names++;
		}

		if (names != 1)
			return names == 0
				? "The editor needs its name field."
				: "The editor already has a name field.";
		if (notes > 1)
			return "The editor already has a note field.";
		if (Count<DraftPacked>(pieces) != 1)
			return "The editor needs its packed switch.";
		if (CountAction(pieces, ScreenProgram.PackSave) != 1)
			return "The editor needs Save.";
		if (CountAction(pieces, ScreenProgram.PackBack) != 1)
			return "The editor needs Back.";
		foreach (var piece in pieces)
		{
			switch (piece)
			{
				case EditorTitle:
				case DraftField:
				case DraftPacked:
				case LiteralText:
					break;
				case ActionButton button when button.Code is ScreenProgram.PackSave or ScreenProgram.PackBack:
					break;
				default:
					return "The editor has an element this program cannot carry.";
			}
		}

		return Duplicate(pieces);
	}

	static string? RequireList(List<Piece> pieces)
	{
		if (Count<Progress>(pieces) != 1)
			return "The progress line is not the packed count.";
		if (Count<Rows>(pieces) != 1)
			return "The packing list has to walk its items.";
		if (CountAction(pieces, ScreenProgram.PackAdd) != 1)
			return "The packing list needs Add item.";
		if (CountAction(pieces, ScreenProgram.PackCopy) != 1)
			return "The packing list needs Copy list.";
		foreach (var piece in pieces)
		{
			switch (piece)
			{
				case LiteralText:
				case Progress:
				case Rows:
					break;
				case ActionButton button when button.Code is ScreenProgram.PackAdd or ScreenProgram.PackCopy:
					break;
				default:
					return "The packing list has an element this program cannot carry.";
			}
		}

		return Duplicate(pieces);
	}

	static string? Duplicate(List<Piece> pieces)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var piece in pieces)
		{
			if (piece is Rows)
				continue;
			if (!seen.Add(piece.Key))
				return "The column repeats the key " + piece.Key + ".";
		}

		return null;
	}

	static int Count<T>(List<Piece> pieces) where T : Piece
	{
		var count = 0;
		foreach (var piece in pieces)
		{
			if (piece is T)
				count++;
		}

		return count;
	}

	static int CountAction(List<Piece> pieces, byte code)
	{
		var count = 0;
		foreach (var piece in pieces)
		{
			if (piece is ActionButton button && button.Code == code)
				count++;
		}

		return count;
	}

	static bool OneArg(ObjectCreationExpressionSyntax creation, out ExpressionSyntax? argument)
	{
		argument = null;
		if (creation.ArgumentList?.Arguments.Count != 1)
			return false;
		argument = creation.ArgumentList.Arguments[0].Expression;
		return true;
	}

	static string? StringKey(ObjectCreationExpressionSyntax creation)
	{
		if (creation.Initializer is null || creation.Initializer.Expressions.Count != 1)
			return null;
		if (creation.Initializer.Expressions[0] is not AssignmentExpressionSyntax assign)
			return null;
		if (assign.Left is not IdentifierNameSyntax name || name.Identifier.ValueText != "Key")
			return null;
		if (assign.Right is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
			return null;
		if (literal.Token.ValueText.Length == 0)
			return null;
		return literal.Token.ValueText;
	}

	static bool IsItemKey(ObjectCreationExpressionSyntax creation, string item)
	{
		if (creation.Initializer is null || creation.Initializer.Expressions.Count != 1)
			return false;
		return creation.Initializer.Expressions[0] is AssignmentExpressionSyntax assign
			&& assign.Left is IdentifierNameSyntax name
			&& name.Identifier.ValueText == "Key"
			&& IsMember(assign.Right, item, "Id");
	}

	static bool IsAdd(InvocationExpressionSyntax call, string variable) =>
		call.Expression is MemberAccessExpressionSyntax access
		&& access.Name.Identifier.ValueText == "Add"
		&& access.Expression is IdentifierNameSyntax identifier
		&& identifier.Identifier.ValueText == variable;

	static bool IsCall(InvocationExpressionSyntax call, string type, string method) =>
		call.Expression is MemberAccessExpressionSyntax access
		&& access.Name.Identifier.ValueText == method
		&& access.Expression is IdentifierNameSyntax identifier
		&& identifier.Identifier.ValueText == type;

	static bool CallsNamed(SyntaxNode node, string type, string method)
	{
		foreach (var call in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (IsCall(call, type, method))
				return true;
		}

		return false;
	}

	static bool Calls(SyntaxNode node, string method)
	{
		foreach (var call in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			var name = call.Expression switch
			{
				MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				_ => "",
			};
			if (name == method)
				return true;
		}

		return false;
	}

	static bool Assigns(SyntaxNode node, string property)
	{
		foreach (var assignment in node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
		{
			if (assignment.Left is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == property)
				return true;
		}

		return false;
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

	static string TypeName(TypeSyntax type) =>
		type switch
		{
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			_ => "",
		};
}
