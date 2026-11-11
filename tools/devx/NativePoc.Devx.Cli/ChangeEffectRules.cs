namespace NativePoc.Devx;

enum ChangeEffect
{
	Ignore,
	ReloadScreen,
	RestartRequired,
}

static class ChangeEffectRules
{
	public static ChangeEffect Classify(IReadOnlyList<string> relativePaths)
	{
		var effect = ChangeEffect.Ignore;
		foreach (var path in relativePaths)
		{
			var normalized = Normalize(path);
			if (!normalized.EndsWith(".cs", StringComparison.Ordinal))
				continue;
			if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal))
				continue;
			if (normalized.StartsWith("src/NativePoc.Core/", StringComparison.Ordinal)
				|| normalized.StartsWith("src/NativePoc.Android/", StringComparison.Ordinal))
				return ChangeEffect.RestartRequired;
			if (normalized.StartsWith("src/NativePoc.Screen/", StringComparison.Ordinal)
				|| normalized.StartsWith("samples/NativePoc.Packing/", StringComparison.Ordinal)
				|| IsAppScreen(normalized))
				effect = ChangeEffect.ReloadScreen;
		}

		return effect;
	}

	public static bool IsHostScreen(string relativePath) =>
		Normalize(relativePath) == "src/NativePoc.Screen/Screen.cs";

	public static bool IsPackingScreen(string relativePath) =>
		Normalize(relativePath) == "samples/NativePoc.Packing/PackingScreen.cs";

	public static bool IsAppScreen(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (!normalized.StartsWith("samples/", StringComparison.Ordinal) || !normalized.EndsWith("Screen.cs", StringComparison.Ordinal))
			return false;
		var parts = normalized.Split('/');
		if (parts.Length != 3 || !NewLoop.IsName(parts[1]))
			return false;
		return parts[2] == NewLoop.TypeName(parts[1]) + "Screen.cs";
	}

	public static string Normalize(string relativePath) =>
		relativePath.Replace('\\', '/');
}
