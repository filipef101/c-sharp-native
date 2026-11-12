namespace NativePoc;

internal static class HotReloadAddress
{
	public const string SocketFile = "nativepoc-hotreload.socket";

	public static string SocketFor(string? package)
	{
		if (string.IsNullOrEmpty(package))
			package = "app";
		var name = "npoc-" + package;
		return name.Length <= 100 ? name : "npoc-app";
	}

	public static int PortFor(string? package)
	{
		if (string.IsNullOrEmpty(package))
			package = "app";
		var hash = 2166136261u;
		foreach (var ch in package)
			hash = (hash ^ ch) * 16777619u;
		return 20000 + (int)(hash % 10000);
	}
}
