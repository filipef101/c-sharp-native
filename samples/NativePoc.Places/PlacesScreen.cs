namespace NativePoc.Places;

public sealed class PlacesState
{
	public string Banner { get; set; } = "loading";

	public int Tick { get; set; }
}

public static class PlacesScreen
{
	public const string ScreenId = "places";

	public static Element Render(PlacesState state, IPlatform platform)
	{
		platform.Effect("Banner", "places");
		platform.Every("Tick");
		return new Column(
			new Text("Place") { Key = "where", Style = new Style { FontSize = 22 } },
			new Text(state.Banner) { Key = "banner" },
			new Text("t" + state.Tick) { Key = "tick" },
			new Image("marker") { Key = "marker" }
		)
		{ Key = "places" };
	}
}
