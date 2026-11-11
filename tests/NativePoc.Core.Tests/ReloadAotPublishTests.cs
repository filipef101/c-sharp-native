namespace NativePoc.Tests;

public sealed class ReloadAotPublishTests
{
	[Fact]
	public async Task A_constant_method_with_a_null_coalescing_argument_publishes_native_aot()
	{
		var root = Repo();
		var projectDir = Path.Combine(root, "tests", "NativePoc.ReloadAot");
		var project = Path.Combine(projectDir, "NativePoc.ReloadAot.csproj");
		var start = new System.Diagnostics.ProcessStartInfo("dotnet")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			WorkingDirectory = projectDir,
		};
		start.ArgumentList.Add("publish");
		start.ArgumentList.Add(project);
		start.ArgumentList.Add("-c");
		start.ArgumentList.Add("Release");
		start.ArgumentList.Add("-p:EmitCompilerGeneratedFiles=true");
		start.ArgumentList.Add("--nologo");
		var dotnetRoot = DotnetRoot();
		if (dotnetRoot.Length > 0)
			start.Environment["DOTNET_ROOT"] = dotnetRoot;
		using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
		var stdoutTask = process.StandardOutput.ReadToEndAsync();
		var stderrTask = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(240_000))
			throw new InvalidOperationException("dotnet publish did not finish.");
		var stdout = await stdoutTask;
		var stderr = await stderrTask;
		Assert.True(process.ExitCode == 0, stdout + stderr);

		var generated = Directory.GetFiles(projectDir, "ReloadDispatch.g.cs", SearchOption.AllDirectories);
		Assert.NotEmpty(generated);
		var source = File.ReadAllText(generated[0]);
		Assert.Contains("string a0 = args[0].Text ?? \"\";", source);
		Assert.Contains("Banner.Label(a0)", source);
		Assert.DoesNotContain("Banner.Label(args[0].Text ?? \"\")", source);

		var publishDir = Path.Combine(projectDir, "bin", "Release", "net10.0", "linux-x64", "publish");
		var exe = Path.Combine(publishDir, "NativePoc.ReloadAot");
		Assert.True(File.Exists(exe));
		Assert.False(File.Exists(Path.Combine(publishDir, "NativePoc.ReloadAot.dll")));
		var run = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		}) ?? throw new InvalidOperationException("The NativeAOT binary did not start.");
		var runOutTask = run.StandardOutput.ReadToEndAsync();
		var runErrTask = run.StandardError.ReadToEndAsync();
		Assert.True(run.WaitForExit(30_000));
		Assert.True(run.ExitCode == 0, await runOutTask + await runErrTask);
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

	static string DotnetRoot()
	{
		var current = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (current is not null && Directory.Exists(Path.Combine(current, "packs", "Microsoft.NETCore.App.Ref")))
			return current;
		var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
		return Directory.Exists(Path.Combine(home, "packs", "Microsoft.NETCore.App.Ref")) ? home : "";
	}
}
