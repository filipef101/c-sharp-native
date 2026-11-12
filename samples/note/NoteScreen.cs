namespace NativePoc.Note;

public sealed class NoteState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public string Error { get; set; } = "";
}

public static class NoteScreen
{
	public const string ScreenId = "note";

	public static Element Render(NoteState state, IPlatform platform)
	{
		void Save()
		{
			if (state.Title.Length == 0)
			{
				state.Error = "Title is required";
				platform.RequestRender();
			}
			else
			{
				state.Error = "";
				platform.WriteBack();
				Storage.Set(platform, "notes-last", state.Title);
				platform.Pop();
				platform.RequestRender();
			}
		}

		return new Column(
		[
			new Text("Note") { Key = "heading", Style = new Style { FontSize = 22 } },
			new Text(state.Id) { Key = "open" },
			new TextInput(
				state.Title,
				value =>
				{
					state.Title = value;
					platform.RequestRender();
				},
				"Title",
				"Title")
			{
				Key = "title",
				Keyboard = Keyboard.Text,
				Submit = Submit.Go,
				OnSubmit = () => Save(),
			},
			new TextInput(
				state.Body,
				value =>
				{
					state.Body = value;
					platform.RequestRender();
				},
				"Body",
				"Body")
			{
				Key = "body",
				Keyboard = Keyboard.Text,
				Lines = 4,
				Submit = Submit.None,
			},
			new Text(state.Error) { Key = "error" },
			new Button("Save", () => Save(), "Save") { Key = "save" },
			new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" },
		])
		{ Key = "note" };
	}
}
