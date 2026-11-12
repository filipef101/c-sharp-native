namespace NativePoc.Where;

public sealed class WhereState
{
	public string Label { get; set; } = "idle";
}

public static class WhereScreen
{
	public const string ScreenId = "where";

	public static Element Render(WhereState state, IPlatform platform)
	{
		async void Read()
		{
			state.Label = await Location.Read(platform);
			platform.RequestRender();
		}

		return new Column(
		[
			new Text("Where") { Key = "title", Style = new Style { FontSize = 22 } },
			new Text(state.Label) { Key = "fix" },
			new Button("Read", () => Read(), "Read") { Key = "read" },
		])
		{ Key = "root" };
	}
}
