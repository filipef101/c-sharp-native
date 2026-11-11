using System.Diagnostics;
using System.Globalization;
using NativePoc;

namespace NativePoc.Devx;

readonly record struct AgentInvocation(string Verb, string? Serial, string? Path, int X, int Y, string? Text);

static class AgentLoop
{
	public static int Run(string[] args)
	{
		AgentInvocation invocation;
		try
		{
			invocation = Parse(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			Console.Error.WriteLine(ArgParser.Usage);
			return 2;
		}

		try
		{
			var serial = AndroidLoop.ResolveSerial(invocation.Serial);
			var token = AndroidLoop.WaitToken(serial);
			using var connection = new HotReloadConnection(serial, token.Hex);
			var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
			var source = File.ReadAllText(Path.Combine(repo, ScreenCompiler.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
			var hash = ScreenProgram.SourceHash(source);
			var tree = connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
			if (!tree.Ok)
			{
				Console.Error.WriteLine(tree.Text);
				return 1;
			}

			switch (ScreenSnapshot.Freshness(tree.Text, hash))
			{
				case ScreenFreshness.Current:
					break;
				case ScreenFreshness.Compiled:
					Console.Error.WriteLine("screen is the compiled Render; push a program before driving it");
					return 3;
				case ScreenFreshness.Stale:
					Console.Error.WriteLine("screen is stale: src/NativePoc.Screen/Screen.cs changed since the running program");
					return 3;
				case ScreenFreshness.Unreadable:
					Console.Error.WriteLine("screen reply had no source line");
					return 1;
				default:
					Console.Error.WriteLine("screen reply had no source line");
					return 1;
			}

			switch (invocation.Verb)
			{
				case "tree":
					Console.Write(tree.Text);
					return 0;
				case "state":
					var state = connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
					if (!state.Ok)
					{
						Console.Error.WriteLine(state.Text);
						return 1;
					}

					Console.Write(state.Text);
					return 0;
				case "screenshot":
					return Screenshot(serial, invocation.Path ?? "");
				case "tap":
					var tap = AndroidLoop.Adb(serial, "shell", "input", "tap", invocation.X.ToString(CultureInfo.InvariantCulture), invocation.Y.ToString(CultureInfo.InvariantCulture));
					if (tap.Code != 0)
					{
						Console.Error.WriteLine(tap.Stderr + tap.Stdout);
						return 1;
					}

					return 0;
				case "type":
					var encoded = (invocation.Text ?? "").Replace(" ", "%s", StringComparison.Ordinal);
					var typed = AndroidLoop.Adb(serial, "shell", "input", "text", encoded);
					if (typed.Code != 0)
					{
						Console.Error.WriteLine(typed.Stderr + typed.Stdout);
						return 1;
					}

					return 0;
				default:
					throw new InvalidOperationException("Unknown agent verb.");
			}
		}
		catch (InvalidOperationException ex)
		{
			Console.Error.WriteLine("cnative: " + ex.Message);
			return 1;
		}
	}

	public static AgentInvocation Parse(IReadOnlyList<string> args)
	{
		if (args.Count == 0 || args[0] != "agent")
			throw new UsageException("Usage: cnative agent tree|state|screenshot|tap|type");
		string? serial = null;
		string? verb = null;
		var positionals = new List<string>();
		for (var index = 1; index < args.Count; index++)
		{
			var arg = args[index];
			if (arg == "--device")
			{
				if (index + 1 >= args.Count)
					throw new UsageException("--device needs a serial.");
				serial = args[++index];
				continue;
			}

			if (verb is null)
			{
				verb = arg;
				continue;
			}

			positionals.Add(arg);
		}

		switch (verb)
		{
			case "tree":
			case "state":
				if (positionals.Count != 0)
					throw new UsageException("Usage: cnative agent " + verb);
				return new AgentInvocation(verb, serial, null, 0, 0, null);
			case "screenshot":
				if (positionals.Count != 1)
					throw new UsageException("Usage: cnative agent screenshot <png>");
				return new AgentInvocation(verb, serial, positionals[0], 0, 0, null);
			case "tap":
				if (positionals.Count != 2
					|| !int.TryParse(positionals[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
					|| !int.TryParse(positionals[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y))
					throw new UsageException("Usage: cnative agent tap <x> <y>");
				return new AgentInvocation(verb, serial, null, x, y, null);
			case "type":
				if (positionals.Count == 0)
					throw new UsageException("Usage: cnative agent type <text>");
				return new AgentInvocation(verb, serial, null, 0, 0, string.Join(' ', positionals));
			default:
				throw new UsageException("Usage: cnative agent tree|state|screenshot|tap|type");
		}
	}

	static int Screenshot(string serial, string path)
	{
		var start = new ProcessStartInfo("adb")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		start.ArgumentList.Add("-s");
		start.ArgumentList.Add(serial);
		start.ArgumentList.Add("exec-out");
		start.ArgumentList.Add("screencap");
		start.ArgumentList.Add("-p");
		using var process = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
		using var file = File.Create(path);
		process.StandardOutput.BaseStream.CopyTo(file);
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0 || file.Length < 8)
		{
			Console.Error.WriteLine(error);
			return 1;
		}

		Console.WriteLine(path);
		return 0;
	}
}
