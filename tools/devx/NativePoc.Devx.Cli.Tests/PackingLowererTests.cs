using NativePoc;
using NativePoc.Devx;
using NativePoc.Packing;

namespace NativePoc.Devx.Tests;

public sealed class PackingLowererTests
{
	[Fact]
	public void The_committed_packing_screen_lowers_to_the_same_tree_and_the_same_actions()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var lowered = PackingLowerer.TryLower(source);
		Assert.Null(lowered.Reason);
		Assert.NotNull(lowered.Program);
		Assert.Equal(1, lowered.Program[5]);
		Assert.True(ScreenProgram.IsPacking(lowered.Program));
		Assert.Throws<InvalidOperationException>(() => ScreenProgram.Bind(lowered.Program));
		var bound = ScreenProgram.BindPacking(lowered.Program);
		Assert.Equal(ScreenProgram.SourceHash(source), bound.SourceHash);

		var screenState = PackingState.Seed();
		var programState = PackingState.Seed();
		var screenHost = new Host();
		var programHost = new Host();
		AssertSame(PackingScreen.Render(screenState, screenHost), bound.Render(new PackingProgramAdapter(programState, programHost)));

		ElementIds.Invoke(PackingScreen.Render(screenState, screenHost), "list/tent/packed", null);
		ElementIds.Invoke(bound.Render(new PackingProgramAdapter(programState, programHost)), "list/tent/packed", null);
		Assert.True(screenState.Items[0].Packed);
		Assert.True(programState.Items[0].Packed);
		AssertSame(PackingScreen.Render(screenState, screenHost), bound.Render(new PackingProgramAdapter(programState, programHost)));

