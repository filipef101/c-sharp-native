namespace NativePoc.Bind;

public sealed class BindState
{
	public string Note { get; set; } = "ready";

	public string Phase { get; set; } = "here";

	public string Returned { get; set; } = "here";
}

public static class BindScreen
{
	public const string ScreenId = "bind";

	public static Element Render(BindState state, IPlatform platform)
	{
		void Paused()
		{
			state.Phase = "paused";
			platform.RequestRender();
		}

		void Resumed()
		{
			state.Returned = "resumed";
			platform.RequestRender();
		}

		void Left()
		{
			DeviceClipboard.Stop();
			state.Phase = "left";
			platform.RequestRender();
		}

		platform.OnPause(Paused);
		platform.OnResume(Resumed);
		platform.OnLeave(Left);
		return new ScrollView(new Column(
		[
			new Column(
			[
				new Text("Bind")
				{
					Key = "heading",
					Style = new Style { FontSize = 22, Weight = FontWeight.Bold, Color = ThemeTokens.TextLight },
				},
				new Text(state.Note)
				{
					Key = "note",
					Style = new Style { FontSize = 16, Color = ThemeTokens.TextLight },
				},
				new Text(state.Phase)
				{
					Key = "phase",
					Style = new Style { FontSize = 13, Color = ThemeTokens.MutedLight },
				},
				new Text(state.Returned)
				{
					Key = "returned",
					Style = new Style { FontSize = 13, Color = ThemeTokens.MutedLight },
				},
			])
			{
				Key = "hero",
				Style = new Style
				{
					Background = ThemeTokens.CardLight,
					Radius = 16,
					Padding = 24,
					Gap = 8,
					Stroke = ThemeTokens.AccentLight,
				},
			},
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
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
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
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
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
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
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
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
			},
			new Button(
				"Battery",
				() => DeviceBattery.Read("bind", "Note"),
				"Battery")
			{
				Key = "battery",
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
			},
			new Button(
				"Watch",
				() => DeviceClipboard.Listen("bind", "Note"),
				"Watch")
			{
				Key = "watch",
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
			},
			new Button(
				"Notify",
				() => DeviceNotify.Ask("bind", "Note"),
				"Notify")
			{
				Key = "notify",
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
			},
			new Button(
				"Bench",
				() => DeviceBench.Measure(),
				"Bench")
			{
				Key = "bench",
				Style = new Style { Padding = 16, FontSize = 16, Radius = 16, Align = Align.Start, Background = ThemeTokens.CardLight, Color = ThemeTokens.TextLight },
			},
		])
		{
			Style = new Style { Padding = 16, Gap = 12 },
		});
	}
}
