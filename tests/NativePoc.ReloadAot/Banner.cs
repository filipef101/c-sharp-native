using NativePoc;

[assembly: ReloadCallable("NativePoc.ReloadAot")]

namespace NativePoc.ReloadAot;

public static class Banner
{
	public static string Label(string name) => "rebuilt";
}
