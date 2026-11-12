namespace NativePoc.Devx;

static class BindingNotice
{
	public const string Line = "cnative: a new binding needs a republish. A literal or a different registered method hot-reloads.";

	public static bool Calls(string source)
	{
		for (var index = 0; index + 8 < source.Length; index++)
		{
			if (!source.AsSpan(index).StartsWith("Device", StringComparison.Ordinal))
				continue;
			var end = index + "Device".Length;
			if (end >= source.Length || !char.IsUpper(source[end]))
				continue;
			end++;
			while (end < source.Length && char.IsLetterOrDigit(source[end]))
				end++;
			if (end < source.Length && source[end] == '.')
				return true;
		}

		return false;
	}

	public static bool CallsFile(string repo, string relative)
	{
		try
		{
			var path = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
			return File.Exists(path) && Calls(File.ReadAllText(path));
		}
		catch (IOException)
		{
			return false;
		}
	}
}
