using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class AsyncHandlerTests
{
	[ThreadStatic]
	static bool _mine;

	const string Screen = """
		namespace NativePoc.Feed;
		public sealed class FeedItem
		{
			public string Id { get; set; } = "";
			public string Title { get; set; } = "";
		}
		public sealed class FeedState
		{
			public string Phase { get; set; } = "";
			public string Error { get; set; } = "";
			public string Json { get; set; } = "";
			public string Mark { get; set; } = "";
			public bool Loading { get; set; }
			public List<FeedItem> Items { get; set; } = new List<FeedItem>();
		}
		public static class FeedScreen
		{
			public const string ScreenId = "feed";
			public static Element Render(FeedState state, IPlatform platform)
			{
				async void Load()
				{
					state.Loading = true;
					state.Phase = "loading";
					state.Error = "";
					platform.RequestRender();
					state.Mark = await Timer.After(platform, 1);
					state.Json = await Http.GetJson(platform, "http://10.0.2.2:8094/feed");
					if (state.Json.StartsWith("http-error:"))
					{
						state.Error = state.Json;
						state.Json = await Storage.Get(platform, "feed-json");
						if (state.Json.Length == 0)
						{
							state.Phase = "error";
							FeedModule.Apply(platform, state, "");
						}
						else
							state.Phase = "offline";
					}
					else
					{
						state.Phase = "ready";
						state.Error = "";
					}
					state.Loading = false;
				}
				return new Column(
				[
					new Text(state.Phase) { Key = "phase" },
					new Button("Refresh", () => Load(), "Refresh") { Key = "refresh" },
				])
				{ Key = "root" };
			}
		}
		""";

	[Fact]
	public void The_feed_screen_file_lowers()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "samples", "feed", "FeedScreen.cs"));
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/feed/FeedScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var tree = RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).Render(new AsyncPlatform());
		var column = Assert.IsType<Column>(tree);
		var list = Assert.IsType<ListView>(column.Children[^1]);
		var window = Assert.IsType<RecordProgram.WindowList>(list.Rows);
		Assert.Equal(1000, window.Count);
		Assert.Equal(0, window.Built);
		Assert.NotNull(list.OnRefresh);
		var row = Assert.IsType<Column>(window[0]);
		Assert.NotNull(row.OnSwipe);
		Assert.Equal("0000", row.Key);
		Assert.Equal("• Row 0000", Assert.IsType<Text>(row.Children[0]).Value);
		Assert.Equal("feed_mark", Assert.IsType<Image>(row.Children[1]).Source);
		Assert.Equal(1, window.Built);
		Assert.Equal("0001", window.Cell(1, 0));
		Assert.Equal("Row 0001", window.Cell(1, 1));
		Assert.Equal(1, window.Built);
		var next = Assert.IsType<Column>(window[1]);
		Assert.Equal("http://10.0.2.2:8094/img/0001", Assert.IsType<Image>(next.Children[1]).Source);
		Assert.Equal(2, window.Built);
	}

	[Fact]
	public void An_await_suspends_and_resumes_on_the_next_segment()
	{
		var lowered = SubsetLowerer.TryLower(Screen, originFile: "samples/feed/FeedScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		var bound = RecordProgram.Bind(lowered.Program!, bag);
		var platform = new AsyncPlatform();
		var notes = new List<int>();
		ActionLog.OnScript = (_, steps, _) =>
		{
			if (_mine)
				notes.Add(steps);
		};
		_mine = true;
		try
		{
			ElementIds.Invoke(bound.Render(platform), "refresh", null);
			Assert.Equal("loading", bag.ReadString("Phase"));
			Assert.True(bag.ReadBool("Loading"));
			Assert.Single(notes);
			platform.Release("ok");
			Assert.Equal(2, notes.Count);
			platform.Release("{\"items\":[{\"id\":\"a\",\"title\":\"Ada\"}]}");
			Assert.Equal("ready", bag.ReadString("Phase"));
			Assert.Equal("{\"items\":[{\"id\":\"a\",\"title\":\"Ada\"}]}", bag.ReadString("Json"));
			Assert.False(bag.ReadBool("Loading"));
			Assert.Equal("ok", bag.ReadString("Mark"));
			Assert.Equal(3, notes.Count);
			Assert.True(notes[1] < notes[0]);
		}
		finally
		{
			_mine = false;
			ActionLog.OnScript = null;
		}
	}

	[Fact]
	public void A_try_around_await_catches_a_throw_inside_if()
	{
		const string source = """
			namespace NativePoc.Feed;
			public sealed class FeedItem
			{
				public string Id { get; set; } = "";
				public string Title { get; set; } = "";
			}
			public sealed class FeedState
			{
				public string Phase { get; set; } = "";
				public string Error { get; set; } = "";
				public string Json { get; set; } = "";
				public string Mark { get; set; } = "";
				public bool Loading { get; set; }
				public List<FeedItem> Items { get; set; } = new List<FeedItem>();
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
						}
					}
					return new Column(
					[
						new Text(state.Phase) { Key = "phase" },
						new Button("Refresh", () => Load(), "Refresh") { Key = "refresh" },
					])
					{ Key = "root" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/feed/FeedScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line);
		var bag = new RecordProgram.Bag();
		var bound = RecordProgram.Bind(lowered.Program!, bag);
		var platform = new AsyncPlatform { Storage = "{\"items\":[{\"id\":\"a\",\"title\":\"Ada\"}]}" };
		ElementIds.Invoke(bound.Render(platform), "refresh", null);
		platform.Release("ok");
		platform.Release("http-error: SocketTimeoutException");
		Assert.Equal("caught", bag.ReadString("Phase"));
		Assert.Equal("http-error: SocketTimeoutException", bag.ReadString("Error"));
		Assert.False(bag.ReadBool("Loading"));
		Assert.Contains(platform.Calls, call => call == "feed.Apply");
		Assert.Null(ActionLog.LastStop);
	}

	[Fact]
	public void A_faulted_fetch_keeps_the_error_and_falls_back()
	{
		var lowered = SubsetLowerer.TryLower(Screen, originFile: "samples/feed/FeedScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		var bound = RecordProgram.Bind(lowered.Program!, bag);
		var platform = new AsyncPlatform();
		ElementIds.Invoke(bound.Render(platform), "refresh", null);
		platform.Release("ok");
		platform.Release("http-error: HttpRequestException");
		Assert.Equal("error", bag.ReadString("Phase"));
		Assert.Equal("http-error: HttpRequestException", bag.ReadString("Error"));
		Assert.Equal("", bag.ReadString("Json"));
		Assert.False(bag.ReadBool("Loading"));
		Assert.Contains(platform.Calls, call => call == "feed.Apply");
	}

	[Fact]
	public void An_interpreted_handler_reads_location_into_a_string_field()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "samples", "where", "WhereScreen.cs"));
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/where/WhereScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		var bound = RecordProgram.Bind(lowered.Program!, bag);
		var platform = new AsyncPlatform();
		ElementIds.Invoke(bound.Render(platform), "read", null);
		Assert.Contains(platform.Calls, call => call == "location.Read");
		platform.Release("granted 37.8 -122.4 4");
		Assert.Equal("granted 37.8 -122.4 4", bag.ReadString("Label"));
		Assert.Equal(LocationAccess.Granted, Location.Parse(bag.ReadString("Label")).Access);
		Assert.Equal(37.8, Location.Parse(bag.ReadString("Label")).Latitude);
	}

	[Fact]
	public void The_visit_form_lowers_and_the_stop_screen_fetches()
	{
		var root = Repo();
		var visit = SubsetLowerer.TryLower(
			File.ReadAllText(Path.Combine(root, "samples", "visit", "VisitScreen.cs")),
			originFile: "samples/visit/VisitScreen.cs");
		Assert.True(visit.Ok, visit.Diagnostics.Count == 0 ? "" : visit.Diagnostics[0].Message);
		var stop = SubsetLowerer.TryLower(
			File.ReadAllText(Path.Combine(root, "samples", "visit", "StopScreen.cs")),
			originFile: "samples/visit/StopScreen.cs");
		Assert.True(stop.Ok, stop.Diagnostics.Count == 0 ? "" : stop.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		var bound = RecordProgram.Bind(visit.Program!, bag);
		var platform = new AsyncPlatform();
		var tree = bound.Render(platform);
		Assert.Equal(22, Assert.IsType<Text>(ElementIds.Require(tree, "scroll/list/heading")).Style!.Value.FontSize);
		ElementIds.Submit(tree, "scroll/list/when");
		Assert.Equal("Place is required", bag.ReadString("Error"));
		ElementIds.Invoke(tree, "scroll/list/place", "Pier");
		ElementIds.Invoke(tree, "scroll/list/when", "Friday");
		ElementIds.Submit(tree, "scroll/list/when");
		Assert.Contains(platform.Calls, call => call == "http.GetJson");
		platform.Release("{\"place\":\"Pier\"}");
		Assert.Equal("", bag.ReadString("Error"));
		Assert.Equal("{\"place\":\"Pier\"}", bag.ReadString("Body"));
		Assert.Equal("button", ScreenSnapshot.Role(ElementIds.Require(bound.Render(platform), "scroll/list/Pier/open")));
		ElementIds.Invoke(bound.Render(platform), "scroll/list/locate", null);
		Assert.Contains(platform.Calls, call => call == "location.Read");
		var detail = RecordProgram.Bind(stop.Program!, new RecordProgram.Bag());
		var lookup = new AsyncPlatform();
		ElementIds.Invoke(detail.Render(lookup), "stop/lookup", null);
		Assert.Contains(lookup.Calls, call => call == "http.GetJson");
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}

	sealed class AsyncPlatform : IPlatform
	{
		public List<string> Calls { get; } = new();

		public string Storage { get; set; } = "";

		Action<string>? _resume;

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Module(string module, string method, string[] args) => Calls.Add(module + "." + method);

		public Task<string> ModuleAsync(string module, string method, string[] args)
		{
			Calls.Add(module + "." + method);
			if (module == "storage")
				return Task.FromResult(Storage);
			return new TaskCompletionSource<string>().Task;
		}

		public void Continue(Task<string> task, Action<string> resume)
		{
			_ = task;
			_resume = resume;
		}

		public void Release(string value)
		{
			var resume = _resume ?? throw new InvalidOperationException("The handler is not waiting.");
			_resume = null;
			resume(value);
		}
	}
}
