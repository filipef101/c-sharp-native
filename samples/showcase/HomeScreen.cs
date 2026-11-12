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
			new Text("Northroom")
			{
				Key = "heading",
				Style = new Style { FontSize = 32, Weight = FontWeight.Bold },
			},
			new Text("Small-batch ceramics from a room that faces the water.")
			{
				Key = "lede",
				Style = new Style { FontSize = 16 },
			},
			new Column(
			[
				new Text("8") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } },
				new Text("pieces on the shelf") { Key = "label", Style = new Style { FontSize = 14, Color = 0xFFD5E4DE } },
			])
			{
				Key = "pieces",
				Style = new Style { Background = 0xFF0F6E56, Radius = 22, Padding = 18, Gap = 4 },
			},
			new Column(
			[
				new Text("3") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } },
				new Text("kept from the last firing") { Key = "label", Style = new Style { FontSize = 14, Color = 0xFFD5E4DE } },
			])
			{
				Key = "kept",
				Style = new Style { Background = 0xFF0F6E56, Radius = 22, Padding = 18, Gap = 4 },
			},
			new Column(
			[
				new Text("Thursday") { Key = "value", Style = new Style { FontSize = 18, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } },
				new Text("the kiln was opened") { Key = "label", Style = new Style { FontSize = 14, Color = 0xFFD5E4DE } },
			])
			{
				Key = "kiln",
				Style = new Style { Background = 0xFF0F6E56, Radius = 22, Padding = 18, Gap = 4 },
			},
			new Button(
				"The shelf",
				() =>
				{
					platform.Push("shelf");
					platform.RequestRender();
				},
				"The shelf")
			{
				Key = "shelf",
				Style = new Style { Background = 0xFF0F6E56, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
			new Button(
				"The room",
				() =>
				{
					platform.Push("room");
					platform.RequestRender();
				},
				"The room")
			{
				Key = "room",
				Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 14 },
		})
		{ Key = "scroll" };
}
