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
			new Button(
				"Copy",
				() =>
				{
					DeviceClipboard.Set("bind", "pier");
					state.Note = DeviceClipboard.Get();
					platform.RequestRender();
				},
				"Copy")
			{
				Key = "copy",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
			new Button(
				"Share",
				() =>
				{
					DeviceShare.Send("pier");
					state.Note = "shared";
					platform.RequestRender();
				},
				"Share")
			{
				Key = "share",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
			new Button(
				"Toast",
				() =>
				{
					DeviceToast.Show("pier");
					state.Note = "toasted";
					platform.RequestRender();
				},
				"Toast")
			{
				Key = "toast",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
			new Button(
				"Battery",
				() => DeviceBattery.Read("bind", "Note"),
				"Battery")
			{
				Key = "battery",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
			new Button(
				"Watch",
				() => DeviceClipboard.Listen("bind", "Note"),
				"Watch")
			{
				Key = "watch",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
			new Button(
				"Bench",
				() => DeviceBench.Measure(),
				"Bench")
			{
				Key = "bench",
				Style = new Style { Padding = 14, FontSize = 16 },
			},
		]);
}
