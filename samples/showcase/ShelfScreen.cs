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

	public static Element Render(ShelfState state, IPlatform platform)
	{
		Element Swatch(Piece item)
		{
			return item.Tone == "Mist"
				? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFF8EAEB4, Radius = 14, Padding = 12 } }
				: item.Tone == "Iron"
					? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF6B5E53, Radius = 14, Padding = 12 } }
					: item.Tone == "Ash"
						? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFC4B7A6, Radius = 14, Padding = 12 } }
						: item.Tone == "Cobalt"
							? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF1F4E79, Radius = 14, Padding = 12 } }
							: item.Tone == "Straw"
								? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFE0B15A, Radius = 14, Padding = 12 } }
								: item.Tone == "Slate"
									? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF5E6A73, Radius = 14, Padding = 12 } }
									: item.Tone == "Bone"
										? new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFE7E1D5, Radius = 14, Padding = 12 } }
										: new Column([new Text(item.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFFB5694D, Radius = 14, Padding = 12 } };
		}

		Element Row(Piece item)
		{
			return new Column(
			[
				Swatch(item),
				new Text(item.Title) { Key = "title", Style = new Style { FontSize = 20, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } },
				new Text(item.Note) { Key = "note", Style = new Style { FontSize = 14, Color = 0xFFD5E4DE } },
				new Button(
					"Open",
					() =>
					{
						platform.Push("piece", item);
						platform.RequestRender();
					},
					"Open")
				{
					Key = "open",
					Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 12, Padding = 10, FontSize = 15, Weight = FontWeight.Bold },
				},
			])
			{
				Key = item.Id,
				Style = new Style { Background = 0xFF0F6E56, Radius = 22, Padding = 16, Gap = 8, Margin = 2 },
			};
		}

		var rows = new List<Element>();
		rows.Add(new Text("The shelf") { Key = "heading", Style = new Style { FontSize = 32, Weight = FontWeight.Bold } });
		rows.Add(new Text("Eight pieces from this firing.") { Key = "intro", Style = new Style { FontSize = 16 } });
		foreach (var item in state.Pieces)
			rows.Add(Row(item));
		rows.Add(new Button(
			"Back",
			() =>
			{
				platform.Pop();
				platform.RequestRender();
			},
			"Back")
		{
			Key = "back",
			Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
		});
		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 14 } }) { Key = "scroll" };
	}
}
