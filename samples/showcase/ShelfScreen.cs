namespace NativePoc.Showcase;

public sealed class Piece
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Note { get; set; } = "";

	public string Tone { get; set; } = "";
}

public sealed class ShelfState
{
	public bool Dark { get; set; }

	public List<Piece> Pieces { get; set; } = new List<Piece>
	{
		new Piece { Id = "harbor", Title = "Harbor bowl", Note = "Mist glaze, 18 cm, a wide bowl for the table.", Tone = "Mist" },
		new Piece { Id = "kiln", Title = "Kiln door", Note = "Iron slip, 24 cm, the color of the door after firing.", Tone = "Iron" },
		new Piece { Id = "cedar", Title = "Cedar tray", Note = "Ash glaze, 32 cm, for bread still warm from the oven.", Tone = "Ash" },
		new Piece { Id = "night", Title = "Night cup", Note = "Cobalt, 9 cm, a cup that looks full before the tea.", Tone = "Cobalt" },
		new Piece { Id = "field", Title = "Field plate", Note = "Straw glaze, 27 cm, the color of cut grass in August.", Tone = "Straw" },
		new Piece { Id = "rain", Title = "Rain jar", Note = "Slate, 21 cm, a jar for stems picked in the wet.", Tone = "Slate" },
		new Piece { Id = "linen", Title = "Linen vase", Note = "Bone, 30 cm, tall enough for a single branch.", Tone = "Bone" },
		new Piece { Id = "moss", Title = "Moss tile", Note = "Copper, 12 cm, a tile meant to be touched.", Tone = "Copper" },
	};
}

public static class ShelfScreen
{
	public const string ScreenId = "shelf";

	public static Element Render(ShelfState state, IPlatform platform) =>
		new Column(
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
			new Column(
			[
				new Text("The shelf")
				{
					Key = "heading",
					Style = new Style { FontSize = 32, Weight = FontWeight.Bold },
				},
				new Text("Eight pieces from this firing.")
				{
					Key = "intro",
					Style = new Style { FontSize = 16 },
				},
			])
			{
				Key = "head",
				Style = new Style { Gap = 4 },
			},
			new ListView(
			[
				new Column(
				[
					new Image("shelf_harbor")
					{
						Key = "swatch",
						Description = "Harbor bowl, Mist",
						Side = 168,
						Style = new Style { Background = 0xFF6E97A0, Wash = 0xFFD5E6EA, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "harbor", 94, 100, 140);
							platform.Push("piece", state.Pieces[0]);
							platform.RequestRender();
						},
					},
					new Text("Harbor bowl") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "harbor",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_kiln")
					{
						Key = "swatch",
						Description = "Kiln door, Iron",
						Side = 168,
						Style = new Style { Background = 0xFF4A4038, Wash = 0xFF8A7B6E, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "kiln", 94, 100, 140);
							platform.Push("piece", state.Pieces[1]);
							platform.RequestRender();
						},
					},
					new Text("Kiln door") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "kiln",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_cedar")
					{
						Key = "swatch",
						Description = "Cedar tray, Ash",
						Side = 168,
						Style = new Style { Background = 0xFFA89886, Wash = 0xFFE4D9CC, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "cedar", 94, 100, 140);
							platform.Push("piece", state.Pieces[2]);
							platform.RequestRender();
						},
					},
					new Text("Cedar tray") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "cedar",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_night")
					{
						Key = "swatch",
						Description = "Night cup, Cobalt",
						Side = 168,
						Style = new Style { Background = 0xFF163A5C, Wash = 0xFF3E78B0, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "night", 94, 100, 140);
							platform.Push("piece", state.Pieces[3]);
							platform.RequestRender();
						},
					},
					new Text("Night cup") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "night",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_field")
					{
						Key = "swatch",
						Description = "Field plate, Straw",
						Side = 168,
						Style = new Style { Background = 0xFFC4923A, Wash = 0xFFF0D48A, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "field", 94, 100, 140);
							platform.Push("piece", state.Pieces[4]);
							platform.RequestRender();
						},
					},
					new Text("Field plate") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "field",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_rain")
					{
						Key = "swatch",
						Description = "Rain jar, Slate",
						Side = 168,
						Style = new Style { Background = 0xFF3E4850, Wash = 0xFF8A969E, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "rain", 94, 100, 140);
							platform.Push("piece", state.Pieces[5]);
							platform.RequestRender();
						},
					},
					new Text("Rain jar") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "rain",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_linen")
					{
						Key = "swatch",
						Description = "Linen vase, Bone",
						Side = 168,
						Style = new Style { Background = 0xFFC9C0B0, Wash = 0xFFF3EFE6, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "linen", 94, 100, 140);
							platform.Push("piece", state.Pieces[6]);
							platform.RequestRender();
						},
					},
					new Text("Linen vase") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "linen",
					Style = new Style { Gap = 8 },
				},
				new Column(
				[
					new Image("shelf_moss")
					{
						Key = "swatch",
						Description = "Moss tile, Copper",
						Side = 168,
						Style = new Style { Background = 0xFF8C4A34, Wash = 0xFFD4A090, Radius = 16 },
						OnTap = () =>
						{
							Animated.Timing(platform, "moss", 94, 100, 140);
							platform.Push("piece", state.Pieces[7]);
							platform.RequestRender();
						},
					},
					new Text("Moss tile") { Key = "name", Style = new Style { FontSize = 16, Align = Align.Center } },
				])
				{
					Key = "moss",
					Style = new Style { Gap = 8 },
				},
			])
			{
				Key = "grid",
				Style = new Style { Across = 2, Gap = 16 },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 20 },
		};
}
