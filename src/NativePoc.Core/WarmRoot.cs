namespace NativePoc;

internal static class WarmRoot
{
	public static bool Same(string top, string requested) =>
		requested.Length > 0 && string.Equals(top, requested, StringComparison.Ordinal);
}
