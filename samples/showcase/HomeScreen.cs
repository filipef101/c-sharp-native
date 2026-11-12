namespace NativePoc.Showcase;

public sealed class HomeState
{
	public bool Dark { get; set; }

	public bool Large { get; set; }
}

public static class HomeScreen
{
	public const string ScreenId = "home";

	public static Element Render(HomeState state, IPlatform platform) =>
		new ScrollView(new Column(
		[
			new Column(
			[
				new Text("Northroom")
				{
					Key = "heading",
					Style = new Style { FontSize = 32, Weight = FontWeight.Bold, Color = 0xFFF7F4EF },
				},
				new Text("Small-batch ceramics from a room that faces the water.")
				{
					Key = "lede",
					Style = new Style { FontSize = 16, Color = 0xFFD5EBE3 },
				},
			])
			{
				Key = "hero",
				Style = new Style { Background = 0xFF0F6E56, Wash = 0xFF3FA888, Radius = 16, Padding = 24, Gap = 8 },
			},
			new Column(
			[
				new Column(
				[
					new Text("8") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFF143028, Align = Align.Center } },
					new Text("pieces") { Key = "label", Style = new Style { FontSize = 13, Color = 0xFF3D4F48, Align = Align.Center } },
				])
				{
					Key = "pieces",
					Style = new Style { Background = 0xFFE4F0EB, Radius = 16, Padding = 16, Gap = 4, Grow = 1 },
				},
				new Column(
				[
					new Text("3") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFF143028, Align = Align.Center } },
					new Text("kept") { Key = "label", Style = new Style { FontSize = 13, Color = 0xFF3D4F48, Align = Align.Center } },
				])
				{
					Key = "kept",
					Style = new Style { Background = 0xFFE4F0EB, Radius = 16, Padding = 16, Gap = 4, Grow = 1 },
				},
				new Column(
				[
					new Text("Thu") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFF143028, Align = Align.Center } },
					new Text("kiln") { Key = "label", Style = new Style { FontSize = 13, Color = 0xFF3D4F48, Align = Align.Center } },
				])
				{
					Key = "kiln",
					Style = new Style { Background = 0xFFE4F0EB, Radius = 16, Padding = 16, Gap = 4, Grow = 1 },
				},
			])
			{
				Key = "stats",
				Style = new Style { Across = 1, Gap = 16 },
			},
			new Button(
				"The shelf  ›",
				() =>
				{
					platform.Push("shelf");
					platform.RequestRender();
				},
				"The shelf")
			{
				Key = "shelf",
				Style = new Style { Background = 0xFFFFFFFF, Color = 0xFF1C1917, Radius = 16, Padding = 16, FontSize = 16, Align = Align.Start },
			},
			new Button(
				"The room  ›",
				() =>
				{
					platform.Push("room");
					platform.RequestRender();
				},
				"The room")
			{
				Key = "room",
				Style = new Style { Background = 0xFFFFFFFF, Color = 0xFF1C1917, Radius = 16, Padding = 16, FontSize = 16, Align = Align.Start },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 16 },
		})
		{ Key = "scroll" };
}
