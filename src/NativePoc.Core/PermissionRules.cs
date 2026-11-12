namespace NativePoc;

internal enum PermissionResult
{
	Granted,
	Denied,
	Blocked,
}

internal static class PermissionRules
{
	public static PermissionResult Classify(bool granted, bool shouldShowRationale, bool userFixed, int denials)
	{
		if (granted)
			return PermissionResult.Granted;
		if (userFixed || !shouldShowRationale || denials >= 2)
			return PermissionResult.Blocked;
		return PermissionResult.Denied;
	}

	public static string Text(PermissionResult result) => result switch
	{
		PermissionResult.Granted => "granted",
		PermissionResult.Denied => "denied",
		PermissionResult.Blocked => "blocked",
		_ => throw new InvalidOperationException("Unknown permission result."),
	};
}
