namespace NativePoc.Motion;

public sealed class MotionState
{
	public string Label { get; set; } = "Rise";
}

public static class MotionScreen
{
	public const string ScreenId = "motion";

	public static Element Render(MotionState state, IPlatform platform) =>
		new Column(
			new Text(state.Label)
			{
				Key = "rise",
				Motion = new Motion
				{
					FromOpacity = 0,
					FromY = 48,
					FromScale = 80,
					Opacity = 100,
					Y = 0,
					Scale = 100,
					Duration = 900,
				},
			})
		{ Key = "motion" };
}
