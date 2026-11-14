namespace NativePoc.Tests;

public sealed class MotionThemeTests
{
	[Fact]
	public void Motion_bytes_round_trip_and_the_clock_interpolates()
	{
		var motion = new Motion
		{
			FromOpacity = 0,
			FromX = 0,
			FromY = 48,
			FromScale = 80,
			Opacity = 100,
			X = 0,
			Y = 0,
			Scale = 100,
			Duration = 900,
		};
		var bytes = new byte[MotionCodec.Bytes];
		MotionCodec.Write(motion, bytes);
		Assert.Equal(motion, MotionCodec.Read(bytes));
		Assert.False(MotionCodec.IsRest(motion));
		Assert.True(MotionCodec.IsRest(Motion.Rest));
		Assert.Equal(0, MotionMath.Lerp(0, 100, 0, 900));
		Assert.Equal(11, MotionMath.Lerp(0, 100, 100, 900));
		Assert.Equal(100, MotionMath.Lerp(0, 100, 900, 900));
	}

	[Fact]
	public void A_motion_patch_reaches_the_mounted_view_and_a_reload_replaces_it()
	{
		var host = new MemoryHost();
		var reconciler = new ViewReconciler<MemoryNode>(host);
		var first = new Text("Rise")
		{
			Key = "rise",
			Motion = new Motion { FromOpacity = 0, Opacity = 100, FromScale = 80, Scale = 100, Duration = 400 },
		};
		reconciler.Render(new Column([first]));
		Assert.Equal(100, reconciler.Root!.Children[0].Motion!.Value.Opacity);
		var second = new Text("Rise")
		{
			Key = "rise",
			Motion = new Motion { FromOpacity = 20, Opacity = 80, FromScale = 100, Scale = 100, Duration = 200 },
		};
		reconciler.Render(new Column([second]));
		Assert.Equal(80, reconciler.Root.Children[0].Motion!.Value.Opacity);
		Assert.False(reconciler.Root.Children[0].Disposed);
	}

	[Fact]
	public void Theme_tokens_follow_the_system_until_a_mode_is_stored()
	{
		Assert.False(ThemeTokens.ResolveDark(false, "system"));
		Assert.True(ThemeTokens.ResolveDark(true, "system"));
		Assert.True(ThemeTokens.ResolveDark(false, "dark"));
		Assert.False(ThemeTokens.ResolveDark(true, "light"));
		Assert.Equal("dark", ThemeTokens.Persist("true"));
		Assert.Equal("light", ThemeTokens.Persist("false"));
		Assert.Equal("system", ThemeTokens.Persist("system"));
		Assert.NotEqual(ThemeTokens.Page(false), ThemeTokens.Page(true));
		Assert.NotEqual(ThemeTokens.Text(false), ThemeTokens.Text(true));
		Assert.NotEqual(ThemeTokens.Accent(false), ThemeTokens.Accent(true));
		Assert.True(ChromeFill.Ratio(ThemeTokens.Text(false), ThemeTokens.Page(false)) >= 4.5);
		Assert.True(ChromeFill.Ratio(ThemeTokens.Text(true), ThemeTokens.Page(true)) >= 4.5);
		Assert.True(ChromeFill.Ratio(ThemeTokens.Hint(false), ThemeTokens.Page(false)) >= 3.0);
		Assert.True(ChromeFill.Ratio(ThemeTokens.Hint(true), ThemeTokens.Page(true)) >= 3.0);
	}

	[Fact]
	public void A_transition_is_instant_when_either_scale_is_zero()
	{
		Assert.True(MotionScale.Instant(0f, 1f));
		Assert.True(MotionScale.Instant(1f, 0f));
		Assert.True(MotionScale.Instant(-1f, 1f));
		Assert.False(MotionScale.Instant(1f, 1f));
		Assert.False(MotionScale.Instant(0.5f, 1f));
		Assert.Equal(0, MotionScale.DurationMs(0f, 1f));
		Assert.Equal(0, MotionScale.DurationMs(1f, 0f));
		Assert.Equal(MotionScale.BasisMs, MotionScale.DurationMs(1f, 1f));
		Assert.Equal(90, MotionScale.DurationMs(0.5f, 1f));
		Assert.Equal(90, MotionScale.DurationMs(1f, 0.5f));
	}

	[Fact]
	public void A_fault_query_keeps_its_span_and_a_dismiss_query_is_empty()
	{
		var payload = ScreenQuery.FaultNote("NP0100", "The screen subset does not include this expression.", 12, 18, 3, 4);
		Assert.Equal(ScreenQuery.Fault, ScreenQuery.Op(payload));
		var text = System.Text.Encoding.UTF8.GetString(payload.AsSpan(6));
		Assert.Equal("NP0100\nThe screen subset does not include this expression.\n12:18:3:4", text);
		Assert.Equal(ScreenQuery.Dismiss, ScreenQuery.Op(ScreenQuery.DismissOverlay()));
	}
}
