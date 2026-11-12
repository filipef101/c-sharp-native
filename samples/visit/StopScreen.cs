namespace NativePoc.Visit;

public sealed class StopState
{
	public string Place { get; set; } = "";

	public string When { get; set; } = "";

	public string Body { get; set; } = "";

	public string Error { get; set; } = "";
}

public static class StopScreen
{
	public const string ScreenId = "stop";

	public static Element Render(StopState state, IPlatform platform)
	{
		async void Lookup()
		{
			state.Body = await Http.GetJson(platform, "http://10.0.2.2:8094/visit");
			if (state.Body.StartsWith("http-error:"))
				state.Error = state.Body;
			else
				state.Error = "";
			platform.RequestRender();
		}

		return new Column(
		[
			new Text("Stop") { Key = "heading", Style = new Style { FontSize = 22 } },
			new Text(state.Place) { Key = "place" },
			new Text(state.When) { Key = "when" },
			new Text(state.Body) { Key = "body" },
			new Text(state.Error) { Key = "error" },
			new Button("Lookup", () => Lookup(), "Lookup") { Key = "lookup" },
			new Button("Back", () => { Nav.Pop(platform); platform.RequestRender(); }, "Back") { Key = "back" },
		])
		{ Key = "stop" };
	}
}
