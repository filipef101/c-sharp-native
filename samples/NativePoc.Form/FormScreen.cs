namespace NativePoc.Form;

public sealed class FormState
{
	public string Email { get; set; } = "";

	public string Note { get; set; } = "";

	public string Error { get; set; } = "";

	public bool Sent { get; set; }
}

public static class FormScreen
{
	public const string ScreenId = "form";

	public static Element Render(FormState state, IPlatform host)
	{
		return new Column(
			new TextField(
				state.Email,
				value =>
				{
					state.Email = value;
					host.RequestRender();
				},
				"Email",
				"Email")
			{
				Key = "email",
				Keyboard = Keyboard.Email,
				Submit = Submit.Next,
			},
			new TextField(
				state.Note,
				value =>
				{
					state.Note = value;
					host.RequestRender();
				},
				"Note",
				"Note")
			{
				Key = "note",
				Lines = 4,
				Submit = Submit.Send,
				OnSubmit = () =>
				{
					state.Error = state.Email.Length == 0 ? "Email is required" : "";
					state.Sent = state.Email.Length != 0;
					Haptics.Confirm(host);
					Share.Text(host, state.Email);
					host.RequestRender();
				},
			},
			new Text(state.Error) { Key = "error" },
			new Text(state.Sent ? "Sent" : "Draft") { Key = "status" },
			new Button("Check", () => { host.RequestRender(); }, "Check") { Key = "check" }
		)
		{ Key = "form" };
	}
}
