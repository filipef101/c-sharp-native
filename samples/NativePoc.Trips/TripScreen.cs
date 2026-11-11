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
			new Text(state.Name.Length == 0 ? L.Get("Trip") : L.Get(state.Name)) { Key = "title", Style = new Style { FontSize = 22, Weight = FontWeight.Bold } },
			new Text(state.OpenId) { Key = "id" },
			new TextField(
				state.Note,
				value =>
				{
					state.Note = value;
					Storage.Set(platform, "trip-note", state.Note);
					platform.RequestRender();
				},
				L.Get("Note"),
				L.Get("Note"))
			{ Key = "note", Lines = 3, Submit = Submit.Send },
			new Button(L.Get("Packing"), () => { platform.Push("kit"); platform.RequestRender(); }, L.Get("Packing")) { Key = "pack" },
			new Button(L.Get("Share"), () => Share.Text(platform, state.Note), L.Get("Share note")) { Key = "share" },
			new Button(L.Get("Back"), () => { platform.Pop(); platform.RequestRender(); }, L.Get("Back")) { Key = "back" },
		])
		{ Key = "trip", Style = new Style { Gap = 8, Padding = 4 } };
}
