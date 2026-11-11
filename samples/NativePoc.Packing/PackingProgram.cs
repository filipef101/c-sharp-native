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
		writer.Return();
		return writer.Finish(null);
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
}
