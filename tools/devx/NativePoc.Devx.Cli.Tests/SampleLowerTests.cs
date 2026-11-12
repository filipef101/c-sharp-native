using NativePoc;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class SampleLowerTests
{
	[Fact]
	public void Every_sample_screen_lowers()
	{
		var root = Repo();
		var files = Directory.EnumerateFiles(Path.Combine(root, "samples"), "*Screen.cs", SearchOption.AllDirectories)
			.Append(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"))
			.OrderBy(path => path, StringComparer.Ordinal);
		foreach (var file in files)
		{
			var source = File.ReadAllText(file);
			var dir = Path.GetDirectoryName(file)!;
			var companions = Directory.EnumerateFiles(dir, "*.cs")
				.Where(item => !string.Equals(item, file, StringComparison.Ordinal))
				.Select(File.ReadAllText)
				.ToArray();
			var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
			var lowered = SubsetLowerer.TryLower(source, companions, originFile: relative);
			var detail = lowered.Diagnostics.Count == 0
				? ""
				: lowered.Diagnostics[0].Id + " " + lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line + ":" + lowered.Diagnostics[0].Column;
			Assert.True(lowered.Ok, relative + " " + detail);
			foreach (var (works, length) in lowered.Actions)
			{
				if (works)
					Assert.True(length > 0, relative + " lowered a non-empty action to an empty body");
			}
		}
	}

	[Fact]
	public void A_bare_local_call_runs_while_the_screen_renders()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					void Load()
					{
						state.Title = "loaded";
					}
					Load();
					return new Text(state.Title) { Key = "title" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/notes/NotesScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Equal("loaded", session.Bag.ReadString("Title"));
		Assert.Contains("loaded", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void An_unknown_call_fails_on_that_call()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					Nope();
					return new Text(state.Title) { Key = "title" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/notes/NotesScreen.cs");
		Assert.False(lowered.Ok);
		var diagnostic = Assert.Single(lowered.Diagnostics);
		Assert.Contains("Nope()", diagnostic.Message, StringComparison.Ordinal);
		Assert.True(diagnostic.Line > 0);
		Assert.True(diagnostic.Column > 0);
	}

	[Fact]
	public void A_local_function_after_the_return_still_runs()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Column(
						new Text(state.Title) { Key = "title" },
						new Button("Go", () => Later(), "Go") { Key = "go" }
					);
					void Later()
					{
						state.Title = "later";
						platform.RequestRender();
					}
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("go");
		Assert.Contains("later", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void OnOpen_after_the_markup_runs_on_the_first_open()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Text(state.Title) { Key = "title" };
					platform.OnOpen(ScreenId, Open);
					void Open()
					{
						state.Title = "opened";
					}
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Contains("opened", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void An_await_keeps_its_result_in_a_string_local()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Id { get; set; } = "piece-1";
				public string Draft { get; set; } = "";
				public string DraftFor { get; set; } = "";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					void Load()
					{
						if (state.DraftFor != state.Id)
						{
							state.DraftFor = state.Id;
							state.Draft = "";
							Fill();
						}
					}
					async void Fill()
					{
						var savedId = await Persist.Get(platform, "northroom.note.id");
						if (savedId == state.Id && state.Draft.Length == 0)
						{
							var saved = await Persist.Get(platform, "northroom.note");
							if (saved.Length != 0)
							{
								state.Draft = saved;
								platform.RequestRender();
							}
						}
					}
					Load();
					return new Text(state.Draft) { Key = "draft" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!, (_, _, args) => args.Length > 0 && args[0] == "northroom.note" ? "hello note" : "piece-1");
		Assert.Contains("hello note", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void A_binding_call_lowers_to_the_jni_module()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Button("Pulse", () => DeviceVibrator.Vibrate(20), "Pulse") { Key = "pulse" };
				}
			}
			""";
		const string companion = """
			namespace NativePoc.Bind
			{
				[JniType("android/os/Vibrator")]
				static partial class DeviceVibrator
				{
					[JniMethod("vibrate", "(J)V", Instance = true, Service = "vibrator")]
					public static partial void Vibrate(long milliseconds);
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, [companion]);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.Contains(lowered.Actions, action => action.Works && action.Length > 0);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("pulse");
		Assert.Contains("jni.DeviceVibrator.Vibrate", session.Modules, StringComparer.Ordinal);
	}

	[Fact]
	public void A_binding_call_with_a_bad_argument_is_a_located_diagnostic()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Button("Pulse", () => DeviceVibrator.Vibrate(state.Title.Normalize()), "Pulse") { Key = "pulse" };
				}
			}
			""";
		const string companion = """
			namespace NativePoc.Bind
			{
				[JniType("android/os/Vibrator")]
				static partial class DeviceVibrator
				{
					[JniMethod("vibrate", "(J)V", Instance = true, Service = "vibrator")]
					public static partial void Vibrate(long milliseconds);
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, [companion], originFile: "samples/bind/BindScreen.cs");
		Assert.False(lowered.Ok);
		var diagnostic = Assert.Single(lowered.Diagnostics);
		Assert.Contains("DeviceVibrator.Vibrate()", diagnostic.Message, StringComparison.Ordinal);
		Assert.True(diagnostic.Line > 0);
	}

	[Fact]
	public void A_telemetry_increment_is_a_module_call()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Button("Go", () => { PackingTelemetry.FieldDeliveries++; }, "Go") { Key = "go" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.Contains(lowered.Actions, action => action.Works && action.Length > 0);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("go");
		Assert.Contains("telemetry.FieldDeliveries", session.Modules, StringComparer.Ordinal);
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
}
