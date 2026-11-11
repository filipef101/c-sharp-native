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
