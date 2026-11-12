using System.Diagnostics;

namespace NativePoc.Devx.Tests;

public sealed class IlcStampTests
{
	[Fact]
	public void Unchanged_inputs_skip_and_a_one_line_change_compiles()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var script = Path.Combine(root, "templates", "cnative-app", "build", "ilc-stamp.sh");
		var dir = Directory.CreateTempSubdirectory("ilc-stamp").FullName;
		try
		{
			var flags = Path.Combine(dir, "flags.txt");
			var list = Path.Combine(dir, "inputs.txt");
			var stamp = Path.Combine(dir, "ilc-inputs.sha256");
			var source = Path.Combine(dir, "HomeScreen.cs");
			var output = Path.Combine(dir, "app.o");
			File.WriteAllText(flags, "Debug|android-x64|net10.0-android|true");
			File.WriteAllText(source, "Title = \"Hello\";\n");
			File.WriteAllText(list, source + "\n");
			File.WriteAllText(output, "object");
			var old = DateTime.UtcNow.AddMinutes(-10);
			File.SetLastWriteTimeUtc(output, old);

			var first = Run(script, "check", stamp, flags, list, output);
			Assert.Contains("ilc-stamp: compile", first);
			Assert.Equal(old, File.GetLastWriteTimeUtc(output));

			var wrote = Run(script, "write", stamp, flags, list);
			Assert.Contains("ilc-stamp: wrote stamp", wrote);

			var skip = Run(script, "check", stamp, flags, list, output);
			Assert.Contains("ilc-stamp: skip", skip);
			Assert.True(File.GetLastWriteTimeUtc(output) > old);

			File.SetLastWriteTimeUtc(output, old);
			File.WriteAllText(source, "Title = \"Hiya\";\n");
			var changed = Run(script, "check", stamp, flags, list, output);
			Assert.Contains("ilc-stamp: compile", changed);
			Assert.Equal(old, File.GetLastWriteTimeUtc(output));
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	static string Run(string script, params string[] args)
	{
		var start = new ProcessStartInfo("sh")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.ArgumentList.Add(script);
		foreach (var arg in args)
			start.ArgumentList.Add(arg);
		using var process = Process.Start(start);
		Assert.NotNull(process);
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0, stderr + stdout);
		return stdout;
	}
}
