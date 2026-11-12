namespace NativePoc.Feed;

public sealed class FeedItem
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";
}

public sealed class FeedState
{
	public string Phase { get; set; } = "idle";

	public string Error { get; set; } = "";

	public string Json { get; set; } = "";

	public string Mark { get; set; } = "";

	public bool Loading { get; set; }

	public List<FeedItem> Items { get; set; } = new List<FeedItem>();

	public List<FeedItem> Generated { get; set; } = new List<FeedItem>();
}

public static class FeedScreen
{
	public const string ScreenId = "feed";

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
				state.Mark = await Timer.After(platform, 1);
				state.Json = await Http.GetJson(platform, "http://10.0.2.2:8094/feed");
				if (state.Json.StartsWith("http-error:"))
					throw new InvalidOperationException("down");
				Storage.Set(platform, "feed-json", state.Json);
				FeedModule.Apply(platform, state, state.Json);
				state.Phase = "ready";
				state.Error = "";
			}
			catch (InvalidOperationException)
			{
				state.Error = state.Json;
				state.Json = await Storage.Get(platform, "feed-json");
				if (state.Json.Length == 0)
				{
					state.Phase = "error";
					FeedModule.Apply(platform, state, "");
				}
				else
				{
					FeedModule.Apply(platform, state, state.Json);
					state.Phase = "caught";
				}
			}
			finally
			{
				state.Loading = false;
				platform.RequestRender();
			}
		}

		async void Post()
		{
			state.Loading = true;
			state.Phase = "loading";
			state.Error = "";
			platform.RequestRender();
			state.Json = await Http.PostJson(platform, "http://10.0.2.2:8094/feed", "{\"items\":[{\"id\":\"c\",\"title\":\"Cara\"}]}");
			if (state.Json.StartsWith("http-error:"))
			{
				state.Error = state.Json;
				state.Phase = "error";
				FeedModule.Apply(platform, state, "");
			}
			else
			{
				FeedModule.Apply(platform, state, state.Json);
				state.Phase = "posted";
				state.Error = "";
			}

			state.Loading = false;
			platform.RequestRender();
		}

		FeedModule.Generate(platform, state);
		var rows = new List<Element>();
		rows.Add(new Text("Feed") { Key = "heading", Style = new Style { FontSize = 22 } });
		rows.Add(new Text(state.Loading ? "Loading feed" : state.Phase) { Key = "phase" });
		rows.Add(new Text(state.Error) { Key = "error" });
		rows.Add(new Button("Refresh", () => Load(), "Refresh") { Key = "refresh" });
		rows.Add(new Button("Post", () => Post(), "Post") { Key = "post" });
		rows.Add(new Button("Back", () => platform.Pop(), "Back") { Key = "back" });
		foreach (var item in state.Generated)
			rows.Add(Row(item));
		return FeedRows.Window(rows, 6);
	}

	static Element Row(FeedItem item) => new Column(
	[
		new Text("• " + item.Title),
		new Image(item.Id == "0000" ? "feed_mark" : "http://10.0.2.2:8094/img/" + item.Id),
	])
	{ Key = item.Id };
}
