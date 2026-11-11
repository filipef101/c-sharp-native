namespace NativePoc.Settings;

public sealed record SettingsState
{
	public string Name { get; set; } = "";

	public string Note { get; set; } = "";

	public bool Dark { get; set; }
}

public static class SettingsScreen
{
	public const string ScreenId = "settings";

	public static Element Render(SettingsState state, IPlatform platform) =>
		new Column(
		[
			new Text(state.Dark ? "Night" : "Day") { Key = "mode" },
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					platform.RequestRender();
				},
				"Name",
				"Name")
			{ Key = "name" },
			new TextField(
				state.Note,
				value =>
				{
					state.Note = value;
					platform.RequestRender();
				},
				"Note",
				"Note")
			{ Key = "note" },
			new Switch(
				state.Dark,
				value =>
				{
					state.Dark = value;
					platform.RequestRender();
				},
				"Dark")
			{ Key = "dark" },
			new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" },
		])
		{ Key = "settings" };
}
