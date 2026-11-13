using System.Text.RegularExpressions;

namespace NativePoc.Tests;

public sealed class TroubleGuideTests
{
	static readonly string[] Sources =
	[
		"src/NativePoc.Core/StartupGuard.cs",
		"src/NativePoc.Android/StateStore.cs",
		"src/NativePoc.Android/HotReloadServer.cs",
		"src/NativePoc.Android/MainActivity.cs",
		"tools/devx/NativePoc.Devx.Cli/BindingNotice.cs",
	];

	[Fact]
	public void Every_logged_reason_has_a_doc_entry()
	{
		var path = Path.Combine(Repo(), "docs", "troubleshooting.md");
		var doc = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
		Assert.Equal(TroubleGuide.Markdown(), doc);
		string[] logged =
		[
			StartupGuard.StateLog("corrupt"),
			StartupGuard.StateLog("old-version"),
			StartupGuard.RootLog("missing"),
			StartupGuard.RenderLog("counter", "Rejected record program"),
			"hotreload rejected reason=stale-token",
			TroubleGuide.Republish,
		];
		foreach (var line in logged)
			Assert.True(TroubleGuide.Covers(line), line);
		foreach (var relative in Sources)
		{
			var text = File.ReadAllText(Path.Combine(Repo(), relative));
			foreach (Match match in Regex.Matches(text, "reason=[A-Za-z0-9-]+"))
				Assert.Contains(match.Value, doc, StringComparison.Ordinal);
			if (text.Contains("a new binding needs a republish", StringComparison.Ordinal))
				Assert.Contains(TroubleGuide.Republish, doc, StringComparison.Ordinal);
		}
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}
