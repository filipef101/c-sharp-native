using System.Globalization;

namespace NativePoc.Devx;

static class ReloadConsole
{
	public static IReadOnlyList<string> Format(ReloadStatus status, ParsedHostOutput parsed, double elapsedMs)
	{
		return status switch
		{
			ReloadStatus.Applied => Applied(parsed, elapsedMs),
			ReloadStatus.CompileError => parsed.Errors.Select(error => error.Display).ToArray(),
			ReloadStatus.RudeEdit => ["rude edit -> restart required"],
			ReloadStatus.Failed => Failed(parsed),
		};
	}

	public static IReadOnlyList<string> AppliedLines(double elapsedMs, double applyMs, string reply)
	{
		return
		[
			string.Create(CultureInfo.InvariantCulture, $"reloaded in {elapsedMs:F1} ms (apply {applyMs:F1} ms)"),
			"reply " + reply,
		];
	}

	public static string? RudeDetail(string raw)
	{
		foreach (var line in raw.Split('\n'))
		{
			var trimmed = line.Trim().TrimEnd('\r');
			if (trimmed.StartsWith("exception=", StringComparison.Ordinal))
			{
				var space = trimmed.IndexOf(' ');
				if (space > 0 && space + 1 < trimmed.Length)
					trimmed = trimmed[(space + 1)..];
			}

			if (trimmed.Length > 0 && HostOutput.IsRude(trimmed))
				return trimmed;
		}

		return null;
	}

	public static string? FollowUp(bool restarting, bool offerFlag)
	{
		if (restarting)
			return "restarting";
		if (offerFlag)
			return "pass --restart-on-rude to rebuild and relaunch";
		return null;
	}

	static IReadOnlyList<string> Applied(ParsedHostOutput parsed, double elapsedMs)
	{
		var timing = parsed.OnceMs is double apply
			? string.Create(CultureInfo.InvariantCulture, $"reloaded in {elapsedMs:F1} ms (apply {apply:F1} ms)")
			: string.Create(CultureInfo.InvariantCulture, $"reloaded in {elapsedMs:F1} ms");
		if (parsed.Reply is null)
			return [timing];
		return [timing, "reply " + parsed.Reply];
	}

	static IReadOnlyList<string> Failed(ParsedHostOutput parsed)
	{
		var lines = new List<string> { "reload failed" };
		var count = 0;
		foreach (var line in parsed.Raw.Split('\n'))
		{
			var trimmed = line.TrimEnd('\r');
			if (trimmed.Length == 0)
				continue;
			lines.Add(trimmed);
			count++;
			if (count == 40)
				break;
		}

		return lines;
	}
}
