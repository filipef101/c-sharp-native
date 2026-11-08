using System.Text.RegularExpressions;

namespace NativePoc.Devx;

static partial class TokenText
{
	public static bool TryParse(string? text, out string hex)
	{
		hex = "";
		if (string.IsNullOrWhiteSpace(text))
			return false;
		var trimmed = text.Trim();
		if (trimmed.StartsWith("npoc-", StringComparison.OrdinalIgnoreCase))
			trimmed = trimmed[5..];
		if (trimmed.Length != 32 || !HexPattern().IsMatch(trimmed))
			return false;
		hex = trimmed.ToLowerInvariant();
		return true;
	}

	public static string? FromUnixTable(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return null;
		var match = UnixPattern().Match(text);
		if (!match.Success)
			return null;
		return match.Groups[1].Value.ToLowerInvariant();
	}

	public static IReadOnlyList<string> TokenFileNames(string listing)
	{
		var names = new List<string>();
		foreach (var line in listing.Split('\n'))
		{
			var name = line.Trim().TrimEnd('\r');
			if (name.Length == 0 || name.Contains('/') || name.Contains(' '))
				continue;
			if (name.EndsWith(".token", StringComparison.Ordinal))
				names.Add(name);
		}

		return names;
	}

	[GeneratedRegex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
	private static partial Regex HexPattern();

	[GeneratedRegex(@"npoc-([0-9a-fA-F]{32})", RegexOptions.CultureInvariant)]
	private static partial Regex UnixPattern();
}
