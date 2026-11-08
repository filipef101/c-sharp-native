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
			if (normalized.StartsWith("src/NativePoc.Screen/", StringComparison.Ordinal))
				effect = ChangeEffect.ReloadScreen;
		}

		return effect;
	}

	public static bool IsHostScreen(string relativePath) =>
		Normalize(relativePath) == "src/NativePoc.Screen/Screen.cs";

	public static string Normalize(string relativePath) =>
		relativePath.Replace('\\', '/');
}
