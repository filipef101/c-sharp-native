namespace NativePoc.Packing;

public static class PackingProgram
{
	public static byte[] Bytes { get; } = Build();

	static byte[] Build()
	{
		var writer = new ScreenProgram.Writer();
		var title = writer.Intern("title");
		var name = writer.Intern("name");
		var packed = writer.Intern("packed");
		var edit = writer.Intern("edit");
		var save = writer.Intern("save");
		var back = writer.Intern("back");
		var add = writer.Intern("add");
		var copy = writer.Intern("copy");
		var list = writer.Intern("list");
		var scroll = writer.Intern("scroll");
		var editor = writer.Intern("editor");
		var progress = writer.Intern("progress");
		var of = writer.Intern(" of ");
		var packedSuffix = writer.Intern(" packed");
		var newItem = writer.Intern("New item");
		var editItem = writer.Intern("Edit item");
		var itemName = writer.Intern("Item name");
		var packedWord = writer.Intern("Packed");
		var packedSpace = writer.Intern("Packed ");
		var editWord = writer.Intern("Edit");
		var editSpace = writer.Intern("Edit ");
		var saveWord = writer.Intern("Save");
		var saveItem = writer.Intern("Save item");
		var backWord = writer.Intern("Back");
		var packing = writer.Intern("Packing");
		var addItem = writer.Intern("Add item");
		var copyList = writer.Intern("Copy list");

		writer.PushEditor();
		var listAt = writer.JumpIfFalse();
		Title(writer, title, newItem, editItem);
		writer.PushDraftName();
		writer.PushString(itemName);
		writer.PushString(itemName);
		writer.MakeDraftField(name);
		writer.PushDraftPacked();
		writer.PushString(packedWord);
		writer.MakeSwitch(packed, ScreenProgram.PackDraftPacked);
		writer.PushString(saveWord);
		writer.PushString(saveItem);
		writer.MakeActionButton(save, ScreenProgram.PackSave);
		writer.PushString(backWord);
		writer.PushString(backWord);
		writer.MakeActionButton(back, ScreenProgram.PackBack);
		writer.MakeKeyedColumn(5, editor);
		writer.Return();
		writer.Patch(listAt, writer.Here);

		writer.Mark();
		writer.PushString(packing);
		writer.MakeText(title);
		writer.PushPackedCount();
		writer.PushString(of);
		writer.Concat();
		writer.PushItemCount();
		writer.Concat();
		writer.PushString(packedSuffix);
		writer.Concat();
		writer.MakeText(progress);
		var items = writer.BeginItems();
		writer.PushItemName();
		writer.MakeText(name);
		writer.PushItemPacked();
		writer.PushString(packedSpace);
		writer.PushItemName();
		writer.Concat();
		writer.MakeSwitch(packed, ScreenProgram.PackItemPacked);
		writer.PushString(editWord);
		writer.PushString(editSpace);
		writer.PushItemName();
		writer.Concat();
		writer.MakeActionButton(edit, ScreenProgram.PackEdit);
		writer.MakeItemColumn(3);
		writer.EndItems(items);
		writer.PushString(addItem);
		writer.PushString(addItem);
		writer.MakeActionButton(add, ScreenProgram.PackAdd);
		writer.PushString(copyList);
		writer.PushString(copyList);
		writer.MakeActionButton(copy, ScreenProgram.PackCopy);
		writer.MakeDynamicColumn(list);
		writer.MakeScroll(scroll);
		writer.Return();
		return writer.Finish(null, 1);
	}

	static void Title(ScreenProgram.Writer writer, int key, int newItem, int editItem)
	{
		writer.PushEditingId();
		writer.IsEmpty();
		var editTitle = writer.JumpIfFalse();
		writer.PushString(newItem);
		var join = writer.Jump();
		writer.Patch(editTitle, writer.Here);
		writer.PushString(editItem);
		writer.Patch(join, writer.Here);
		writer.MakeText(key);
	}
}

public sealed class PackingProgramAdapter : IPackingProgram
{
	readonly PackingState _state;
	readonly IPackingHost _host;

