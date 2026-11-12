namespace NativePoc.Bind;

public sealed class BindState
{
	public string Note { get; set; } = "ready";
}

public static class BindScreen
{
	public const string ScreenId = "bind";

	public static Element Render(BindState state, IPlatform platform) =>
		new Column(
		[
			new Text("Bind") { Key = "heading", Style = new Style { FontSize = 22 } },
			new Text(state.Note) { Key = "note" },
			new Button(
				"Pulse",
				() =>
				{
					DeviceVibrator.Vibrate(20);
					state.Note = "sent";
					platform.RequestRender();
				},
				"Pulse")
			{
				Key = "pulse",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
		]);
}
