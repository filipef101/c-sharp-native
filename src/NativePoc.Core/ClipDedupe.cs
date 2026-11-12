namespace NativePoc;

internal static class ClipDedupe
{
	public static bool Repeat(long lastStamp, string? lastText, long lastTicks, long stamp, string text, long now, long frequency)
	{
		if (stamp != 0 && stamp == lastStamp)
			return true;
		if (lastText is null || !string.Equals(lastText, text, StringComparison.Ordinal))
			return false;
		if (frequency <= 0)
			return false;
		var window = frequency / 10;
		if (window <= 0)
			return false;
		var elapsed = now - lastTicks;
		return elapsed >= 0 && elapsed < window;
	}
}
