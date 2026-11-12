namespace NativePoc;

internal static class ScreenSignals
{
	public static Action? Pause { get; set; }

	public static Action? Resume { get; set; }

	public static Action? Leave { get; set; }

	public static void FirePause() => Pause?.Invoke();

	public static void FireResume() => Resume?.Invoke();

	public static void FireLeave()
	{
		var leave = Leave;
		Pause = null;
		Resume = null;
		Leave = null;
		leave?.Invoke();
	}
}
