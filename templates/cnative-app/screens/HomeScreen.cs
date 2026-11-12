namespace NativePoc;

public sealed class HomeItem
{
	public string Name { get; set; } = "";
}

public sealed class HomeState
{
	public int Count { get; set; }

	public string Title { get; set; } = "Hello";

	public string Draft { get; set; } = "";

	public List<HomeItem> Items { get; set; } = new List<HomeItem>();
}

public static class HomeScreen
{
	public const string ScreenId = "home";

	public static Element Render(HomeState state, IPlatform platform)
	{
		var rows = new List<Element>();
		rows.Add(new Text(state.Title) { Key = "title", Style = new Style { FontSize = 22 } });
		rows.Add(new Text("Count " + state.Count) { Key = "count" });
		rows.Add(new TextField(state.Draft, value => { state.Draft = value; platform.RequestRender(); }, "Item", "Item") { Key = "draft", Autofill = false });
		foreach (var item in state.Items)
			rows.Add(new Text(item.Name) { Key = item.Name });
		rows.Add(new Button("Open", () => { platform.Push("home"); }, "Open") { Key = "open" });
		rows.Add(new Button("Add", () => { state.Count++; platform.RequestRender(); }, "Add") { Key = "add" });
		return new Column(rows) { Key = "home" };
	}
}
