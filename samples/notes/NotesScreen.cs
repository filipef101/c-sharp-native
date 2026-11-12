namespace NativePoc.Notes;

public sealed record NoteItem
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public bool Done { get; set; }
}

public sealed class NotesState
{
	public string Draft { get; set; } = "";

	public string Error { get; set; } = "";

	public bool Dark { get; set; }

	public List<NoteItem> Notes { get; set; } = new List<NoteItem>();

	public void Drop(NoteItem item) => Notes.Remove(item);
}

public static class NotesScreen
{
	public const string ScreenId = "notes";

	public static Element Render(NotesState state, IPlatform platform)
	{
		void Add()
		{
			if (state.Draft.Length == 0)
			{
				state.Error = "Need a title";
				platform.RequestRender();
			}
			else
			{
				state.Error = "";
				state.Notes.Add(new NoteItem { Id = state.Draft, Title = state.Draft, Body = "" });
				Storage.Set(platform, "notes-last", state.Draft);
				state.Draft = "";
				platform.RequestRender();
			}
		}

		var rows = new List<Element>();
		rows.Add(new Text(state.Dark ? "Night" : "Day") { Key = "mode" });
		rows.Add(new Switch(
			state.Dark,
			value =>
			{
				state.Dark = value;
				Theme.Mode(platform, state.Dark);
				platform.RequestRender();
			},
			"Dark")
		{ Key = "dark" });
		rows.Add(new TextInput(
			state.Draft,
			value =>
			{
				state.Draft = value;
				platform.RequestRender();
			},
			"New note",
			"Title")
		{
			Key = "draft",
			Keyboard = Keyboard.Text,
			Lines = 1,
			Submit = Submit.Send,
			OnSubmit = () => Add(),
		});
		rows.Add(new Text(state.Error) { Key = "error" });
		rows.Add(new Button("Add", () => Add(), "Add") { Key = "add" });
		foreach (var item in state.Notes)
		{
			rows.Add(new Column(
			[
				new Text(item.Done ? "done" : "open") { Key = "flag" },
				new Text(item.Title) { Key = "title" },
				new Button(
					"Open",
					() =>
					{
						platform.Push("note", item);
						platform.RequestRender();
					},
					"Open")
				{ Key = "open" },
				new Button(
					"Delete",
					() =>
					{
						state.Drop(item);
						platform.RequestRender();
					},
					"Delete")
				{ Key = "delete" },
			])
			{ Key = item.Id });
		}

		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } }) { Key = "scroll" };
	}
}
