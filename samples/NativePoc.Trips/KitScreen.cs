namespace NativePoc.Trips;

public sealed record KitRow
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public bool Packed { get; set; }
}

public sealed class KitState
{
	public bool Editing { get; set; }

	public string EditingId { get; set; } = "";

	public string DraftName { get; set; } = "";

	public bool DraftPacked { get; set; }

	public string Held { get; set; } = "";

	public string Fresh { get; set; } = "";

	public int NextNumber { get; set; } = 1;

	public List<KitRow> Items { get; set; } = new List<KitRow>()
	{
		new KitRow { Id = "tent", Name = "Tent fly", Packed = false },
		new KitRow { Id = "stove", Name = "Stove", Packed = true },
	};

	public void Commit()
	{
	}

	public void Drop(KitRow row)
	{
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (Items[index].Id == row.Id)
				Items.RemoveAt(index);
		}
	}
}

public static class KitScreen
{
	public const string ScreenId = "kit";

	public static Element Render(KitState state, IPlatform platform)
	{
		if (state.Editing)
		{
			return new Column(
			[
				new Text(L.Get("Add to kit")) { Key = "title" },
				new TextField(
					state.DraftName,
					value =>
					{
						state.DraftName = value;
						platform.RequestRender();
					},
					L.Get("Item name"),
					L.Get("Item name"))
				{ Key = "name" },
				new Button(L.Get("Save"), () => { state.Commit(); platform.RequestRender(); }, L.Get("Save item")) { Key = "save" },
				new Button(L.Get("Back"), () => { state.Editing = false; platform.RequestRender(); }, L.Get("Back")) { Key = "back" },
			])
			{ Key = "editor" };
		}

		var rows = new List<Element>();
		rows.Add(new Text(L.Get("Packing")) { Key = "title", Style = new Style { FontSize = 22, Weight = FontWeight.Bold } });
		rows.Add(new Text(state.Fresh.Length == 0 ? L.Get("Pull to refresh") : L.Get(state.Fresh)) { Key = "fresh" });
		rows.Add(new Text(state.Held.Length == 0 ? L.Get("Hold a row") : L.Get(state.Held)) { Key = "held" });
		foreach (var item in state.Items)
		{
			rows.Add(new Column(
			[
				new Text(L.Get(item.Name)) { Key = "name", OnLongPress = () => { state.Held = item.Name; platform.RequestRender(); } },
				new Text(item.Packed ? L.Get("Packed") : L.Get("Open")) { Key = "packed" },
			])
			{
				Key = item.Id,
				OnSwipe = () => { state.Drop(item); platform.RequestRender(); },
			});
		}

		rows.Add(new Button(
			L.Get("Add item"),
			() =>
			{
				state.EditingId = "";
				state.DraftName = "";
				state.DraftPacked = false;
				state.Editing = true;
				platform.RequestRender();
			},
			L.Get("Add item"))
		{ Key = "add" });
		rows.Add(new Button(L.Get("Back"), () => { platform.Pop(); platform.RequestRender(); }, L.Get("Back")) { Key = "back" });
		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } })
		{
			Key = "scroll",
			OnRefresh = () => platform.Effect("Fresh", "refresh"),
		};
	}
}
