namespace NativePoc.Web;

public sealed class WebState
{
	public string Url { get; set; } = "about:blank";
}

public static class WebScreen
{
	public const string ScreenId = "web";

	public static Element Render(WebState state, IPlatform platform) =>
		new Column(
		[
			new Text(L.Get("Page")) { Key = "title" },
			new HostView("webview", "url", state.Url)
			{
				Key = "page",
				Event = "navigate",
				OnEvent = value =>
				{
					state.Url = value;
					platform.RequestRender();
				},
			},
		])
		{ Key = "web" };
}