		ElementIds.Invoke(PackingScreen.Render(screenState, screenHost), "list/tent/edit", null);
		ElementIds.Invoke(bound.Render(new PackingProgramAdapter(programState, programHost)), "list/tent/edit", null);
		Assert.Equal(PackingRoute.Editor, programState.Route);
		AssertSame(PackingScreen.Render(screenState, screenHost), bound.Render(new PackingProgramAdapter(programState, programHost)));
	}

	[Fact]
	public void A_row_label_a_text_field_and_a_reorder_lower()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var labeled = source.Replace("\"Packed \" + item.Name", "\"Held \" + item.Name", StringComparison.Ordinal);
		var labelProgram = PackingLowerer.TryLower(labeled).Program;
		Assert.NotNull(labelProgram);
		var labelTree = ScreenProgram.BindPacking(labelProgram).Render(new PackingProgramAdapter(PackingState.Seed(), new Host()));
		var row = Assert.IsType<Column>(Assert.IsType<Column>(labelTree).Children[2]);
		Assert.Equal("Held Canvas tent", Assert.IsType<Switch>(row.Children[1]).AccessibilityLabel);

		var lines = source.Split('\n');
		var noteIndex = Array.FindIndex(lines, line => line.Contains("new Text(item.Name)", StringComparison.Ordinal));
		var indent = lines[noteIndex][..(lines[noteIndex].Length - lines[noteIndex].TrimStart().Length)];
		var withNote = new List<string>(lines);
		withNote.Insert(noteIndex + 1, indent + "new Text(\"Note\") { Key = \"note\" },");
		var noted = PackingLowerer.TryLower(string.Join('\n', withNote)).Program;
		Assert.NotNull(noted);
		var notedRow = Assert.IsType<Column>(Assert.IsType<Column>(ScreenProgram.BindPacking(noted).Render(new PackingProgramAdapter(PackingState.Seed(), new Host()))).Children[2]);
		Assert.Equal("Note", Assert.IsType<Text>(notedRow.Children[1]).Value);
		Assert.Equal("note", notedRow.Children[1].Key);
		Assert.Equal(4, notedRow.Children.Count);

		(lines[noteIndex], lines[noteIndex + 2]) = (lines[noteIndex + 2], lines[noteIndex]);
		var swapped = PackingLowerer.TryLower(string.Join('\n', lines)).Program;
		Assert.NotNull(swapped);
		var swappedRow = Assert.IsType<Column>(Assert.IsType<Column>(ScreenProgram.BindPacking(swapped).Render(new PackingProgramAdapter(PackingState.Seed(), new Host()))).Children[2]);
		Assert.Equal("edit", swappedRow.Children[0].Key);
		Assert.Equal("name", swappedRow.Children[2].Key);
	}

	[Fact]
	public void A_new_member_is_refused_and_a_counter_program_is_not_packing()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var extra = source.Replace(
			"public static Element View(PackingItem item",
			"public static int Extra => 1;\n\tpublic static Element View(PackingItem item",
			StringComparison.Ordinal);
		var lowered = PackingLowerer.TryLower(extra);
		Assert.Null(lowered.Program);
		Assert.Contains("Extra", lowered.Reason);

		var screen = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), ScreenCompiler.RelativePath));
		var counter = ScreenLowerer.TryLower(screen).Program;
		Assert.NotNull(counter);
		Assert.False(ScreenProgram.IsPacking(counter));
		Assert.Throws<InvalidOperationException>(() => ScreenProgram.BindPacking(counter));
	}

	[Fact]
	public void A_changed_add_and_a_changed_save_prefix_reload_as_behavior()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var edited = source
			.Replace("state.DraftPacked = false;", "state.DraftPacked = true;", StringComparison.Ordinal)
			.Replace("\"item-\" + state.NextNumber", "\"kit-\" + state.NextNumber", StringComparison.Ordinal);
		var lowered = PackingLowerer.TryLower(edited);
		Assert.Null(lowered.Reason);
		var bound = ScreenProgram.BindPacking(lowered.Program!);
		var state = PackingState.Seed();
		var host = new PackingProgramAdapter(state, new Host());
		ElementIds.Invoke(bound.Render(host), "list/add", null);
		Assert.True(state.DraftPacked);
		Assert.Equal(PackingRoute.Editor, state.Route);
		state.DraftName = "Tarp";
		ElementIds.Invoke(bound.Render(host), "editor/save", null);
		Assert.Equal("kit-1", state.Items[^1].Id);
		Assert.Equal("Tarp", state.Items[^1].Name);
		Assert.True(state.Items[^1].Packed);
		Assert.Equal(PackingRoute.List, state.Route);
	}

	[Fact]
	public void Anything_past_toggle_rename_add_or_remove_is_refused()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var cleared = source.Replace(
			"state.EditingId = null;",
			"state.Items.Clear();\n\t\tstate.EditingId = null;",
			StringComparison.Ordinal);
		var lowered = PackingLowerer.TryLower(cleared);
		Assert.Null(lowered.Program);
		Assert.Equal("A packing action can toggle, rename, add, or remove.", lowered.Reason);
	}

	[Fact]
	public void A_note_field_lowers_and_a_second_name_field_is_refused()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var note = source.Replace(
			"{ Key = \"name\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,",
			"{ Key = \"name\" },\n\t\t\tnew TextField(\n\t\t\t\tstate.DraftNote,\n\t\t\t\tvalue =>\n\t\t\t\t{\n\t\t\t\t\tstate.DraftNote = value;\n\t\t\t\t\thost.RequestRender();\n\t\t\t\t},\n\t\t\t\t\"Note\",\n\t\t\t\t\"Note\")\n\t\t\t{ Key = \"note\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,",
			StringComparison.Ordinal);
		var lowered = PackingLowerer.TryLower(note);
		Assert.Null(lowered.Reason);
		var bound = ScreenProgram.BindPacking(lowered.Program!);
		var state = PackingState.Seed();
		var host = new PackingProgramAdapter(state, new Host());
		ElementIds.Invoke(bound.Render(host), "list/tent/edit", null);
		ElementIds.Invoke(bound.Render(host), "editor/note", "hem");
		Assert.Equal("hem", state.DraftNote);
		Assert.Equal("Canvas tent", state.DraftName);

		var again = source.Replace(
			"{ Key = \"name\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,",
			"{ Key = \"name\" },\n\t\t\tnew TextField(\n\t\t\t\tstate.DraftName,\n\t\t\t\tvalue =>\n\t\t\t\t{\n\t\t\t\t\tstate.DraftName = value;\n\t\t\t\t\thost.RequestRender();\n\t\t\t\t},\n\t\t\t\t\"Again\",\n\t\t\t\t\"Again\")\n\t\t\t{ Key = \"again\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,",
			StringComparison.Ordinal);
		var refused = PackingLowerer.TryLower(again);
		Assert.Null(refused.Program);
		Assert.Equal("The editor already has a name field.", refused.Reason);
	}

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
			default:
				throw new InvalidOperationException(expected.GetType().Name);
		}
	}

	sealed class Host : IPackingHost
	{
		public INativeServices Native { get; } = new Services();

		public void RequestRender()
		{
		}

		sealed class Services : INativeServices
		{
			public void CopyText(string label, string text)
			{
			}

			public void Pulse()
			{
			}

			public void Dispose()
			{
			}
		}
	}
}
