namespace NativePoc.Packing;

public interface IPackingHost
{
	void RequestRender();

	INativeServices Native { get; }
}

public static class ItemRow
{
	public static Element View(PackingItem item, Action onEdit, Action<bool> onPacked) =>
		new Column(
		[
			new Text(item.Name) { Key = "name" },
			new Switch(item.Packed, onPacked, "Packed " + item.Name) { Key = "packed" },
			new Button("Edit", onEdit, "Edit " + item.Name) { Key = "edit" },
		])
		{ Key = item.Id };
}

public static class ItemEditor
{
	public static Element View(PackingState state, IPackingHost host)
	{
		var title = state.EditingId is null ? "New item" : "Edit item";
		return new Column(
		[
			new Text(title) { Key = "title" },
			new TextField(
				state.DraftName,
				value =>
				{
					PackingTelemetry.FieldDeliveries++;
					state.DraftName = value;
					host.RequestRender();
				},
				"Item name",
				"Item name")
			{ Key = "name" },
			new Switch(
				state.DraftPacked,
				value =>
				{
					PackingTelemetry.SwitchDeliveries++;
					state.DraftPacked = value;
					host.Native.Pulse();
					host.RequestRender();
				},
				"Packed")
			{ Key = "packed" },
			new Button("Save", () => PackingActions.Save(state, host), "Save item") { Key = "save" },
			new Button("Back", () => PackingActions.Back(state, host), "Back") { Key = "back" },
		])
		{ Key = "editor" };
	}
}

public static class PackingList
{
	public static Element View(PackingState state, IPackingHost host)
	{
		var children = new List<Element>
		{
			new Text("Packing") { Key = "title" },
			new Text(state.PackedCount + " of " + state.Items.Count + " packed") { Key = "progress" },
		};
		foreach (var item in state.Items)
		{
			var current = item;
			children.Add(ItemRow.View(
				current,
				() => PackingActions.BeginEdit(state, host, current.Id),
				value =>
				{
					PackingTelemetry.SwitchDeliveries++;
					current.Packed = value;
					host.Native.Pulse();
					host.RequestRender();
				}));
		}
		children.Add(new Button("Add item", () => PackingActions.BeginAdd(state, host), "Add item") { Key = "add" });
		children.Add(new Button("Copy list", () => PackingActions.Copy(state, host), "Copy list") { Key = "copy" });
		return new ScrollView(new Column(children) { Key = "list" }) { Key = "scroll" };
	}
}

public static class PackingActions
{
	public static void BeginAdd(PackingState state, IPackingHost host)
	{
		state.EditingId = null;
		state.DraftName = "";
		state.DraftPacked = false;
		state.Route = PackingRoute.Editor;
		host.RequestRender();
	}

	public static void BeginEdit(PackingState state, IPackingHost host, string id)
	{
		var item = Find(state, id);
		state.EditingId = item.Id;
		state.DraftName = item.Name;
		state.DraftPacked = item.Packed;
		state.Route = PackingRoute.Editor;
		host.RequestRender();
	}

	public static void Save(PackingState state, IPackingHost host)
	{
		var name = state.DraftName.Trim();
		if (name.Length == 0)
			return;
		if (state.EditingId is null)
		{
			state.Items.Add(new PackingItem("item-" + state.NextNumber, name, state.DraftPacked));
			state.NextNumber++;
		}
		else
		{
			var item = Find(state, state.EditingId);
			item.Name = name;
			item.Packed = state.DraftPacked;
		}
		state.Route = PackingRoute.List;
		host.RequestRender();
	}

	public static void Back(PackingState state, IPackingHost host)
	{
		state.Route = PackingRoute.List;
		host.RequestRender();
	}

	public static void Copy(PackingState state, IPackingHost host)
	{
		host.Native.CopyText("packing", state.CopyText());
		host.Native.Pulse();
	}

	static PackingItem Find(PackingState state, string id)
	{
		foreach (var item in state.Items)
		{
			if (item.Id == id)
				return item;
		}
		throw new InvalidOperationException("Packing item was not found.");
	}
}

public static class PackingScreen
{
	public static Element Render(PackingState state, IPackingHost host)
	{
		switch (state.Route)
		{
			case PackingRoute.List:
				return PackingList.View(state, host);
			case PackingRoute.Editor:
				return ItemEditor.View(state, host);
			default:
				return Unknown(state.Route);
		}
	}

	static Element Unknown(PackingRoute route) =>
		route switch
		{
			PackingRoute.List => throw new InvalidOperationException("List is handled."),
			PackingRoute.Editor => throw new InvalidOperationException("Editor is handled."),
			_ => throw new InvalidOperationException("Unknown route."),
		};
}
