using System.Globalization;

namespace NativePoc.Packing;

public sealed record PackingRow
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public bool Packed { get; set; }

	public string Note { get; set; } = "";

	public string Picture { get; set; } = "";
}

public sealed class PackingModel
{
	public bool Editing { get; set; }

	public string EditingId { get; set; } = "";

	public string DraftName { get; set; } = "";

	public string DraftNote { get; set; } = "";

	public bool DraftPacked { get; set; }

	public string Held { get; set; } = "";

	public string Fresh { get; set; } = "";

	public int NextNumber { get; set; } = 1;

	public List<PackingRow> Items { get; } =
	[
		new() { Id = "tent", Name = "Canvas tent", Packed = false, Note = "Ridge line" },
		new() { Id = "stove", Name = "Camp stove", Packed = true, Picture = "marker" },
		new() { Id = "map", Name = "Ridge map", Packed = false, Note = "Folded", Picture = "marker2" },
		new() { Id = "bladder", Name = "Water bladder", Packed = false },
	];

	public int PackedCount
	{
		get
		{
			var count = 0;
			foreach (var item in Items)
			{
				if (item.Packed)
					count++;
			}

			return count;
		}
	}

	public string ListText
	{
		get
		{
			var lines = new string[Items.Count];
			for (var index = 0; index < Items.Count; index++)
			{
				var item = Items[index];
				lines[index] = item.Packed ? item.Name + " [packed]" : item.Name;
			}

			return string.Join('\n', lines);
		}
	}

	public void Commit()
	{
		var name = DraftName.Trim();
		if (name.Length == 0)
			return;
		if (EditingId.Length == 0)
		{
			Items.Add(new PackingRow
			{
				Id = "item-" + NextNumber.ToString(CultureInfo.InvariantCulture),
				Name = name,
				Packed = DraftPacked,
			});
			NextNumber++;
		}
		else
		{
			foreach (var item in Items)
			{
				if (item.Id == EditingId)
				{
					item.Name = name;
					item.Packed = DraftPacked;
				}
			}
		}

		Editing = false;
	}

	public void Drop(PackingRow row)
	{
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (Items[index].Id == row.Id)
				Items.RemoveAt(index);
		}
	}

	public static PackingModel From(PackingState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		var model = new PackingModel();
		model.Items.Clear();
		foreach (var item in state.Items)
			model.Items.Add(new PackingRow { Id = item.Id, Name = item.Name, Packed = item.Packed });
		model.Editing = state.Route == PackingRoute.Editor;
		model.EditingId = state.EditingId ?? "";
		model.DraftName = state.DraftName;
		model.DraftNote = state.DraftNote;
		model.DraftPacked = state.DraftPacked;
		model.NextNumber = state.NextNumber;
		return model;
	}

	public void CopyTo(PackingState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		state.Items.Clear();
		foreach (var item in Items)
			state.Items.Add(new PackingItem(item.Id, item.Name, item.Packed));
		state.Route = Editing ? PackingRoute.Editor : PackingRoute.List;
		state.EditingId = EditingId.Length == 0 ? null : EditingId;
		state.DraftName = DraftName;
		state.DraftNote = DraftNote;
		state.DraftPacked = DraftPacked;
		state.NextNumber = NextNumber;
	}
}

public static class PackingScreen
{
	public const string ScreenId = "packing";

	public static Element Render(PackingModel state, IPlatform host)
	{
		if (state.Editing)
			return new Column(
			[
				new Text(state.EditingId.Length == 0 ? "New item" : "Edit item") { Key = "title" },
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
						host.Pulse();
						host.RequestRender();
					},
					"Packed")
				{ Key = "packed" },
				new Button("Save", () => { state.Commit(); host.RequestRender(); }, "Save item") { Key = "save" },
				new Button("Back", () => { state.Editing = false; host.RequestRender(); }, "Back") { Key = "back" },
			])
			{ Key = "editor" };
		var rows = new List<Element>();
		rows.Add(new Text("Packing") { Key = "title", Style = new Style { FontSize = 22, Weight = FontWeight.Bold, Color = 0xFF1B3A2F } });
		rows.Add(new Text(state.Fresh.Length == 0 ? "Pull to refresh" : state.Fresh) { Key = "fresh" });
		rows.Add(new Text(state.Held.Length == 0 ? "Hold a name" : state.Held) { Key = "held" });
		rows.Add(new Text($"{state.PackedCount} of {state.Items.Count} packed") { Key = "progress", Style = new Style { FontSize = 14, Color = 0xFF5C6B63 } });
		foreach (var item in state.Items)
			rows.Add(new Column(
			[
				new Text(item.Name) { Key = "name", OnLongPress = () => { state.Held = item.Name; host.RequestRender(); } },
				new Text(item.Note) { Key = "note" },
				new Switch(item.Packed, value => { PackingTelemetry.SwitchDeliveries++; item.Packed = value; host.Pulse(); host.RequestRender(); }, "Packed " + item.Name) { Key = "packed" },
				new Button("Edit", () => { state.EditingId = item.Id; state.DraftName = item.Name; state.DraftPacked = item.Packed; state.Editing = true; host.RequestRender(); }, "Edit " + item.Name) { Key = "edit" },
				new Image(item.Picture) { Key = "picture", Description = item.Name },
			])
			{
				Key = item.Id,
				Style = new Style { Padding = 12, Background = 0xFFF4F1EA, Radius = 10 },
				OnSwipe = () => { state.Drop(item); host.RequestRender(); },
			});
		rows.Add(new Button("Add item", () => { state.EditingId = ""; state.DraftName = ""; state.DraftPacked = false; state.Editing = true; host.RequestRender(); }, "Add item") { Key = "add" });
		rows.Add(new Button("Copy list", () => { host.CopyText("packing", state.ListText); host.Pulse(); }, "Copy list") { Key = "copy" });
		rows.Add(new Button("Settings", () => { host.Push("settings"); host.RequestRender(); }, "Settings") { Key = "settings" });
		rows.Add(new Button("Back", () => { host.Pop(); host.RequestRender(); }, "Back") { Key = "back" });
		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } })
		{
			Key = "scroll",
			OnRefresh = () => host.Effect("Fresh", "refresh"),
		};
	}
}
