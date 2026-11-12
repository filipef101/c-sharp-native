using System.Diagnostics;

namespace NativePoc.Devx.Tests;

public sealed class ToolScriptTests
{
	[Fact]
	public void Guest_env_self_test_passes()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		AssertExitZero(Path.Combine(root, "tools", "bench", "guest-env.sh"), "guest-env self-test ok");
	}

	[Fact]
	public void Symbolicate_self_test_prints_a_file_line()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		AssertExitZero(Path.Combine(root, "tools", "devx", "symbolicate-stack.sh"), "symbolicate self-test ok");
	}

	static void AssertExitZero(string script, string marker)
	{
		var start = new ProcessStartInfo("bash", script + " --self-test")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		using var process = Process.Start(start);
		Assert.NotNull(process);
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0, stdout + stderr);
		Assert.Contains(marker, stdout, StringComparison.Ordinal);
	}
}