	public PackingProgramAdapter(PackingState state, IPackingHost host)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(host);
		_state = state;
		_host = host;
	}

	public int ItemCount => _state.Items.Count;

	public int PackedCount => _state.PackedCount;

	public bool Editor => _state.Route == PackingRoute.Editor;

	public string? EditingId => _state.EditingId;

	public string DraftName => _state.DraftName;

	public string DraftNote => _state.DraftNote;

	public bool DraftPacked => _state.DraftPacked;

	public string ItemId(int index) => _state.Items[index].Id;

	public string ItemName(int index) => _state.Items[index].Name;

	public bool ItemPacked(int index) => _state.Items[index].Packed;

	public void SetItemPacked(int index, bool packed)
	{
		PackingTelemetry.SwitchDeliveries++;
		_state.Items[index].Packed = packed;
		_host.Native.Pulse();
		_host.RequestRender();
	}

	public void BeginAdd() => PackingActions.BeginAdd(_state, _host);

	public void BeginEdit(int index) => PackingActions.BeginEdit(_state, _host, _state.Items[index].Id);

	public void SetDraftName(string name)
	{
		PackingTelemetry.FieldDeliveries++;
		_state.DraftName = name;
		_host.RequestRender();
	}

	public void SetDraftPacked(bool packed)
	{
		PackingTelemetry.SwitchDeliveries++;
		_state.DraftPacked = packed;
		_host.Native.Pulse();
		_host.RequestRender();
	}

	public void Save() => PackingActions.Save(_state, _host);

	public void Back() => PackingActions.Back(_state, _host);

	public void Copy() => PackingActions.Copy(_state, _host);

	string? _trimmed;

	public bool Apply(byte op, int itemIndex, bool argument, string? text)
	{
		switch (op)
		{
			case ScreenProgram.ActRender:
				_host.RequestRender();
				return true;
			case ScreenProgram.ActRoute:
				_state.Route = argument ? PackingRoute.Editor : PackingRoute.List;
				return true;
			case ScreenProgram.ActEditingClear:
				_state.EditingId = null;
				return true;
			case ScreenProgram.ActEditingItem:
				_state.EditingId = _state.Items[itemIndex].Id;
				return true;
			case ScreenProgram.ActDraftClear:
				_state.DraftName = "";
				return true;
			case ScreenProgram.ActDraftFromItem:
				_state.DraftName = _state.Items[itemIndex].Name;
				return true;
			case ScreenProgram.ActDraftPacked:
				_state.DraftPacked = argument;
				return true;
			case ScreenProgram.ActDraftPackedItem:
				_state.DraftPacked = _state.Items[itemIndex].Packed;
				return true;
			case ScreenProgram.ActDraftPackedArg:
				_state.DraftPacked = argument;
				return true;
			case ScreenProgram.ActItemPackedArg:
				_state.Items[itemIndex].Packed = argument;
				return true;
			case ScreenProgram.ActItemPackedDraft:
			{
				var editing = _state.EditingId ?? throw new InvalidOperationException("Packing item was not found.");
				Find(editing).Packed = _state.DraftPacked;
				return true;
			}
			case ScreenProgram.ActRename:
			{
				var id = _state.EditingId ?? throw new InvalidOperationException("Packing item was not found.");
				var item = Find(id);
				item.Name = _trimmed ?? _state.DraftName;
				return true;
			}
			case ScreenProgram.ActTrimGate:
				_trimmed = _state.DraftName.Trim();
				return _trimmed.Length > 0;
			case ScreenProgram.ActAdd:
				_state.Items.Add(new PackingItem((text ?? "") + _state.NextNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), _trimmed ?? _state.DraftName, _state.DraftPacked));
				_state.NextNumber++;
				_trimmed = null;
				return true;
			case ScreenProgram.ActRemove:
				_state.Items.RemoveAt(itemIndex);
				return true;
			case ScreenProgram.ActCopy:
				_host.Native.CopyText(text ?? "", _state.CopyText());
				return true;
			case ScreenProgram.ActPulse:
				_host.Native.Pulse();
				return true;
			case ScreenProgram.ActCountSwitch:
				PackingTelemetry.SwitchDeliveries++;
				return true;
			case ScreenProgram.ActCountField:
				PackingTelemetry.FieldDeliveries++;
				return true;
			case ScreenProgram.ActDraftFromArg:
				_state.DraftName = text ?? "";
				return true;
			case ScreenProgram.ActDraftNoteArg:
				_state.DraftNote = text ?? "";
				return true;
			default:
				throw new InvalidOperationException("Rejected packing action.");
		}
	}

	PackingItem Find(string id)
	{
		foreach (var item in _state.Items)
		{
			if (item.Id == id)
				return item;
		}

		throw new InvalidOperationException("Packing item was not found.");
	}
}
