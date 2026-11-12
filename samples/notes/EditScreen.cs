namespace NativePoc.Notes;

public sealed class EditState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public string Error { get; set; } = "";

	public string Saved { get; set; } = "";

	public string Record { get; set; } = "";
}

public static class EditScreen
{
	public const string ScreenId = "edit";

	public static Element Render(EditState state, IPlatform platform)
	{
		async void Save()
		{
			if (state.Title.Length == 0)
			{
				state.Error = "Title is required";
				platform.RequestRender();
			}
			else
			{
				state.Error = "";
				state.Saved = await Persist.Put(platform, "notes.last", state.Title);
				state.Record = await Persist.PutRecord(platform, "notes.item", state.Id, state.Title, state.Body);
				platform.WriteBack();
				Nav.Pop(platform);
				platform.RequestRender();
			}
		}

		return new Column(
		[
			new Text("Edit") { Key = "heading", Style = new Style { FontSize = 22 } },
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
				Submit = Submit.Next,
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
				Submit = Submit.Go,
				OnSubmit = () => Save(),
			},
			new Text(state.Error) { Key = "error" },
			new Text(state.Saved) { Key = "saved" },
			new Button("Save", () => Save(), "Save") { Key = "save" },
			new Button(
				"Back",
				() =>
				{
					Nav.Pop(platform);
					platform.RequestRender();
				},
				"Back")
			{ Key = "back" },
		])
		{ Key = "edit" };
	}
}
