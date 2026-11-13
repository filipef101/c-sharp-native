using System.Text;

namespace NativePoc;

internal static class TroubleGuide
{
	public readonly record struct Entry(string Title, string Log, string Do);

	public const string Republish = "a new binding needs a republish";

	public static readonly Entry[] Entries =
	[
		new("Corrupt state", StartupGuard.StateLog("corrupt"), "Delete `files/nativepoc-state.bin` and start again. Those bytes are not a checkpoint. The app paints the counter."),
		new("Old checkpoint", StartupGuard.StateLog("old-version"), "The file was written by an older build. Delete `files/nativepoc-state.bin` and start again. The app paints the counter."),
		new("Unknown root", "startup root unknown", "The `nativepoc_root` extra is not a staged screen. Omit it, or pass a catalog id. The app paints the counter."),
		new("Stale token", "hotreload rejected reason=stale-token", "The process wrote a new `files/nativepoc-hotreload.token`. The watcher reads that file and retries. Save the screen again."),
		new("Rejected program", "startup render rejected", "The baked program is older than this interpreter. The app paints the compiled counter. Rebuild so the catalog is regenerated."),
		new("Republish needed", Republish, "A new `[JniMethod]` is not in this binary. Publish the listener again. A literal or a method that is already registered hot-reloads."),
	];

	public static bool Covers(string logged)
	{
		foreach (var entry in Entries)
		{
			if (logged.Contains(entry.Log, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	public static string Markdown()
	{
		var builder = new StringBuilder();
		builder.Append("# Troubleshooting\n\n");
		builder.Append("The listener logs these lines and still paints a screen.\n\n");
		for (var index = 0; index < Entries.Length; index++)
		{
			var entry = Entries[index];
			builder.Append("## ").Append(entry.Title).Append("\n\n");
			builder.Append('`').Append(entry.Log).Append("`\n\n");
			builder.Append(entry.Do);
			builder.Append(index + 1 == Entries.Length ? "\n" : "\n\n");
		}

		return builder.ToString();
	}
}
