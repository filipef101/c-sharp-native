namespace NativePoc.Devx;

static class RestartPolicy
{
	public static bool ShouldRestart(bool rude, bool flag, bool interactive, Func<string?> readLine)
	{
		if (!rude)
			return false;
		if (flag)
			return true;
		if (!interactive)
			return false;
		var answer = readLine()?.Trim();
		return answer is "y" or "Y" or "yes" or "Yes";
	}
}
