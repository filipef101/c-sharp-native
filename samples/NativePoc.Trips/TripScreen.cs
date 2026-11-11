namespace NativePoc.Trips;

public sealed class TripState
{
	public string OpenId { get; set; } = "";

	public string Name { get; set; } = "";

	public string Note { get; set; } = "";
}

public static class TripScreen
{
	public const string ScreenId = "trip";

	public static Element Render(TripState state, IPlatform platform) =>
		new Column(
		[
			new Text(state.Name.Length == 0 ? "Trip" : state.Name) { Key = "title", Style = new Style { FontSize = 22, Weight = FontWeight.Bold } },
			new Text(state.OpenId) { Key = "id" },
			new TextField(
				state.Note,
				value =>
				{
					state.Note = value;
					Storage.Set(platform, "trip-note", state.Note);
					platform.RequestRender();
				},
				"Note",
				"Note")
			{ Key = "note", Lines = 3, Submit = Submit.Send },
			new Button("Packing", () => { platform.Push("kit"); platform.RequestRender(); }, "Packing") { Key = "pack" },
			new Button("Share", () => Share.Text(platform, state.Note), "Share note") { Key = "share" },
			new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" },
		])
		{ Key = "trip", Style = new Style { Gap = 8, Padding = 4 } };
}
