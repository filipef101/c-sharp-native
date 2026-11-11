using NativePoc;

[assembly: ReloadCallable("NativePoc.Library")]

namespace NativePoc.Library;

public static class LibraryFormat
{
	public static string Label(string name) => name.Length == 0 ? "empty" : name.Trim();
}
