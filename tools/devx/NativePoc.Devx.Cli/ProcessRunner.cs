using System.Diagnostics;
using System.Text;

namespace NativePoc.Devx;

static class ProcessRunner
{
	public static (int Code, string Stdout, string Stderr) Run(
		string file,
		IReadOnlyList<string> args,
		string? workingDirectory = null,
		Action<string>? onStdout = null,
		Action<string>? onStderr = null)
	{
		var start = new ProcessStartInfo(file)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
		};
		foreach (var arg in args)
			start.ArgumentList.Add(arg);
		Process? process;
		try
		{
			process = Process.Start(start);
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			throw new InvalidOperationException(file + " did not start: " + ex.Message);
		}

		using var running = process ?? throw new InvalidOperationException(file + " did not start.");
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();
		var outTask = Task.Run(() => Pump(running.StandardOutput, stdout, onStdout));
		var errTask = Task.Run(() => Pump(running.StandardError, stderr, onStderr));
		running.WaitForExit();
		outTask.GetAwaiter().GetResult();
		errTask.GetAwaiter().GetResult();
		return (running.ExitCode, stdout.ToString(), stderr.ToString());
	}

	static void Pump(StreamReader reader, StringBuilder sink, Action<string>? onLine)
	{
		while (true)
		{
			var line = reader.ReadLine();
			if (line is null)
				break;
			sink.AppendLine(line);
			onLine?.Invoke(line);
		}
	}
}
