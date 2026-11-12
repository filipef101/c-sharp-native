namespace NativePoc.Showcase;

public sealed class PieceState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Note { get; set; } = "";

	public string Tone { get; set; } = "";

	public string Kept { get; set; } = "";

	public string Status { get; set; } = "";

	public bool Dark { get; set; }
}

public static class PieceScreen
{
	public const string ScreenId = "piece";

	public static Element Render(PieceState state, IPlatform platform)
	{
		async void Keep()
		{
			state.Kept = state.Id;
			state.Status = await Persist.Put(platform, "northroom.keep", state.Id);
			platform.RequestRender();
		}

		async void Clear()
		{
			state.Status = await Persist.Put(platform, "northroom.keep", "");
			state.Kept = "";
			platform.RequestRender();
		}

		async void Recall()
		{
			state.Kept = await Persist.Get(platform, "northroom.keep");
			platform.RequestRender();
		}

		return new ScrollView(new Column(
		[
			new Text("Piece") { Key = "heading", Style = new Style { FontSize = 32, Weight = FontWeight.Bold } },
			state.Tone == "Mist"
				? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFF8EAEB4, Radius = 18, Padding = 18 } }
				: state.Tone == "Iron"
					? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF6B5E53, Radius = 18, Padding = 18 } }
					: state.Tone == "Ash"
						? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFC4B7A6, Radius = 18, Padding = 18 } }
						: state.Tone == "Cobalt"
							? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF1F4E79, Radius = 18, Padding = 18 } }
							: state.Tone == "Straw"
								? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFE0B15A, Radius = 18, Padding = 18 } }
								: state.Tone == "Slate"
									? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFF5E6A73, Radius = 18, Padding = 18 } }
									: state.Tone == "Bone"
										? new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFF143028 } }]) { Key = "swatch", Style = new Style { Background = 0xFFE7E1D5, Radius = 18, Padding = 18 } }
										: new Column([new Text(state.Tone) { Key = "chip", Style = new Style { FontSize = 13, Weight = FontWeight.Bold, Color = 0xFFF4F7F5 } }]) { Key = "swatch", Style = new Style { Background = 0xFFB5694D, Radius = 18, Padding = 18 } },
			new Text(state.Title) { Key = "title", Style = new Style { FontSize = 28, Weight = FontWeight.Bold } },
			new Text(state.Note) { Key = "note", Style = new Style { FontSize = 16 } },
			new Text(state.Kept.Length == 0 ? "Not kept yet." : "Kept " + state.Kept)
			{
				Key = "kept",
				Style = new Style { FontSize = 16 },
			},
			new Button("Keep", () => Keep(), "Keep")
			{
				Key = "keep",
				Style = new Style { Background = 0xFF0F6E56, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
			new Button("Clear", () => Clear(), "Clear")
			{
				Key = "clear",
				Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
			new Button("Recall", () => Recall(), "Recall")
			{
				Key = "recall",
				Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
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
				Style = new Style { Background = 0xFF143028, Color = 0xFFF4F7F5, Radius = 16, Padding = 14, FontSize = 16, Weight = FontWeight.Bold },
			},
		])
		{
			Key = "page",
			Style = new Style { Gap = 14 },
		})
		{ Key = "scroll" };
	}
}
