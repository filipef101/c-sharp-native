namespace NativePoc.Feed;

public static class StoryScreen
{
	public const string ScreenId = "stories";

	public static Element Render(FeedState state, IPlatform platform)
	{
		async void Load()
		{
			try
			{
				state.Loading = true;
				state.Phase = "loading";
				state.Error = "";
				platform.RequestRender();
				state.Json = await Http.GetJson(platform, "http://127.0.0.1:8094/stories");
				if (state.Json.StartsWith("http-error:"))
					throw new InvalidOperationException("down");
				FeedModule.Apply(platform, state, state.Json);
				state.Phase = "ready";
				state.Error = "";
			}
			catch (InvalidOperationException)
			{
				state.Error = state.Json;
				state.Phase = "error";
				FeedModule.Apply(platform, state, "");
			}
			finally
			{
				state.Loading = false;
				platform.RequestRender();
			}
		}

		void Left()
		{
			state.Phase = "left";
		}

		Element Row(FeedItem item)
		{
			return new Column(
			[
				new Image(item.Id == "mark" ? "feed_mark" : "http://127.0.0.1:8094/img/" + item.Id) { Description = item.Title, Side = 72 },
				new Text(item.Title) { Key = "title", Style = new Style { FontSize = 16, Color = 0xFF143028 } },
			])
			{
				Key = item.Id,
				Style = new Style { Background = 0xFFFFFFFF, Radius = 16, Padding = 12, Gap = 8 },
			};
		}

		platform.OnOpen("stories", Load);
		platform.OnLeave(Left);
		var rows = new List<Element>();
		rows.Add(new Column(
		[
			new Text("Feed")
			{
				Key = "heading",
				Style = new Style { FontSize = 32, Weight = FontWeight.Bold, Color = 0xFFF7F4EF },
			},
			new Text(state.Loading ? "Loading feed" : state.Phase)
			{
				Key = "phase",
				Style = new Style { FontSize = 16, Color = 0xFFD5EBE3 },
			},
			new Text(state.Error)
			{
				Key = "error",
				Style = new Style { FontSize = 14, Color = 0xFFF7F4EF },
			},
		])
		{
			Key = "hero",
			Style = new Style { Background = 0xFF0F6E56, Radius = 16, Padding = 24, Gap = 8 },
		});
		rows.Add(new Button("Refresh", () => Load(), "Refresh")
		{
			Key = "refresh",
			Style = new Style { Background = 0xFFFFFFFF, Color = 0xFF1C1917, Radius = 16, Padding = 16, FontSize = 16, Align = Align.Start },
		});
		rows.Add(new Button("Back", () => platform.Pop(), "Back")
		{
			Key = "back",
			Style = new Style { Background = 0xFFFFFFFF, Color = 0xFF1C1917, Radius = 16, Padding = 16, FontSize = 16, Align = Align.Start },
		});
		foreach (var item in state.Items)
			rows.Add(Row(item));
		return FeedRows.Window(rows, 3, () => Load());
	}
}
