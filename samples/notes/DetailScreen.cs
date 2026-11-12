namespace NativePoc.Notes;

public sealed class DetailState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";
}

public static class DetailScreen
{
	public const string ScreenId = "detail";

	public static Element Render(DetailState state, IPlatform platform)
	{
		return new Column(
		[
			new Text("Note") { Key = "heading", Style = new Style { FontSize = 22 } },
			new Text(state.Title) { Key = "title" },
			new Text(state.Body) { Key = "body" },
			new Button(
				"Edit",
				() =>
				{
					Nav.Push(platform, "edit");
					platform.RequestRender();
				},
				"Edit")
			{ Key = "edit" },
			new Button(
				"Back",
				() =>
				{
					Nav.Pop(platform);
					platform.RequestRender();
				},
				"Back")
			{ Key = "back" },
		])
		{ Key = "detail" };
	}
}
