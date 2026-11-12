namespace NativePoc.Showcase;

public sealed class FloorState
{
}

public static class FloorScreen
{
	public const string ScreenId = "floor";

	// The canonical showcase floor. One native column, no children.
	// Built with the same Release flags as Northroom, and launched instead of home.
	public static Element Render(FloorState state, IPlatform platform)
	{
		_ = state;
		_ = platform;
		return new Column([])
		{
			Key = "page",
		};
	}
}
