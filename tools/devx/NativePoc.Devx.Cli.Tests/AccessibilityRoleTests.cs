using NativePoc;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class AccessibilityRoleTests
{
	[Fact]
	public void Counter_notes_note_and_feed_have_heading_button_field_and_list_roles()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var counter = Render(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"));
		var counterColumn = Assert.IsType<Column>(counter);
		Assert.Equal(22, Assert.IsType<Text>(counterColumn.Children[0]).Style!.Value.FontSize);
		Assert.Equal("textfield", ScreenSnapshot.Role(counterColumn.Children[1]));
		Assert.Equal("button", ScreenSnapshot.Role(counterColumn.Children[2]));

		var notes = Render(Path.Combine(root, "samples", "notes", "NotesScreen.cs"));
		Assert.Equal(22, Assert.IsType<Text>(ElementIds.Require(notes, "scroll/list/heading")).Style!.Value.FontSize);
		Assert.Equal("textfield", ScreenSnapshot.Role(ElementIds.Require(notes, "scroll/list/draft")));
		Assert.Equal("button", ScreenSnapshot.Role(ElementIds.Require(notes, "scroll/list/add")));

		var note = Render(Path.Combine(root, "samples", "note", "NoteScreen.cs"));
		Assert.Equal(22, Assert.IsType<Text>(ElementIds.Require(note, "note/heading")).Style!.Value.FontSize);
		Assert.Equal("textfield", ScreenSnapshot.Role(ElementIds.Require(note, "note/title")));
		Assert.Equal("button", ScreenSnapshot.Role(ElementIds.Require(note, "note/save")));

		var feed = Render(Path.Combine(root, "samples", "feed", "FeedScreen.cs"));
		Assert.Equal(22, Assert.IsType<Text>(ElementIds.Require(feed, "feed/heading")).Style!.Value.FontSize);
		Assert.Equal("button", ScreenSnapshot.Role(ElementIds.Require(feed, "feed/refresh")));
		var list = Assert.IsType<ListView>(ElementIds.Require(feed, "feed/window"));
		Assert.Equal("list", ScreenSnapshot.Role(list));
		var row = Assert.IsType<Column>(Assert.IsType<RecordProgram.WindowList>(list.Rows)[0]);
		Assert.Equal("image", ScreenSnapshot.Role(row.Children[1]));
	}

	static Element Render(string path)
	{
		var lowered = SubsetLowerer.TryLower(File.ReadAllText(path), originFile: path);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		return RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).Render(new Quiet());
	}

	sealed class Quiet : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}
}
