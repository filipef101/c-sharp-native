using NativePoc;
using NativePoc.Packing;

namespace NativePoc.Devx.Tests;

public sealed class PackingLowererTests
{
	[Fact]
	public void The_committed_packing_screen_lowers_to_the_same_tree_and_actions()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, Message(lowered));
		Assert.True(ScreenProgram.IsRecord(lowered.Program));
		var model = new PackingModel();
		var platform = new Host();
		AssertSame(PackingScreen.Render(model, platform), Render(lowered.Program!, model, platform));

		var screen = new PackingModel();
		var bag = Seed(new PackingModel());
		ElementIds.Invoke(PackingScreen.Render(screen, new Host()), "scroll/list/tent/packed", null);
		ElementIds.Invoke(Render(lowered.Program!, bag), "scroll/list/tent/packed", null);
		Assert.True(screen.Items[0].Packed);
		Assert.Equal("true", bag.Cell("Items", "tent", 0, 2));
		AssertSame(PackingScreen.Render(screen, new Host()), RecordProgram.Bind(lowered.Program!, bag).Render(new Host()));

		ElementIds.Invoke(PackingScreen.Render(screen, new Host()), "scroll/list/tent/edit", null);
		ElementIds.Invoke(RecordProgram.Bind(lowered.Program!, bag).Render(new Host()), "scroll/list/tent/edit", null);
		Assert.True(screen.Editing);
		Assert.Equal("Canvas tent", screen.DraftName);
		Assert.True(bag.ReadBool("Editing"));
		Assert.Equal("Canvas tent", bag.ReadString("DraftName"));
		AssertSame(PackingScreen.Render(screen, new Host()), RecordProgram.Bind(lowered.Program!, bag).Render(new Host()));
	}

	[Fact]
	public void A_row_label_a_note_and_a_swap_lower_and_an_extra_member_is_refused()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var labeled = source.Replace("new Button(\"Edit\",", "new Button(\"Open\",", StringComparison.Ordinal);
		var labelProgram = SubsetLowerer.TryLower(labeled);
		Assert.True(labelProgram.Ok, Message(labelProgram));
		var labelTree = Render(labelProgram.Program!, new PackingModel(), new Host());
		Assert.Equal("Open", Assert.IsType<Button>(ElementIds.Require(labelTree, "scroll/list/tent/edit")).Title);

		var noted = source.Replace(
			"{ Key = \"name\" },",
			"{ Key = \"name\" },\n\t\t\t\tnew TextField(\n\t\t\t\t\tstate.DraftNote,\n\t\t\t\t\tvalue =>\n\t\t\t\t\t{\n\t\t\t\t\t\tstate.DraftNote = value;\n\t\t\t\t\t\thost.RequestRender();\n\t\t\t\t\t},\n\t\t\t\t\t\"Note\",\n\t\t\t\t\t\"Note\")\n\t\t\t\t{ Key = \"note\" },",
			StringComparison.Ordinal);
		var noteProgram = SubsetLowerer.TryLower(noted);
		Assert.True(noteProgram.Ok, Message(noteProgram));
		var editing = new PackingModel { Editing = true };
		Assert.Equal("note", ElementIds.Require(Render(noteProgram.Program!, editing, new Host()), "editor/note").Key);

		var lines = source.Split('\n');
		for (var index = 0; index < lines.Length - 2; index++)
		{
			if (!lines[index].Contains("new Text(item.Name)", StringComparison.Ordinal))
				continue;
			(lines[index], lines[index + 2]) = (lines[index + 2], lines[index]);
			break;
		}

		var swapped = SubsetLowerer.TryLower(string.Join('\n', lines));
		Assert.True(swapped.Ok, Message(swapped));
		var row = Assert.IsType<Column>(ElementIds.Require(Render(swapped.Program!, new PackingModel(), new Host()), "scroll/list/tent"));
		Assert.IsType<Button>(row.Children[0]);
		Assert.IsType<Text>(row.Children[2]);

		var extra = source.Replace(
			"public static Element Render(PackingModel state",
			"public static int Extra => 1;\n\tpublic static Element Render(PackingModel state",
			StringComparison.Ordinal);
		var refused = SubsetLowerer.TryLower(extra);
		Assert.False(refused.Ok);
		Assert.Contains("Extra", refused.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Begin_add_keeps_the_draft_packed_literal_and_save_appends_a_row()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var edited = source.Replace("state.DraftPacked = false;", "state.DraftPacked = true;", StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(edited);
		Assert.True(lowered.Ok, Message(lowered));
		var bag = Seed(new PackingModel());
		ElementIds.Invoke(Render(lowered.Program!, bag), "scroll/list/add", null);
		Assert.True(bag.ReadBool("DraftPacked"));
		Assert.True(bag.ReadBool("Editing"));
		bag.WriteString("DraftName", "Lantern");
		ElementIds.Invoke(RecordProgram.Bind(lowered.Program!, bag).Render(new Host()), "editor/save", null);
		Assert.False(bag.ReadBool("Editing"));
		Assert.Equal("Lantern", bag.Cell("Items", "item-1", 0, 1));
		Assert.Equal("true", bag.Cell("Items", "item-1", 0, 2));
	}

	static Element Render(byte[] program, PackingModel model, IPlatform platform) =>
		RecordProgram.Bind(program, Seed(model)).Render(platform);

	static Element Render(byte[] program, RecordProgram.Bag bag) =>
		RecordProgram.Bind(program, bag).Render(new Host());

	static RecordProgram.Bag Seed(PackingModel model)
	{
		var bag = new RecordProgram.Bag();
		bag.SeedBool("Editing", model.Editing);
		bag.SeedString("EditingId", model.EditingId);
		bag.SeedString("DraftName", model.DraftName);
		bag.SeedString("DraftNote", model.DraftNote);
		bag.SeedBool("DraftPacked", model.DraftPacked);
		bag.SeedInt("NextNumber", model.NextNumber);
		var rows = new List<string[]>();
		foreach (var item in model.Items)
			rows.Add([item.Id, item.Name, item.Packed ? "true" : "false"]);
		bag.SeedList("Items", rows);
		return bag;
	}

	static string Message(SubsetResult result) =>
		result.Diagnostics.Count == 0 ? "" : result.Diagnostics[0].Id + " " + result.Diagnostics[0].Message + " line " + result.Diagnostics[0].Line;

	static void AssertSame(Element expected, Element actual)
	{
		Assert.Equal(expected.Key, actual.Key);
		switch (expected)
		{
			case Text text:
				Assert.Equal(text.Value, Assert.IsType<Text>(actual).Value);
				return;
			case TextField field:
				var otherField = Assert.IsType<TextField>(actual);
				Assert.Equal(field.Value, otherField.Value);
				Assert.Equal(field.Placeholder, otherField.Placeholder);
				Assert.Equal(field.AccessibilityLabel, otherField.AccessibilityLabel);
				return;
			case Button button:
				var otherButton = Assert.IsType<Button>(actual);
				Assert.Equal(button.Title, otherButton.Title);
				Assert.Equal(button.AccessibilityLabel, otherButton.AccessibilityLabel);
				return;
			case Switch toggle:
				var otherToggle = Assert.IsType<Switch>(actual);
				Assert.Equal(toggle.Value, otherToggle.Value);
				Assert.Equal(toggle.AccessibilityLabel, otherToggle.AccessibilityLabel);
				return;
			case Column column:
				var otherColumn = Assert.IsType<Column>(actual);
				Assert.Equal(column.Children.Count, otherColumn.Children.Count);
				for (var index = 0; index < column.Children.Count; index++)
					AssertSame(column.Children[index], otherColumn.Children[index]);
				return;
			case ScrollView scroll:
				AssertSame(scroll.Child, Assert.IsType<ScrollView>(actual).Child);
				return;
			default:
				throw new InvalidOperationException(expected.GetType().Name);
		}
	}

	sealed class Host : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void CopyText(string label, string text)
		{
		}

		public void Pulse()
		{
		}
	}
}
