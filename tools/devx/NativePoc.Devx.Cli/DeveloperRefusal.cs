using System.Globalization;

namespace NativePoc.Devx;

static class DeveloperRefusal
{
	public const string Stays = "The edit was not applied. The last program stays until relaunch.";

	public static string Line(string relative, SubsetDiagnostic diagnostic)
	{
		ArgumentNullException.ThrowIfNull(diagnostic);
		return string.Create(
			CultureInfo.InvariantCulture,
			$"{relative}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Id}: {diagnostic.Message}");
	}

	public static string? FollowUp(bool restarting, bool offerFlag)
	{
		if (restarting)
			return "restarting";
		if (offerFlag)
			return "Fix the screen and save again. A relaunch loads this same file.";
		return null;
	}
}

sealed class KeptPrograms
{
	readonly Dictionary<string, byte[]> _programs = new(StringComparer.Ordinal);

	public void Remember(string relative, byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		_programs[relative] = program;
	}

	public bool TryGet(string relative, out byte[] program) =>
		_programs.TryGetValue(relative, out program!);
}
