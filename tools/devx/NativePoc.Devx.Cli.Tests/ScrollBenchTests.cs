using System.Diagnostics;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ScrollBenchTests
{
	[Fact]
	public void Scroll_bench_self_test_passes()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var script = Path.Combine(root, "tools", "bench", "scroll-bench.sh");
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
		Assert.Contains("scroll-bench self-test ok", stdout, StringComparison.Ordinal);
	}
}
