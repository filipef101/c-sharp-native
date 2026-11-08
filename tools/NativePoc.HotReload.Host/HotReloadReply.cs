namespace NativePoc.HotReload;

public sealed class RestartRequiredException : Exception
{
	public RestartRequiredException(string message) : base(message)
	{
	}
}

public static class HotReloadReply
{
	public static readonly string[] ImplicitGlobalUsings =
	[
		"System",
		"System.Collections.Generic",
		"System.IO",
		"System.Linq",
		"System.Net.Http",
		"System.Threading",
		"System.Threading.Tasks",
	];

	public static bool NeedsRestart(string text) =>
		text.Contains("MissingMethodException", StringComparison.Ordinal)
		|| text.Contains("MissingFieldException", StringComparison.Ordinal)
		|| text.Contains("MissingMemberException", StringComparison.Ordinal)
		|| text.Contains("TypeLoadException", StringComparison.Ordinal);

	public static string Summary(string text) =>
		NeedsRestart(text) ? "restart required" : text;
}
