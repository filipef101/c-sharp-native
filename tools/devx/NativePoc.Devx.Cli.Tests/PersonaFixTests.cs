using NativePoc.Devx;
using NativePoc.HotReload;

namespace NativePoc.Devx.Tests;

public sealed class PersonaFixTests
{
	[Fact]
	public void A_failed_first_push_keeps_watching_unless_once()
	{
		Assert.Equal(InitialDecision.WatchAfterFailure, InitialPush.Decide(ReloadStatus.Failed, false, false));
		Assert.Equal(InitialDecision.ExitFailure, InitialPush.Decide(ReloadStatus.Failed, false, true));
		Assert.Equal(InitialDecision.Watch, InitialPush.Decide(ReloadStatus.Applied, false, false));
		Assert.Equal(InitialDecision.Watch, InitialPush.Decide(ReloadStatus.CompileError, false, false));
		Assert.Equal(InitialDecision.ExitSuccess, InitialPush.Decide(ReloadStatus.Applied, false, true));
		Assert.Equal(InitialDecision.ExitSuccess, InitialPush.Decide(ReloadStatus.RudeEdit, true, true));
		Assert.Equal(
			"cnative: the first push failed. Still watching. Save src/NativePoc.Screen/Screen.cs again.",
			InitialPush.FailureLine);
	}

	[Fact]
	public void Readme_dev_loop_names_cnative_and_says_mode_a_restarts()
	{
		var repo = RepoRoot.Find(AppContext.BaseDirectory);
		var readme = File.ReadAllText(Path.Combine(repo, "README.md"));
		var script = File.ReadAllText(Path.Combine(repo, "tools", "devx", "cnative"));
		var comparison = File.ReadAllText(Path.Combine(repo, "docs", "devx", "comparison.md"));

		Assert.Contains("## Dev loop", readme);
		Assert.Contains("tools/devx/cnative run android", readme);
		Assert.Contains("`a` is a benchmark that restarts the app", readme);
		Assert.Contains("[[ \"$help\" == 1 && -f \"$dll\" ]]", script);
		Assert.DoesNotContain("## What is missing", comparison);
		Assert.DoesNotContain("does not write the token file", comparison);
		Assert.DoesNotContain("No device reload time was recorded", comparison);
		Assert.Contains("## What is still true", comparison);
		Assert.Contains("not the socket name", comparison);
	}

	[Fact]
	public void The_legacy_host_reports_a_missing_method_as_restart_required()
	{
		var text = "MissingMethodException: Method not found: int NativePoc.AppState.get_Extra().";

		Assert.True(HotReloadReply.NeedsRestart(text));
		Assert.Equal("restart required", HotReloadReply.Summary(text));
		Assert.False(HotReloadReply.NeedsRestart("EndOfStreamException The hot reload connection closed early."));
		Assert.Equal(ScreenCompiler.ImplicitGlobalUsings, HotReloadReply.ImplicitGlobalUsings);
	}
}
