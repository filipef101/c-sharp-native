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
			new Column(
			[
				new Button(
					"←",
					() =>
					{
						platform.Pop();
						platform.RequestRender();
					},
					"Back")
				{
					Key = "back",
					Style = new Style { FontSize = 22, Align = Align.Center, Padding = 8 },
				},
			])
			{
				Key = "back-row",
				Style = new Style { Across = 1, Align = Align.Start },
			},
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
			new Column(
			[
				new Text(state.Dark ? "Night is on." : "Day is on.")
				{
					Key = "night-state",
					Style = new Style { FontSize = 16, Color = 0xFF1C1917 },
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
				{
					Key = "night",
					Style = new Style { Color = 0xFF1C1917, FontSize = 16 },
				},
				new Text(state.Large ? "Large type is on." : "Type is at the usual size.")
				{
					Key = "type-state",
					Style = new Style { FontSize = 16, Color = 0xFF1C1917 },
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
				{
					Key = "large",
					Style = new Style { Color = 0xFF1C1917, FontSize = 16 },
				},
			])
			{
				Key = "card",
				Style = new Style { Background = 0xFFFFFFFF, Radius = 16, Padding = 16, Gap = 12 },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 24 },
		})
		{ Key = "scroll" };
}
