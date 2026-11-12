namespace NativePoc.Showcase;

public sealed class PieceState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Note { get; set; } = "";

	public string Tone { get; set; } = "";

	public string Kept { get; set; } = "";

	public string Status { get; set; } = "";

	public string Draft { get; set; } = "";

	public string DraftFor { get; set; } = "";

	public bool Dark { get; set; }
}

public static class PieceScreen
{
	public const string ScreenId = "piece";

	public static Element Render(PieceState state, IPlatform platform)
	{
		void Load()
		{
			if (string.Equals(state.DraftFor, state.Id, StringComparison.Ordinal))
				return;
			var id = state.Id;
			state.DraftFor = id;
			state.Draft = "";
			_ = Fill(id);
		}

		async Task Fill(string id)
		{
			var savedId = await Persist.Get(platform, "northroom.note.id");
			var saved = await Persist.Get(platform, "northroom.note");
			if (!string.Equals(state.Id, id, StringComparison.Ordinal))
				return;
			if (!string.Equals(savedId, id, StringComparison.Ordinal) || saved.Length == 0 || state.Draft.Length > 0)
				return;
			state.Draft = saved;
			platform.RequestRender();
		}

		async void Keep()
		{
			var kept = state.Kept.Length == 0;
			state.Kept = kept ? state.Id : "";
			state.Status = await Persist.Put(platform, "northroom.keep", state.Kept);
			platform.RequestRender();
			Animated.Spring(platform, "keep", 86, 100, 220);
		}

		async void Save()
		{
			state.Status = await Persist.Put(platform, "northroom.note", state.Draft);
			state.Status = await Persist.Put(platform, "northroom.note.id", state.Id);
		}

		Load();

		return new ScrollView(new Column(
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
			state.Tone == "Mist"
				? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFF6E97A0, Wash = 0xFFD5E6EA, Color = 0xFF143028, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
				: state.Tone == "Iron"
					? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFF4A4038, Wash = 0xFF8A7B6E, Color = 0xFFF7F4EF, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
					: state.Tone == "Ash"
						? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFFA89886, Wash = 0xFFE4D9CC, Color = 0xFF143028, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
						: state.Tone == "Cobalt"
							? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFF163A5C, Wash = 0xFF3E78B0, Color = 0xFFF7F4EF, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
							: state.Tone == "Straw"
								? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFFC4923A, Wash = 0xFFF0D48A, Color = 0xFF143028, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
								: state.Tone == "Slate"
									? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFF3E4850, Wash = 0xFF8A969E, Color = 0xFFF7F4EF, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
									: state.Tone == "Bone"
										? new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFFC9C0B0, Wash = 0xFFF3EFE6, Color = 0xFF143028, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } }
										: new Text(state.Tone) { Key = "swatch", Style = new Style { Background = 0xFF8C4A34, Wash = 0xFFD4A090, Color = 0xFFF7F4EF, Radius = 16, Padding = 48, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center } },
			new Text(state.Title) { Key = "title", Style = new Style { FontSize = 28, Weight = FontWeight.Bold } },
			new Text(state.Note) { Key = "note", Style = new Style { FontSize = 16 } },
			new TextField(
				state.Draft,
				value =>
				{
					state.Draft = value;
					Save();
					platform.RequestRender();
				},
				"Note",
				"Note")
			{
				Key = "draft",
				Autofill = false,
			},
			state.Kept.Length == 0
				? new Button("Keep", () => Keep(), "Keep")
				{
					Key = "keep",
					Style = new Style { Background = 0xFFFFFFFF, Stroke = 0xFF0F6E56, Color = 0xFF0F6E56, Radius = 24, Padding = 14, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center },
				}
				: new Button("Kept", () => Keep(), "Unkeep")
				{
					Key = "keep",
					Style = new Style { Background = 0xFF0F6E56, Color = 0xFFF7F4EF, Radius = 24, Padding = 14, FontSize = 16, Weight = FontWeight.Bold, Align = Align.Center },
				},
		])
		{
			Key = "page",
			Style = new Style { Gap = 16 },
		})
		{ Key = "scroll" };
	}
}
