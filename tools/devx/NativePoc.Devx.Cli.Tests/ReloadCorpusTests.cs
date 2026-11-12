using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class ReloadCorpusTests
{
	[Fact]
	public void Developer_edits_lower_or_name_the_refusal()
	{
		var hello = NewLoop.Template("hello");
		AssertLower(hello);

		var text = hello.Replace("= \"Hello\";", "= \"Hello again\";", StringComparison.Ordinal);
		Assert.NotEqual(hello, text);
		AssertLower(text);

		var style = text.Replace(
			"FontSize = 22",
			"FontSize = 28",
			StringComparison.Ordinal);
		Assert.NotEqual(text, style);
		AssertLower(style);

		var handler = style.Replace(
			"state.Count++;",
			"state.Count++; state.Title = \"Added\";",
			StringComparison.Ordinal);
		Assert.NotEqual(style, handler);
		AssertLower(handler);

		var assigned = handler.Replace("state.Count++;", "state.Count += 1;", StringComparison.Ordinal);
		var added = Open(assigned);
		added.Tap("hello/add");
		Assert.Contains("Count 1", added.Tree(), StringComparison.Ordinal);

		var multiplied = Open(handler.Replace("state.Count++;", "state.Count = 3 * 4;", StringComparison.Ordinal));
		multiplied.Tap("hello/add");
		Assert.Contains("Count 12", multiplied.Tree(), StringComparison.Ordinal);

		var normalized = handler.Replace("state.Count++;", "state.Title = state.Title.Normalize();", StringComparison.Ordinal);
		var refused = SubsetLowerer.TryLower(normalized);
		Assert.False(refused.Ok);
		Assert.Equal(SubsetLowerer.BadLambda, refused.Diagnostics[0].Id);
		var line = DeveloperRefusal.Line("samples/hello/HelloScreen.cs", refused.Diagnostics[0]);
		Assert.Contains("samples/hello/HelloScreen.cs:", line, StringComparison.Ordinal);
		Assert.Contains("subset.bad-lambda:", line, StringComparison.Ordinal);
		Assert.Contains("A screen action can update state, branch, loop, or call a method compiled into the binary.", line, StringComparison.Ordinal);
		Assert.Contains("Normalize()", line, StringComparison.Ordinal);
		Assert.Equal("restarting", DeveloperRefusal.FollowUp(true, false));
		Assert.Equal("Fix the screen and save again. A relaunch loads this same file.", DeveloperRefusal.FollowUp(false, true));
		Assert.Null(DeveloperRefusal.FollowUp(false, false));

		var renamed = hello.Replace("Count", "Total", StringComparison.Ordinal);
		Assert.DoesNotContain("Count", renamed, StringComparison.Ordinal);
		AssertLower(renamed);

		var half = hello.Replace("public int Count { get; set; }", "public int Total { get; set; }", StringComparison.Ordinal);
		var halfRefused = SubsetLowerer.TryLower(half);
		Assert.False(halfRefused.Ok);
		Assert.Equal(SubsetLowerer.UnknownMember, halfRefused.Diagnostics[0].Id);

		var broken = hello.Replace("public const string ScreenId = \"hello\";", "public const string ScreenId = \"hello\"", StringComparison.Ordinal);
		var parsed = SubsetLowerer.TryLower(broken);
		Assert.False(parsed.Ok);
		Assert.Equal(SubsetLowerer.NotAScreen, parsed.Diagnostics[0].Id);
		AssertLower(hello);

		var pushed = hello.Replace("state.Count++;", "state.Count++; platform.Push(\"settings\");", StringComparison.Ordinal);
		AssertLower(pushed);

		var root = Directory.CreateTempSubdirectory("cnative-corpus-").FullName;
		NewLoop.Write(root, "hello");
		NewLoop.Write(root, "notes");
		Assert.Equal(
			new[] { "samples/hello/HelloScreen.cs", "samples/notes/NotesScreen.cs" },
			NewLoop.AppScreens(root));
		AssertLower(File.ReadAllText(Path.Combine(root, "samples", "notes", "NotesScreen.cs")));

		var packing = RepoFile("samples", "NativePoc.Packing", "PackingScreen.cs");
		var template = packing.Replace("new Text(item.Name)", "new Text(item.Name + \" item\")", StringComparison.Ordinal);
		Assert.NotEqual(packing, template);
		AssertLower(template);
		AssertLower(packing);
	}

	[Fact]
	public void Handlers_run_the_widened_actions()
	{
		var hello = NewLoop.Template("hello");
		Tap(hello.Replace("state.Count++;", "state.Count += 1;", StringComparison.Ordinal), "Count 1");
		Tap(hello.Replace("state.Count++;", "state.Count -= 1;", StringComparison.Ordinal), "Count -1");
		Tap(hello.Replace("state.Count++;", "state.Count--;", StringComparison.Ordinal), "Count -1");
		Tap(hello.Replace("state.Count++;", "++state.Count;", StringComparison.Ordinal), "Count 1");
		Tap(hello.Replace("state.Count++;", "state.Count = state.Count + 2;", StringComparison.Ordinal), "Count 2");

		var titled = hello.Replace("state.Count++;", "state.Title = state.Title + \"!\";", StringComparison.Ordinal);
		Tap(titled, "Hello!");
		Tap(hello.Replace("state.Count++;", "state.Title += \"!\";", StringComparison.Ordinal), "Hello!");
		Tap(hello.Replace("state.Count++;", "state.Title = $\"{state.Title} {state.Count}\";", StringComparison.Ordinal), "Hello 0");
		Tap(hello.Replace("state.Count++;", "state.Title = state.Count == 0 ? \"Zero\" : \"More\";", StringComparison.Ordinal), "Zero");

		var open = hello.Replace(
			"public int Count { get; set; }",
			"public int Count { get; set; }\n\tpublic bool Open { get; set; }",
			StringComparison.Ordinal);
		var toggled = Open(open.Replace("state.Count++;", "state.Open = !state.Open; state.Title = state.Open ? \"Yes\" : \"No\";", StringComparison.Ordinal));
		toggled.Tap("hello/add");
		Assert.Contains("Yes", toggled.Tree(), StringComparison.Ordinal);
		toggled.Tap("hello/add");
		Assert.Contains("No", toggled.Tree(), StringComparison.Ordinal);

		var branched = open.Replace(
			"state.Count++;",
			"if (state.Open) state.Count += 5; else state.Open = !state.Open;",
			StringComparison.Ordinal);
		var branch = Open(branched);
		branch.Tap("hello/add");
		Assert.Contains("Count 0", branch.Tree(), StringComparison.Ordinal);
		branch.Tap("hello/add");
		Assert.Contains("Count 5", branch.Tree(), StringComparison.Ordinal);

		var local = hello.Replace(
			"state.Count++;",
			"var next = state.Count + 1; var label = state.Title + \" \" + next; state.Count = next; state.Title = label;",
			StringComparison.Ordinal);
		var locals = Open(local);
		locals.Tap("hello/add");
		Assert.Contains("Hello 1", locals.Tree(), StringComparison.Ordinal);
		Assert.Contains("Count 1", locals.Tree(), StringComparison.Ordinal);

		var list = ListScreen("state.Items.Add(new Note { Name = \"Lantern\", Packed = true });");
		var added = Open(list);
		added.Tap("notes/add");
		Assert.Equal("Lantern", added.Bag.Rows("Items")[0][0]);
		Assert.Equal("true", added.Bag.Rows("Items")[0][1]);
		var removed = Open(ListScreen("state.Items.Add(new Note { Name = \"Lantern\" }); state.Items.RemoveAt(0);"));
		removed.Tap("notes/add");
		Assert.Empty(removed.Bag.Rows("Items"));
		var cleared = Open(ListScreen("state.Items.Add(new Note { Name = \"Lantern\" }); state.Items.Clear();"));
		cleared.Tap("notes/add");
		Assert.Empty(cleared.Bag.Rows("Items"));
		var missing = Open(ListScreen("state.Items.RemoveAt(4);"));
		missing.Tap("notes/add");
		Assert.Empty(missing.Bag.Rows("Items"));

		var haptic = Open(hello.Replace("state.Count++;", "Haptics.Confirm(platform); state.Count += 1;", StringComparison.Ordinal));
		haptic.Tap("hello/add");
		Assert.Contains("haptics.Confirm", haptic.Modules);
		Assert.Contains("Count 1", haptic.Tree(), StringComparison.Ordinal);
		var share = Open(hello.Replace("state.Count++;", "Share.Text(platform, state.Title);", StringComparison.Ordinal));
		share.Tap("hello/add");
		Assert.Contains("share.Text", share.Modules);
		var clipboard = Open(hello.Replace("state.Count++;", "Clipboard.Copy(platform, \"form\", state.Title);", StringComparison.Ordinal));
		clipboard.Tap("hello/add");
		Assert.Contains("clipboard.Copy", clipboard.Modules);
		var storage = Open(hello.Replace("state.Count++;", "Storage.Set(platform, \"title\", state.Title);", StringComparison.Ordinal));
		storage.Tap("hello/add");
		Assert.Contains("storage.Set", storage.Modules);
		var theme = Open(open.Replace("state.Count++;", "Theme.Mode(platform, state.Open);", StringComparison.Ordinal));
		theme.Tap("hello/add");
		Assert.Contains("theme.Mode", theme.Modules);

		var pushed = Open(hello.Replace("state.Count++;", "platform.Push(\"settings\");", StringComparison.Ordinal));
		pushed.Tap("hello/add");
		Assert.Equal("settings", Assert.Single(pushed.Stack));
		var popped = Open(hello.Replace("state.Count++;", "platform.Push(\"settings\"); platform.Pop();", StringComparison.Ordinal));
		popped.Tap("hello/add");
		Assert.Empty(popped.Stack);

		Tap(hello.Replace("state.Count++;", "state.Title = \"  hi  \".Trim();", StringComparison.Ordinal), "hi");
		Tap(hello.Replace("state.Count++;", "while (state.Count < 3) state.Count++;", StringComparison.Ordinal), "Count 3");
	}

	[Fact]
	public void The_action_vm_runs_arithmetic_loops_and_calls()
	{
		var hello = NewLoop.Template("hello");
		Tap(hello.Replace("state.Count++;", "state.Count = 8 / 3;", StringComparison.Ordinal), "Count 2");
		Tap(hello.Replace("state.Count++;", "state.Count = 7 % 2;", StringComparison.Ordinal), "Count 1");
		Tap(hello.Replace("state.Count++;", "state.Count = 4; state.Count *= 2;", StringComparison.Ordinal), "Count 8");
		Tap(hello.Replace("state.Count++;", "state.Title = state.Count < 1 ? \"yes\" : \"no\";", StringComparison.Ordinal), "yes");
		Tap(hello.Replace("state.Count++;", "state.Title = state.Count > 1 && false ? \"no\" : \"yes\";", StringComparison.Ordinal), "yes");
		Tap(hello.Replace("state.Count++;", "state.Count = 3; state.Title = $\"{state.Count * 2}\";", StringComparison.Ordinal), "6");
		Tap(hello.Replace("state.Count++;", "var n = state.Count++; state.Title = $\"{n}\";", StringComparison.Ordinal), "0");
		Tap(hello.Replace("state.Count++;", "var n = ++state.Count; state.Title = $\"{n}\";", StringComparison.Ordinal), "1");
		Tap(hello.Replace("state.Count++;", "state.Title = \"ab\".ToUpper();", StringComparison.Ordinal), "AB");
		Tap(hello.Replace("state.Count++;", "state.Title = \"AB\".ToLower();", StringComparison.Ordinal), "ab");
		Tap(hello.Replace("state.Count++;", "state.Title = \"hello\".Contains(\"ell\") ? \"yes\" : \"no\";", StringComparison.Ordinal), "yes");
		Tap(hello.Replace("state.Count++;", "state.Title = \"hello\".StartsWith(\"he\") ? \"yes\" : \"no\";", StringComparison.Ordinal), "yes");
		Tap(hello.Replace("state.Count++;", "state.Title = \"hello\".Substring(1, 2);", StringComparison.Ordinal), "el");
		Tap(hello.Replace("state.Count++;", "state.Count = \"hello\".Length;", StringComparison.Ordinal), "Count 5");
		Tap(hello.Replace("state.Count++;", "state.Count = Math.Abs(-4);", StringComparison.Ordinal), "Count 4");
		Tap(hello.Replace("state.Count++;", "state.Count = Math.Min(3, 1);", StringComparison.Ordinal), "Count 1");
		Tap(hello.Replace("state.Count++;", "state.Count = Math.Max(3, 1);", StringComparison.Ordinal), "Count 3");
		Tap(hello.Replace("state.Count++;", "state.Count = (int)Math.Round(1.6);", StringComparison.Ordinal), "Count 2");
		Tap(hello.Replace("state.Count++;", "var x = 1.5 * 4.0; state.Count = (int)x;", StringComparison.Ordinal), "Count 6");
		Tap(hello.Replace("state.Count++;", "state.Title = \"  Hello  \".Trim();", StringComparison.Ordinal), "Hello");
		Tap(hello.Replace("state.Count++;", "string label = null; state.Title = label ?? \"kept\";", StringComparison.Ordinal), "kept");
		Tap(hello.Replace("state.Count++;", "string label = null; state.Title = label?.Trim() ?? \"empty\";", StringComparison.Ordinal), "empty");
		Tap(hello.Replace("state.Count++;", "state.Title = \"12\"; state.Count = int.Parse(state.Title);", StringComparison.Ordinal), "Count 12");
		var parsed = Open(hello.Replace("state.Count++;", "if (int.TryParse(\"9\", out var n)) state.Count = n;", StringComparison.Ordinal));
		parsed.Tap("hello/add");
		Assert.Contains("Count 9", parsed.Tree(), StringComparison.Ordinal);

		var summed = Open(ListScreen("state.Items.Add(new Note { Name = \"2\" }); state.Items.Add(new Note { Name = \"4\" }); var sum = 0; foreach (var item in state.Items) sum += int.Parse(item.Name); state.Count = sum;"));
		summed.Tap("notes/add");
		Assert.Equal(6, summed.Bag.ReadInt("Count"));
		var removed = Open(ListScreen("state.Items.Add(new Note { Name = \"Lantern\" }); state.Items.Add(new Note { Name = \"Rope\" }); foreach (var item in state.Items) if (item.Name == \"Lantern\") state.Items.Remove(item);"));
		removed.Tap("notes/add");
		Assert.Null(ActionLog.LastStop);
		Assert.Equal("Rope", Assert.Single(removed.Bag.Rows("Items"))[0]);
		var found = Open(ListScreen("state.Items.Add(new Note { Name = \"Lantern\" }); var seen = false; var at = -1; foreach (var item in state.Items) { if (state.Items.Contains(item)) seen = true; at = state.Items.IndexOf(item); } state.Count = state.Items.Count; state.Open = seen; state.Added = at;"));
		found.Tap("notes/add");
		Assert.Equal(1, found.Bag.ReadInt("Count"));
		Assert.True(found.Bag.ReadBool("Open"));
		Assert.Equal(0, found.Bag.ReadInt("Added"));

		var looped = Open(hello.Replace("state.Count++;", "var n = 0; for (var i = 0; i < 1000; i++) n++; state.Count = n;", StringComparison.Ordinal));
		looped.Tap("hello/add");
		Assert.Contains("Count 1000", looped.Tree(), StringComparison.Ordinal);
		Assert.True(ActionLog.LastSteps < RecordProgram.ActionStepBudget);
		Assert.Null(ActionLog.LastStop);

		var stopped = Open(hello.Replace("state.Count++;", "while (true) state.Count += 1;", StringComparison.Ordinal));
		stopped.Tap("hello/add");
		Assert.Equal("action stopped after 100000 steps", ActionLog.LastStop);
		Assert.True(stopped.Bag.ReadInt("Count") > 0);
		var divided = Open(hello.Replace("state.Count++;", "state.Count = 1 / 0;", StringComparison.Ordinal));
		divided.Tap("hello/add");
		Assert.Equal("Attempted to divide by zero.", ActionLog.LastStop);
	}

	[Fact]
	public void A_notes_count_does_not_inherit_the_counter()
	{
		var notes = Lowered(NewLoop.Template("notes"));
		Assert.Equal("notes", RecordProgram.ScreenId(notes));
		Assert.False(RecordProgram.SharesAppCounter(notes));
		Assert.False(RecordProgram.SharesPacking(notes));
		var notesBag = new RecordProgram.Bag();
		RecordProgram.SeedAppCounter(notes, notesBag, 5, "Ada", true, true);
		Assert.False(notesBag.TryReadInt("Count", out _));
		var notesBound = RecordProgram.Bind(notes, new RecordProgram.Bag());
		notesBound.State.WriteInt("Count", 9);
		Assert.False(RecordProgram.PublishAppCounter(notesBound, 5, "Ada", true, true, out var count, out var name, out var greeting, out var plain));
		Assert.Equal(5, count);
		Assert.Equal("Ada", name);
		Assert.True(greeting);
		Assert.True(plain);

		var counter = Lowered(RepoFile("src", "NativePoc.Screen", "Screen.cs"));
		Assert.Equal(RecordProgram.CounterScreenId, RecordProgram.ScreenId(counter));
		Assert.True(RecordProgram.SharesAppCounter(counter));
		var counterBag = new RecordProgram.Bag();
		RecordProgram.SeedAppCounter(counter, counterBag, 5, "Ada", true, false);
		Assert.Equal(5, counterBag.ReadInt("Count"));
		Assert.Equal("Ada", counterBag.ReadString("Name"));
		var counterBound = RecordProgram.Bind(counter, counterBag);
		Assert.True(RecordProgram.PublishAppCounter(counterBound, 0, "", false, false, out count, out name, out greeting, out plain));
		Assert.Equal(5, count);
		Assert.Equal("Ada", name);
		Assert.True(greeting);
		Assert.False(plain);

		var packing = Lowered(RepoFile("samples", "NativePoc.Packing", "PackingScreen.cs"));
		Assert.True(RecordProgram.SharesPacking(packing));
		Assert.False(RecordProgram.SharesAppCounter(packing));
		Assert.Equal(ScreenQuery.Reset, ScreenQuery.Op(ScreenQuery.Ask(ScreenQuery.Reset)));
	}

	[Fact]
	public void An_initializer_edit_names_the_kept_value()
	{
		var hello = Lowered(NewLoop.Template("hello"));
		var bag = new RecordProgram.Bag();
		Assert.Equal("", RecordProgram.KeptNote(hello, bag));
		RecordProgram.Bind(hello, bag);
		var edited = Lowered(NewLoop.Template("hello").Replace("= \"Hello\";", "= \"Hello again\";", StringComparison.Ordinal));
		Assert.Equal("state kept Title=\"Hello\" (initializer \"Hello again\"). cnative reset-state", RecordProgram.KeptNote(edited, bag));
		bag.WriteString("Title", "Hello again");
		Assert.Equal("", RecordProgram.KeptNote(edited, bag));
	}

	static void Tap(string source, string expected)
	{
		var session = Open(source);
		session.Tap(session.Tree().Contains("hello/", StringComparison.Ordinal) ? "hello/add" : "notes/add");
		Assert.Contains(expected, session.Tree(), StringComparison.Ordinal);
	}

	static ScreenSession Open(string source) => ScreenSession.Open(Lowered(source));

	static byte[] Lowered(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
		return lowered.Program!;
	}

	static string ListScreen(string handler)
	{
		return """
namespace NativePoc.Notes;

public sealed class Note
{
	public string Name { get; set; } = "";
	public bool Packed { get; set; }
}

public sealed class NotesState
{
	public List<Note> Items { get; } = [];
	public int Count { get; set; }
	public int Added { get; set; }
	public bool Open { get; set; }
}

public static class NotesScreen
{
	public const string ScreenId = "notes";

	public static Element Render(NotesState state, IPlatform platform)
	{
		return new Column(
			new Button("Add", () => { __HANDLER__ platform.RequestRender(); }, "Add") { Key = "add" }
		) { Key = "notes" };
	}
}
""".Replace("__HANDLER__", handler, StringComparison.Ordinal);
	}

	static void AssertLower(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
	}

	static string RepoFile(params string[] parts)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
			{
				var path = dir.FullName;
				foreach (var part in parts)
					path = Path.Combine(path, part);
				return File.ReadAllText(path);
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}
}
