namespace NativePoc.Note;

public sealed class NoteState
{
	public string OpenId { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public string Error { get; set; } = "";
}

public static class NoteScreen
{
	public const string ScreenId = "note";

	public static Element Render(NoteState state, IPlatform platform)
	{
		return new Column(
			new Text(state.OpenId) { Key = "open" },
			new TextField(
				state.Title,
				value =>
				{
					state.Title = value;
					platform.RequestRender();
				},
				"Title",
				"Title")
			{ Key = "title" },
			new TextField(
				state.Body,
				value =>
				{
					state.Body = value;
					platform.RequestRender();
				},
				"Body",
				"Body")
			{ Key = "body", Lines = 3 },
			new Text(state.Error) { Key = "error" },
			new Button(
				"Save",
				() =>
				{
					if (state.Title.Length == 0)
					{
						state.Error = "Title is required";
						platform.RequestRender();
						return;
					}

					state.Error = "";
					Storage.Set(platform, "notes-last", state.Title);
					platform.Pop();
					platform.RequestRender();
				},
				"Save")
			{ Key = "save" },
			new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" })
		{ Key = "note" };
	}
}
