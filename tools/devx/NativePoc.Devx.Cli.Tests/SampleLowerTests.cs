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
	public void An_expression_bodied_local_function_runs_after_the_return()
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
					return new Button("Go", () => Mark(), "Go") { Key = "go" };
					void Mark() => state.Title = "marked";
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("go");
		Assert.Equal("marked", session.Bag.ReadString("Title"));
	}

	[Fact]
	public void A_nested_local_function_is_hoisted_and_runs()
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
					return new Button("Go", () => Outer(), "Go") { Key = "go" };
					void Outer()
					{
						Inner();
						void Inner() => state.Title = "nested";
					}
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("go");
		Assert.Equal("nested", session.Bag.ReadString("Title"));
	}

	[Fact]
	public void A_companion_data_class_is_recorded_and_a_foreign_member_is_reported()
	{
		var root = Repo();
		var file = Path.Combine(root, "samples", "NativePoc.Packing", "PackingScreen.cs");
		var source = File.ReadAllText(file);
		var companions = Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.cs")
			.Where(item => !string.Equals(item, file, StringComparison.Ordinal))
			.Select(File.ReadAllText)
			.ToArray();
		var lowered = SubsetLowerer.TryLower(source, companions, originFile: "samples/NativePoc.Packing/PackingScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.Contains("PackingState", lowered.Types, StringComparer.Ordinal);
		Assert.Contains("PackingRoute", lowered.Types, StringComparer.Ordinal);
		Assert.Contains("PackingItem", lowered.Types, StringComparer.Ordinal);
		var reported = Assert.Single(lowered.Reported, diagnostic => diagnostic.Message.Contains("PackingTelemetry", StringComparison.Ordinal));
		Assert.Contains("Change", reported.Message, StringComparison.Ordinal);
		Assert.True(reported.Line > 0);
		Assert.True(reported.Column > 0);
		Assert.DoesNotContain(lowered.Reported, diagnostic => diagnostic.Message.Contains("PackingSession", StringComparison.Ordinal));
		Assert.DoesNotContain(lowered.Reported, diagnostic => diagnostic.Message.Contains("IPackingHost", StringComparison.Ordinal));
		Assert.DoesNotContain(lowered.Reported, diagnostic => diagnostic.Message.Contains("PackingActions", StringComparison.Ordinal));
		Assert.DoesNotContain(lowered.Reported, diagnostic => diagnostic.Message.Contains("PackingNavigation", StringComparison.Ordinal));
	}

	[Fact]
	public void A_byte_long_or_explicit_enum_lowers_as_an_int()
	{
		const string source = """
			namespace NativePoc.Notes;
			public enum Width : byte
			{
				Narrow = 1,
				Wide = 4,
			}
			public enum Span : long
			{
				Short = 2L,
				Tall = 9L,
			}
			public enum Mark
			{
				None,
				Flag = 3,
				Also = Flag,
			}
			public sealed class NotesState
			{
				public Width Size { get; set; } = Width.Wide;
				public Span Reach { get; set; } = Span.Tall;
				public Mark Tag { get; set; } = Mark.Also;
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Text("notes") { Key = "size" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.Contains("Width", lowered.Types, StringComparer.Ordinal);
		Assert.Contains("Span", lowered.Types, StringComparer.Ordinal);
		Assert.Contains("Mark", lowered.Types, StringComparer.Ordinal);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Equal(4, session.Bag.ReadInt("Size"));
		Assert.Equal(9, session.Bag.ReadInt("Reach"));
		Assert.Equal(3, session.Bag.ReadInt("Tag"));
	}

	[Fact]
	public void Clipboard_and_share_lower_through_the_binding_registry()
	{
		const string bindings = """
			namespace NativePoc;
			[JniType("android/content/ClipboardManager")]
			static class DeviceClipboard
			{
				[JniMethod("setPrimaryClip", "(Ljava/lang/String;Ljava/lang/String;)V", Instance = true, Service = "clipboard")]
				public static void Set(string label, string text) { }
				[JniMethod("getPrimaryClip", "()Ljava/lang/String;")]
				public static string Get() => "";
			}
			[JniType("android/content/Intent")]
			static class DeviceShare
			{
				[JniMethod("ACTION_SEND", "(Ljava/lang/String;)V")]
				public static void Send(string text) { }
			}
			""";
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Note { get; set; } = "ready";
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Column(
					[
						new Text(state.Note) { Key = "note" },
						new Button("Copy", () => { DeviceClipboard.Set("bind", "pier"); state.Note = DeviceClipboard.Get(); platform.RequestRender(); }, "Copy") { Key = "copy" },
						new Button("Share", () => { DeviceShare.Send("pier"); state.Note = "shared"; platform.RequestRender(); }, "Share") { Key = "share" },
					]);
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, [bindings]);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!, (module, method, args) =>
		{
			Assert.Equal("jni", module);
			return method == "DeviceClipboard.Get" ? "pier" : "ok";
		});
		session.Tap("copy");
		Assert.Equal("pier", session.Bag.ReadString("Note"));
		Assert.Contains(session.Modules, line => line.StartsWith("jni.DeviceClipboard.Set", StringComparison.Ordinal));
		Assert.Contains(session.Modules, line => line.StartsWith("jni.DeviceClipboard.Get", StringComparison.Ordinal));
		session.Tap("share");
		Assert.Equal("shared", session.Bag.ReadString("Note"));
		Assert.Contains(session.Modules, line => line.StartsWith("jni.DeviceShare.Send", StringComparison.Ordinal));
	}

	[Fact]
	public void Every_referenced_type_is_in_the_program_or_reported()
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
			Assert.True(lowered.Ok, relative);
			var names = new HashSet<string>(StringComparer.Ordinal);
			CollectTypes(source, names);
			foreach (var companion in companions)
				CollectTypes(companion, names);
			var screen = StaticClass(source);
			if (screen is not null)
				names.Remove(screen);
			foreach (var name in names)
			{
				if (!Uses(source, name))
					continue;
				var recorded = lowered.Types.Contains(name, StringComparer.Ordinal);
				var reported = lowered.Reported.Any(diagnostic => diagnostic.Line > 0 && diagnostic.Message.Contains(name, StringComparison.Ordinal));
				Assert.True(recorded || reported, relative + " " + name);
			}
		}
	}

	static void CollectTypes(string source, HashSet<string> names)
	{
		foreach (var token in new[] { "class ", "record ", "enum ", "interface " })
		{
			var index = 0;
			while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
			{
				var start = index + token.Length;
				while (start < source.Length && char.IsWhiteSpace(source[start]))
					start++;
				if (source.AsSpan(start).StartsWith("class ") || source.AsSpan(start).StartsWith("struct "))
				{
					index = start;
					continue;
				}

				var end = start;
				while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == '_'))
					end++;
				if (end > start)
					names.Add(source[start..end]);
				index = end;
			}
		}
	}

	static string? StaticClass(string source)
	{
		const string token = "static class ";
		var index = source.IndexOf(token, StringComparison.Ordinal);
		if (index < 0)
			return null;
		var start = index + token.Length;
		var end = start;
		while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == '_'))
			end++;
		return end > start ? source[start..end] : null;
	}

	static bool Uses(string source, string name)
	{
		var index = 0;
		while ((index = source.IndexOf(name, index, StringComparison.Ordinal)) >= 0)
		{
			var before = index == 0 || (!char.IsLetterOrDigit(source[index - 1]) && source[index - 1] != '_');
			var afterAt = index + name.Length;
			var after = afterAt >= source.Length || (!char.IsLetterOrDigit(source[afterAt]) && source[afterAt] != '_');
			if (before && after)
				return true;
			index = afterAt;
		}

		return false;
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
