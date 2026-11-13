namespace NativePoc;

internal static class StartupGuard
{
	public const string DefaultId = "counter";

	public static bool Rejected(Exception ex) =>
		ex is InvalidOperationException && ex.Message.StartsWith("Rejected ", StringComparison.Ordinal);

	public static string RenderLog(string screen, string reason) =>
		"startup render rejected screen=" + screen + " reason=" + reason + " fallback=" + DefaultId;

	public static string RootLog(string id) =>
		"startup root unknown id=" + id + " fallback=" + DefaultId;

	public static string StateReason(string message) =>
		message.Contains("version", StringComparison.Ordinal) ? "old-version" : "corrupt";

	public static string StateLog(string reason) =>
		"state-load rejected reason=" + reason;
}
