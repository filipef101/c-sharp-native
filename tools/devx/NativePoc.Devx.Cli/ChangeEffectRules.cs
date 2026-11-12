namespace NativePoc.Devx;

enum ChangeEffect
{
	Ignore,
	ReloadScreen,
	RestartRequired,
	RepublishBinding,
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
				|| normalized.StartsWith("src/NativePoc.Android/", StringComparison.Ordinal)
				|| IsCallableSource(normalized))
				return ChangeEffect.RestartRequired;
			if (IsBindingSource(normalized))
			{
				effect = ChangeEffect.RepublishBinding;
				continue;
			}
			if (effect == ChangeEffect.RepublishBinding)
				continue;
			if (normalized.StartsWith("src/NativePoc.Screen/", StringComparison.Ordinal)
				|| normalized.StartsWith("samples/NativePoc.Packing/", StringComparison.Ordinal)
				|| IsAppScreen(normalized)
				|| IsProjectScreen(normalized)
				|| IsSampleCompanion(normalized))
				effect = ChangeEffect.ReloadScreen;
		}

		return effect;
	}

	public static bool IsHostEdit(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (!normalized.EndsWith(".cs", StringComparison.Ordinal))
			return false;
		if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal))
			return false;
		return normalized.StartsWith("src/NativePoc.Core/", StringComparison.Ordinal)
			|| normalized.StartsWith("src/NativePoc.Android/", StringComparison.Ordinal);
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
		return parts[2].Length > "Screen.cs".Length;
	}

	public static bool IsProjectScreen(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (!normalized.StartsWith("samples/", StringComparison.Ordinal) || !normalized.EndsWith("Screen.cs", StringComparison.Ordinal))
			return false;
		if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal))
			return false;
		if (IsAppScreen(normalized) || IsPackingScreen(normalized))
			return false;
		var parts = normalized.Split('/');
		return parts.Length == 3 && parts[1].Length > 0 && parts[2].Length > "Screen.cs".Length;
	}

	public static bool IsSampleCompanion(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (!normalized.StartsWith("samples/", StringComparison.Ordinal) || !normalized.EndsWith(".cs", StringComparison.Ordinal) || IsAppScreen(normalized))
			return false;
		var parts = normalized.Split('/');
		return parts.Length == 3 && NewLoop.IsName(parts[1]);
	}

	public static bool IsBindingSource(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (!normalized.StartsWith("samples/", StringComparison.Ordinal) || !normalized.EndsWith(".cs", StringComparison.Ordinal))
			return false;
		if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal))
			return false;
		var name = normalized[(normalized.LastIndexOf('/') + 1)..];
		return name.StartsWith("Device", StringComparison.Ordinal) && name.Length > "Device.cs".Length;
	}

	public static bool IsCallableSource(string relativePath)
	{
		var normalized = Normalize(relativePath);
		if (normalized == "src/NativePoc.Screen/CounterService.cs")
			return true;
		return normalized.StartsWith("samples/NativePoc.Library/", StringComparison.Ordinal)
			&& normalized.EndsWith(".cs", StringComparison.Ordinal)
			&& !normalized.Contains("/bin/", StringComparison.Ordinal)
			&& !normalized.Contains("/obj/", StringComparison.Ordinal);
	}

	public static string Normalize(string relativePath) =>
		relativePath.Replace('\\', '/');
}
