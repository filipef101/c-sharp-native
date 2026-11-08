using System.Globalization;
using System.Text.RegularExpressions;

namespace NativePoc.Devx;

enum ReloadStatus
{
	Applied,
	CompileError,
	RudeEdit,
	Failed,
}

sealed record FormattedDiagnostic(string File, int Line, int Column, string Code, string Message)
{
	public string Display => string.Create(
		CultureInfo.InvariantCulture,
		$"{File}:{Line}:{Column}: error {Code}: {Message}");
}

sealed record ParsedHostOutput(
	int ExitCode,
	double? OnceMs,
	string? Reply,
	IReadOnlyList<FormattedDiagnostic> Errors,
	string Raw);

static partial class HostOutput
{
	public static ParsedHostOutput Parse(int exitCode, string stdout, string stderr, string? repo = null)
	{
		var raw = stdout;
		if (stderr.Length > 0)
			raw = stdout.Length == 0 ? stderr : stdout + "\n" + stderr;
		var errors = new List<FormattedDiagnostic>();
		double? onceMs = null;
		string? reply = null;
		foreach (var line in raw.Split('\n'))
		{
			var trimmed = line.TrimEnd('\r');
			var diagnostic = DiagnosticPattern().Match(StripExceptionPrefix(trimmed));
			if (diagnostic.Success)
			{
				var file = DisplayPath(diagnostic.Groups["file"].Value, repo);
				errors.Add(new FormattedDiagnostic(
					file,
					int.Parse(diagnostic.Groups["line"].Value, CultureInfo.InvariantCulture),
					int.Parse(diagnostic.Groups["col"].Value, CultureInfo.InvariantCulture),
					diagnostic.Groups["code"].Value,
					diagnostic.Groups["message"].Value));
				continue;
			}

			var timing = OncePattern().Match(trimmed);
			if (timing.Success && double.TryParse(timing.Groups["ms"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
				onceMs = ms;
			var replyMatch = ReplyPattern().Match(trimmed);
			if (replyMatch.Success)
				reply = replyMatch.Groups["reply"].Value;
		}

		return new ParsedHostOutput(exitCode, onceMs, reply, errors, raw);
	}

	public static ReloadStatus Classify(ParsedHostOutput parsed)
	{
		if (parsed.Errors.Count > 0)
			return ReloadStatus.CompileError;
		if (parsed.ExitCode == 0 && parsed.Reply is not null)
			return ReloadStatus.Applied;
		if (IsRude(parsed.Raw))
			return ReloadStatus.RudeEdit;
		return ReloadStatus.Failed;
	}

	public static bool IsRude(string text)
	{
		return text.Contains("MissingMethodException", StringComparison.Ordinal)
			|| text.Contains("MissingFieldException", StringComparison.Ordinal)
			|| text.Contains("MissingMemberException", StringComparison.Ordinal)
			|| text.Contains("TypeLoadException", StringComparison.Ordinal)
			|| text.Contains("Method not found", StringComparison.Ordinal);
	}

	public static string DisplayPath(string path, string? repo)
	{
		var normalized = path.Replace('\\', '/');
		if (string.IsNullOrEmpty(repo))
			return normalized;
		var full = Path.GetFullPath(path);
		var root = Path.GetFullPath(repo);
		var prefix = root.EndsWith(Path.DirectorySeparatorChar)
			? root
			: root + Path.DirectorySeparatorChar;
		if (full.StartsWith(prefix, StringComparison.Ordinal))
			return full[prefix.Length..].Replace('\\', '/');
		return normalized;
	}

	static string StripExceptionPrefix(string line)
	{
		const string marker = "exception=";
		if (!line.StartsWith(marker, StringComparison.Ordinal))
			return line;
		var space = line.IndexOf(' ');
		if (space < 0 || space + 1 >= line.Length)
			return line;
		return line[(space + 1)..];
	}

	[GeneratedRegex(@"^(?<file>.+)\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<message>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex DiagnosticPattern();

	[GeneratedRegex(@"^once-ms=(?<ms>\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant)]
	private static partial Regex OncePattern();

	[GeneratedRegex(@"^reply=(?<reply>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex ReplyPattern();
}
