namespace NativePoc;

public sealed class HomeState
{
	public int Count { get; set; }

	public string Title { get; set; } = "Hello";
}

public static class HomeScreen
{
	public const string ScreenId = "home";

	public static Element Render(HomeState state, IPlatform platform)
	{
		return new Column(
		[
			new Text(state.Title) { Key = "title", Style = new Style { FontSize = 22 } },
			new Text("Count " + state.Count) { Key = "count" },
			new Button("Add", () => { state.Count++; platform.RequestRender(); }, "Add") { Key = "add" },
		])
		{ Key = "home" };
	}
}
