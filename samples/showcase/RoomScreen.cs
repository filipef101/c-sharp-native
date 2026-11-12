namespace NativePoc.Showcase;

public sealed class RoomState
{
	public bool Dark { get; set; }

	public bool Large { get; set; }
}

public static class RoomScreen
{
	public const string ScreenId = "room";

	public static Element Render(RoomState state, IPlatform platform) =>
		new ScrollView(new Column(
		[
			new Text("The room")
			{
				Key = "heading",
				Style = new Style { FontSize = 32, Weight = FontWeight.Bold },
			},
			new Text("Night changes the page. Large type sets the app to twice the usual size.")
			{
				Key = "lede",
				Style = new Style { FontSize = 16 },
			},
			new Text(state.Dark ? "Night is on." : "Day is on.")
			{
				Key = "night-state",
				Style = new Style { FontSize = 14 },
			},
			new Switch(
				state.Dark,
				value =>
				{
					state.Dark = value;
					Theme.Mode(platform, state.Dark);
					platform.RequestRender();
				},
				"Night")
			{ Key = "night" },
			new Text(state.Large ? "Large type is on." : "Type is at the usual size.")
			{
				Key = "type-state",
				Style = new Style { FontSize = 14 },
			},
			new Switch(
				state.Large,
				value =>
				{
					state.Large = value;
					Theme.Scale(platform, state.Large);
					platform.RequestRender();
				},
				"Large type")
			{ Key = "large" },
			new Button(
				"Back",
				() =>
				{
					platform.Pop();
					platform.RequestRender();
				},
				"Back")
			{
				Key = "back",
				Style = new Style { Background = 0xFF0F6E56, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 14 },
		})
		{ Key = "scroll" };
}
