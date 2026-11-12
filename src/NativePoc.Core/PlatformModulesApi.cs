using System.Globalization;

namespace NativePoc;

public static class Clipboard
{
	public static void Copy(IPlatform platform, string label, string text)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("clipboard", "Copy", [label, text]);
	}
}

public static class Haptics
{
	public static void Confirm(IPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("haptics", "Confirm", []);
	}
}

public static class Share
{
	public static void Text(IPlatform platform, string text)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("share", "Text", [text]);
	}
}

public static class Storage
{
	public static void Set(IPlatform platform, string key, string value)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("storage", "Set", [key, value]);
	}

	public static Task<string> Get(IPlatform platform, string key)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("storage", "Get", [key]);
	}
}

public static class Http
{
	public static Task<string> GetJson(IPlatform platform, string url)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("http", "GetJson", [url]);
	}

	public static Task<string> PostJson(IPlatform platform, string url, string json)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("http", "PostJson", [url, json]);
	}
}

public static class Timer
{
	public static Task<string> After(IPlatform platform, int milliseconds)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("timer", "After", [milliseconds.ToString(CultureInfo.InvariantCulture)]);
	}
}

public static class Theme
{
	public static void Mode(IPlatform platform, string mode)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("theme", "Mode", [mode]);
	}

	public static void Mode(IPlatform platform, bool dark) => Mode(platform, dark ? "true" : "false");
}
