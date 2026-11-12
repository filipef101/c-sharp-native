namespace NativePoc.Devx;

enum RestartChoice
{
	Ask,
	Yes,
	No,
}

static class RestartPolicy
{
	public static bool ShouldRestart(bool rude, RestartChoice choice, bool interactive, Func<string?> readLine)
	{
		if (!rude)
			return false;
		switch (choice)
		{
			case RestartChoice.Yes:
				return true;
			case RestartChoice.No:
				return false;
			case RestartChoice.Ask:
				if (!interactive)
					return false;
				var answer = readLine()?.Trim();
				return answer is "y" or "Y" or "yes" or "Yes";
			default:
				throw new InvalidOperationException("Unhandled restart choice " + choice);
		}
	}

	public static string? Follow(bool restarting, RestartChoice choice, bool interactive, bool subset)
	{
		if (restarting)
			return "restarting";
		switch (choice)
		{
			case RestartChoice.Yes:
				return null;
			case RestartChoice.No:
				return "cnative: --no leaves the app running.";
			case RestartChoice.Ask:
				if (!interactive)
					return "cnative: not a terminal, so the restart stays off. Pass --yes to relaunch, or --no to keep this choice.";
				if (subset)
					return DeveloperRefusal.FollowUp(false, true);
				return "pass --yes to relaunch. The default is no.";
			default:
				throw new InvalidOperationException("Unhandled restart choice " + choice);
		}
	}
}
