using System.Text;

namespace NativePoc.Devx;

internal sealed record HotReloadRule(string Change, bool Can, string Why, string? Source = null, string? Companion = null);

internal static class HotReloadCatalog
{
	public const string GpuDate = "2026-11-13";

	public const string GpuCommit = "0c6a958";

	public static IReadOnlyList<HotReloadRule> Rules { get; } =
	[
		new("Text, keys, and button labels", true, "The screen file lowers again and the same process applies the program.", Screen("""
			return new Text(state.Title) { Key = "title" };
		""")),
		new("A local function after the return", true, "Render hoists every local function before the rest of the body.", Screen("""
			return new Button("Go", () => Later(), "Go") { Key = "go" };
			void Later()
			{
				state.Title = "later";
			}
		""")),
		new("An expression-bodied local function nested in another", true, "Render hoists every local function, and an expression body lowers as that function.", Screen("""
			return new Button("Go", () => Outer(), "Go") { Key = "go" };
			void Outer()
			{
				void Inner() => state.Title = "nested";
				Inner();
			}
		""")),
		new("platform.OnOpen after the markup", true, "OnOpen is hoisted and runs when the screen id changes.", Screen("""
			return new Text(state.Title) { Key = "title" };
			platform.OnOpen(ScreenId, Open);
			void Open()
			{
				state.Title = "opened";
			}
		""")),
		new("await into a string local", true, "The result is a string slot, not a state field.", Screen("""
			async void Fill()
			{
				var saved = await Persist.Get(platform, "northroom.note");
				state.Title = saved;
			}
			Fill();
			return new Text(state.Title) { Key = "title" };
		""")),
		new("A JNI binding call in a button", true, "The call is the jni module through JniRegistry, or a diagnostic on that call.", Screen("""
			return new Button("Pulse", () => DeviceVibrator.Vibrate(20), "Pulse") { Key = "pulse" };
		"""), """
			namespace NativePoc.Bind
			{
				[JniType("android/os/Vibrator")]
				static partial class DeviceVibrator
				{
					[JniMethod("vibrate", "(J)V", Instance = true, Service = "vibrator")]
					public static partial void Vibrate(long milliseconds);
				}
			}
			"""),
		new("A PackingTelemetry increment", true, "The reload program calls the telemetry module. The C# method increments the same counter.", Screen("""
			return new Button("Go", () => { PackingTelemetry.FieldDeliveries++; }, "Go") { Key = "go" };
		""")),
		new("Add and remove list rows", true, "The action updates the record and the screen renders again.", """
			namespace NativePoc.Notes;
			public sealed class Item
			{
				public string Id { get; set; } = "";
				public string Name { get; set; } = "";
			}
			public sealed class NotesState
			{
				public List<Item> Items { get; }
			}
			public static class NotesScreen
			{
				public static Element Render(NotesState state, IPlatform platform)
				{
					var rows = new List<Element>();
					foreach (var item in state.Items)
						rows.Add(new Text(item.Name) { Key = item.Id });
					rows.Add(new Button("Add", () => state.Items.Add(new Item { Id = "2", Name = "Bread" }), "Add") { Key = "add" });
					rows.Add(new Button("Drop", () => state.Items.RemoveAt(0), "Drop") { Key = "drop" });
					return new Column(rows) { Key = "notes" };
				}
			}
			"""),
		new("state.Title.Normalize()", false, "A call the subset does not name is a diagnostic. The program does not skip it.", Screen("""
			return new Button("Go", () => { state.Title = state.Title.Normalize(); }, "Go") { Key = "go" };
		""")),
		new("await of a call the subset does not name", false, "Await is only the platform calls the subset lists, stored in a string property or a string local.", Screen("""
			async void Load()
			{
				state.Title = await Weird.Get(platform);
			}
			Load();
			return new Text(state.Title) { Key = "title" };
		""")),
		new("A new type", false, "The running binary was compiled without it. Save a rude edit and relaunch.", Screen("""
			var box = new StringBuilder();
			return new Text(state.Title) { Key = "title" };
		""")),
		new("A change under src/NativePoc.Core or src/NativePoc.Android", false, "That is a rude edit. The listener prints restart required."),
	];

	public static string Markdown()
	{
		var builder = new StringBuilder();
		builder.Append("# What hot reload can and cannot change\n\n");
		builder.Append("This table is generated from the subset rules. A test lowers each row that has a screen and checks this file against that generator, so the table cannot drift from the rules.\n\n");
		builder.Append("| Change | Hot reload | Why |\n");
		builder.Append("| --- | --- | --- |\n");
		foreach (var rule in Rules)
		{
			builder.Append("| ").Append(rule.Change).Append(" | ").Append(rule.Can ? "yes" : "no").Append(" | ").Append(rule.Why).Append(" |\n");
		}

		builder.Append("\nA force-stop starts a new process and a new token file. The watcher reads that file after a rejected token or a dead socket and applies the next edit on the new process. The watch loop stays up.\n");
		builder.Append("\n## Loaded-host relatives\n\n");
		builder.Append("These numbers are relatives. The host was already loaded. They are not a cold start and they are not a SwiftShader run.\n\n");
		builder.Append("| | |\n");
		builder.Append("| --- | --- |\n");
		builder.Append("| Date | ").Append(GpuDate).Append(" |\n");
		builder.Append("| Commit | ").Append(GpuCommit).Append(" |\n");
		builder.Append("| Machine | Mac arm64 emulator, API 36, host GLES via the Apple GPU |\n");
		builder.Append("| Shelf grid scroll frame p50/p90/p99 | 31.5 / 57.2 / 101.0 ms |\n");
		builder.Append("| Java list floor frame p50/p90/p99 | 27.8 / 49.3 / 113.2 ms |\n");
		builder.Append("| Janky median | 6.7% on both |\n");
		builder.Append("| Startup home-floor delta median | 53 ms |\n");
		builder.Append("| Shelf deep link delta | 71 ms |\n");
		builder.Append("\nThe scroll gap was the renderer. Scroll was not re-run on SwiftShader.\n");
		return builder.ToString();
	}

	static string Screen(string body) =>
		"""
		namespace NativePoc.Notes;
		public sealed class NotesState
		{
			public string Title { get; set; } = "";
			public string Id { get; set; } = "piece-1";
			public string Draft { get; set; } = "";
		}
		public static class NotesScreen
		{
			public const string ScreenId = "notes";
			public static Element Render(NotesState state, IPlatform platform)
			{
		""" + body + """
			}
		}
		""";
}
