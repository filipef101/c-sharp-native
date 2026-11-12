namespace NativePoc.Notes;

public sealed record NoteItem
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";
}

public sealed class NotesState
{
	public string Draft { get; set; } = "";

	public string Error { get; set; } = "";

	public string Saved { get; set; } = "";

	public string Record { get; set; } = "";

	public List<NoteItem> Notes { get; set; } = new List<NoteItem>();
}

public static class NotesScreen
{
	public const string ScreenId = "notes";

	public static Element Render(NotesState state, IPlatform platform)
	{
		async void Add()
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
				state.Saved = await Persist.Put(platform, "notes.last", state.Draft);
				state.Record = await Persist.PutRecord(platform, "notes.item", state.Draft, state.Draft, state.Draft);
				state.Draft = "";
				platform.RequestRender();
			}
		}

		var rows = new List<Element>();
		rows.Add(new Text("Notes")
		{
			Key = "heading",
			Style = new Style { FontSize = 22 },
		});
		rows.Add(new TextInput(
			state.Draft,
			value =>
			{
				state.Draft = value;
				platform.RequestRender();
			},
			"Title",
			"New note")
		{
			Key = "draft",
			Keyboard = Keyboard.Text,
			Lines = 1,
			Submit = Submit.Send,
			OnSubmit = () => Add(),
		});
		rows.Add(new Text(state.Error) { Key = "error" });
		rows.Add(new Text(state.Saved) { Key = "saved" });
		rows.Add(new Button("Add", () => Add(), "Add") { Key = "add" });
		foreach (var item in state.Notes)
		{
			rows.Add(new Column(
			[
				new Text(item.Title) { Key = "title" },
				new Button(
					"Open",
					() =>
					{
						platform.Push("detail", item);
						platform.RequestRender();
					},
					"Open")
				{ Key = "open" },
				new Button(
					"Edit",
					() =>
					{
						platform.Push("edit", item);
						platform.RequestRender();
					},
					"Edit")
				{ Key = "edit" },
			])
			{
				Key = item.Id,
			});
		}

		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } }) { Key = "scroll" };
	}
}
