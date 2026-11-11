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
			"new Text(state.Title) { Key = \"title\" }",
			"new Text(state.Title) { Key = \"title\", Style = new Style { FontSize = 28 } }",
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

		var multiplied = handler.Replace("state.Count++;", "state.Count *= 2;", StringComparison.Ordinal);
		var refused = SubsetLowerer.TryLower(multiplied);
		Assert.False(refused.Ok);
		Assert.Equal(SubsetLowerer.BadLambda, refused.Diagnostics[0].Id);
		var line = DeveloperRefusal.Line("samples/hello/HelloScreen.cs", refused.Diagnostics[0]);
		Assert.Contains("samples/hello/HelloScreen.cs:", line, StringComparison.Ordinal);
		Assert.Contains("subset.bad-lambda:", line, StringComparison.Ordinal);
		Assert.Contains("A screen action can update state, branch on it, change a list, or call the platform.", line, StringComparison.Ordinal);
		Assert.Equal("restarting", DeveloperRefusal.FollowUp(true, false));
		Assert.Equal("pass --restart-on-rude to relaunch and keep state", DeveloperRefusal.FollowUp(false, true));
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

		var trimmed = SubsetLowerer.TryLower(hello.Replace("state.Count++;", "state.Title = state.Title.Trim();", StringComparison.Ordinal));
		Assert.False(trimmed.Ok);
		Assert.Equal(SubsetLowerer.BadLambda, trimmed.Diagnostics[0].Id);
		var loop = SubsetLowerer.TryLower(hello.Replace("state.Count++;", "while (state.Count < 3) state.Count++;", StringComparison.Ordinal));
		Assert.False(loop.Ok);
		Assert.Equal(SubsetLowerer.BadLambda, loop.Diagnostics[0].Id);
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
