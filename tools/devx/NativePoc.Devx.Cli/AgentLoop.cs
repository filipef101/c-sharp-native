using System.Diagnostics;
using System.Globalization;
using System.Text;
using NativePoc;

namespace NativePoc.Devx;

readonly record struct AgentInvocation(string Verb, string? Serial, string? Path, int X, int Y, string? Text, bool Running = false);

static class AgentLoop
{
	public const string RebuildWith = "Rebuild with: tools/devx/cnative run android --once";

	public static string Refusal(ScreenFreshness freshness, string hash) =>
		freshness switch
		{
			ScreenFreshness.Stale => "stale build: src/NativePoc.Screen/Screen.cs hash " + hash + " does not match the running program. " + RebuildWith,
			ScreenFreshness.Compiled => "stale build: the running screen is the compiled Render, not src/NativePoc.Screen/Screen.cs. " + RebuildWith,
			ScreenFreshness.Current => throw new InvalidOperationException("A current screen is not a refusal."),
			ScreenFreshness.Unreadable => throw new InvalidOperationException("An unreadable screen is not a stale refusal."),
		};
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

			var freshness = ScreenSnapshot.Freshness(tree.Text, hash);
			var observe = invocation.Verb is "a11y" or "layout" or "logs" or "clock";
			if (!observe && !invocation.Running)
			{
				switch (freshness)
				{
					case ScreenFreshness.Current:
						break;
					case ScreenFreshness.Compiled:
					case ScreenFreshness.Stale:
						Console.Error.WriteLine(Refusal(freshness, hash));
						return 3;
					case ScreenFreshness.Unreadable:
						Console.Error.WriteLine("screen reply had no source line");
						return 1;
					default:
						Console.Error.WriteLine("screen reply had no source line");
						return 1;
				}
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
				case "press":
					return Query(connection, ScreenQuery.PressId(invocation.Path ?? ""));
				case "enter":
					return Query(connection, ScreenQuery.EnterId(invocation.Path ?? "", invocation.Text ?? ""));
				case "a11y":
					Console.Write(AccessibilityDump.Summarize(AccessibilityDump.Read(serial)));
					return 0;
				case "layout":
					return Query(connection, ScreenQuery.Ask(ScreenQuery.Layout));
				case "logs":
					var pidText = AndroidLoop.Adb(serial, "shell", "pidof", LaunchCommand.Package).Stdout.Trim();
					var pid = pidText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
					var logs = pid.Length == 0
						? AndroidLoop.Adb(serial, "logcat", "-d", "-s", "NativePoc:I", "NativePoc:W", "NativePoc:E")
						: AndroidLoop.Adb(serial, "logcat", "-d", "--pid=" + pid, "-s", "NativePoc:I", "NativePoc:W", "NativePoc:E");
					Console.Write(logs.Stdout);
					return logs.Code == 0 ? 0 : 1;
				case "clock":
					return Query(connection, ScreenQuery.Ask(ScreenQuery.Clock));
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
			throw new UsageException("Usage: cnative agent tree|state|screenshot|tap|type|press|enter|a11y|layout|logs|clock");
		string? serial = null;
		string? verb = null;
		var running = false;
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

			if (arg == "--running")
			{
				running = true;
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
			case "a11y":
			case "layout":
			case "logs":
			case "clock":
				if (positionals.Count != 0)
					throw new UsageException("Usage: cnative agent " + verb);
				return new AgentInvocation(verb, serial, null, 0, 0, null, running);
			case "screenshot":
				if (positionals.Count != 1)
					throw new UsageException("Usage: cnative agent screenshot <png>");
				return new AgentInvocation(verb, serial, positionals[0], 0, 0, null, running);
			case "tap":
				if (positionals.Count != 2
					|| !int.TryParse(positionals[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
					|| !int.TryParse(positionals[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y))
					throw new UsageException("Usage: cnative agent tap <x> <y>");
				return new AgentInvocation(verb, serial, null, x, y, null, running);
			case "type":
				if (positionals.Count == 0)
					throw new UsageException("Usage: cnative agent type <text>");
				return new AgentInvocation(verb, serial, null, 0, 0, string.Join(' ', positionals), running);
			case "press":
				if (positionals.Count != 1)
					throw new UsageException("Usage: cnative agent press <id>");
				return new AgentInvocation(verb, serial, positionals[0], 0, 0, null, running);
			case "enter":
				if (positionals.Count < 2)
					throw new UsageException("Usage: cnative agent enter <id> <text>");
				return new AgentInvocation(verb, serial, positionals[0], 0, 0, string.Join(' ', positionals.Skip(1)), running);
			default:
				throw new UsageException("Usage: cnative agent tree|state|screenshot|tap|type|press|enter|a11y|layout|logs|clock");
		}
	}

	static int Query(HotReloadConnection connection, byte[] payload)
	{
		var reply = connection.Apply(payload);
		if (!reply.Ok)
		{
			Console.Error.WriteLine(reply.Text);
			return 1;
		}

		Console.WriteLine(reply.Text);
		return 0;
	}

	static int Screenshot(string serial, string path)
	{
		var start = new ProcessStartInfo("adb")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			StandardOutputEncoding = Encoding.Latin1,
		};
		start.ArgumentList.Add("-s");
		start.ArgumentList.Add(serial);
		start.ArgumentList.Add("exec-out");
		start.ArgumentList.Add("screencap");
		start.ArgumentList.Add("-p");
		using var process = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
		var bytes = Encoding.Latin1.GetBytes(process.StandardOutput.ReadToEnd());
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		File.WriteAllBytes(path, bytes);
		if (process.ExitCode != 0 || bytes.Length < 8)
		{
			Console.Error.WriteLine(error);
			return 1;
		}

		Console.WriteLine(path);
		return 0;
	}
}
