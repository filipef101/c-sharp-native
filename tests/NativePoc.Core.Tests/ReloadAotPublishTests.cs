using System.Diagnostics;

namespace NativePoc.Tests;

public class ReloadAotPublishTests
{
	[Fact(Timeout = 120_000)]
	public async Task The_generator_lifts_a_null_coalescing_argument()
	{
		await Task.Run(GeneratorLifted);
	}

	static void GeneratorLifted()
	{
		var project = Path.Combine(Repo(), "tests", "NativePoc.ReloadAot", "NativePoc.ReloadAot.csproj");
		var (stdout, stderr, code) = BoundedProcess.Run(new ProcessStartInfo(
			Dotnet(),
			"build \"" + project + "\" -c Release -f net10.0 -r linux-x64 -p:EmitCompilerGeneratedFiles=true"), 90_000);
		Assert.True(code == 0, stdout + stderr);
		var generated = Path.Combine(
			Path.GetDirectoryName(project)!,
			"obj",
			"Release",
			"net10.0",
			"linux-x64",
			"generated",
			"NativePoc.ReloadGen",
			"NativePoc.ReloadGen.ReloadDispatchGenerator",
			"ReloadDispatch.g.cs");
		Assert.True(File.Exists(generated), stdout + stderr);
		var text = File.ReadAllText(generated);
		Assert.Contains("string a0 = args[0].Text ?? \"\";", text, StringComparison.Ordinal);
		Assert.Contains("Banner.Label(a0)", text, StringComparison.Ordinal);
		Assert.DoesNotContain("Banner.Label(args[0].Text ?? \"\")", text, StringComparison.Ordinal);
		Assert.Contains("#if DEBUG", text, StringComparison.Ordinal);
		Assert.Contains("TryRunReplacement", text, StringComparison.Ordinal);
	}

	[Fact(Timeout = 20_000)]
	public async Task A_bounded_wait_kills_a_child_that_does_not_exit()
	{
		await Task.Run(() =>
		{
		var watch = Stopwatch.StartNew();
		var thrown = Assert.Throws<TimeoutException>(() => BoundedProcess.Run(new ProcessStartInfo("sleep", "120"), 1_000));
		Assert.Contains("1000", thrown.Message, StringComparison.Ordinal);
		Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), watch.Elapsed.ToString());
		});
	}

	[Fact(Timeout = 300_000)]
	public async Task The_native_aot_binary_omits_the_interpreter_when_opted_in()
	{
		await Task.Run(NativeBinaryOmitsInterpreter);
	}

	static void NativeBinaryOmitsInterpreter()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("NATIVEPOC_RELOAD_AOT"), "1", StringComparison.Ordinal))
			return;
		var project = Path.Combine(Repo(), "tests", "NativePoc.ReloadAot", "NativePoc.ReloadAot.csproj");
		var (stdout, stderr, code) = BoundedProcess.Run(new ProcessStartInfo(
			Dotnet(),
			"publish \"" + project + "\" -c Release -f net10.0 -r linux-x64"), 240_000);
		Assert.True(code == 0, stdout + stderr);
		var published = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", "net10.0", "linux-x64", "publish", "NativePoc.ReloadAot");
		Assert.True(File.Exists(published), stdout + stderr);
		Assert.False(File.Exists(published + ".dll"));
		var bytes = File.ReadAllBytes(published);
		var needle = "TryRunReplacement"u8;
		for (var i = 0; i + needle.Length <= bytes.Length; i++)
		{
			if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle))
				Assert.Fail("The native binary still contains TryRunReplacement.");
		}

		var (runOut, runErr, runCode) = BoundedProcess.Run(new ProcessStartInfo(published), 30_000);
		Assert.True(runCode == 0, runOut + runErr);
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
			dir = dir.Parent;
		return dir?.FullName ?? throw new InvalidOperationException("Repo root was not found.");
	}

	static string Dotnet()
	{
		var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (string.IsNullOrEmpty(root))
			root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
		return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
	}
}

static class BoundedProcess
{
	public static (string Stdout, string Stderr, int Code) Run(ProcessStartInfo start, int timeoutMs)
	{
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		start.UseShellExecute = false;
		start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
		start.Environment["DOTNET_NOLOGO"] = "1";
		using var process = Process.Start(start) ?? throw new InvalidOperationException("Process did not start.");
		var stdout = "";
		var stderr = "";
		var outThread = new Thread(() => stdout = process.StandardOutput.ReadToEnd()) { IsBackground = true };
		var errThread = new Thread(() => stderr = process.StandardError.ReadToEnd()) { IsBackground = true };
		outThread.Start();
		errThread.Start();
		if (!Wait(process, timeoutMs))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException)
			{
			}

			Wait(process, 5_000);
			outThread.Join(2_000);
			errThread.Join(2_000);
			throw new TimeoutException("Process did not finish within " + timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms.");
		}

		outThread.Join(10_000);
		errThread.Join(10_000);
		return (stdout, stderr, process.ExitCode);
	}

	static bool Wait(Process process, int timeoutMs)
	{
		if (process.HasExited)
			return true;
		using var done = new ManualResetEventSlim(false);
		process.EnableRaisingEvents = true;
		process.Exited += (_, _) => done.Set();
		if (process.HasExited)
			return true;
		return done.Wait(timeoutMs);
	}
}
