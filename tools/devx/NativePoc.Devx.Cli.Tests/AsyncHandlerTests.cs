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
				return Task.FromResult("");
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
