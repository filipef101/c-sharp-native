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
			new Text("Settings") { Key = "title", Style = new Style { FontSize = 22 } },
			new Text(state.Dark ? "Night" : "Day") { Key = "mode" },
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					Storage.Set(platform, "name", state.Name);
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
					Storage.Set(platform, "note", state.Note);
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
					Theme.Mode(platform, state.Dark);
					platform.RequestRender();
				},
				"Dark")
			{ Key = "dark" },
			new Button(
				"System",
				() =>
				{
					Theme.Mode(platform, "system");
					platform.RequestRender();
				},
				"System")
			{ Key = "system" },
			new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" },
		])
		{ Key = "settings" };
}
