namespace NativePoc.Devx;

sealed class UsageException : Exception
{
	public UsageException(string message) : base(message)
	{
	}
}

sealed record CliOptions(
	bool Help,
	bool Once,
	string? Serial,
	string? Abi,
	int DebounceMs,
	bool RestartOnRude)
{
	public const int DefaultDebounceMs = 40;
}

static class ArgParser
{
	public const string Usage =
		"""
		cnative run android [options]

		  Build Debug, install it on the running emulator or device, launch it,
		  and push Screen.cs edits. The emulator or device must already be online.

		  Only src/NativePoc.Screen/Screen.cs reloads in place. A save under
		  src/NativePoc.Core or src/NativePoc.Android asks before a restart.
		  "reloaded in" is the whole push. "apply" is the device exchange.
		  A screen edit is lowered to a program the running process interprets.
		  An edit the program cannot carry is refused and the process stays up.

		  cnative agent tree|state
		  cnative agent screenshot <png>
		  cnative agent tap <x> <y>
		  cnative agent type <text>

		  --device <serial>     adb serial. Default: the only online device.
		  --abi <rid>           android-x64 or android-arm64. Default: the device ABI.
		  --debounce <ms>       quiet period before a reload. Default: 40.
		  --restart-on-rude     rebuild and relaunch when an edit needs a restart.
		  --once                push the current screen once and exit.
		  --help                show this text.
		""";

	public static CliOptions Parse(IReadOnlyList<string> args)
	{
		if (args.Count == 0)
			throw new UsageException("Usage: cnative run android");
		if (args.Count == 1 && IsHelp(args[0]))
			return new CliOptions(true, false, null, null, CliOptions.DefaultDebounceMs, false);
		if (args[0] != "run" || args.Count < 2 || args[1] != "android")
			throw new UsageException("Usage: cnative run android");

		var help = false;
		var once = false;
		string? serial = null;
		string? abi = null;
		var debounce = CliOptions.DefaultDebounceMs;
		var restart = false;
		for (var i = 2; i < args.Count; i++)
		{
			var arg = args[i];
			if (IsHelp(arg))
			{
				help = true;
				continue;
			}

			switch (arg)
			{
				case "--once":
					once = true;
					break;
				case "--restart-on-rude":
					restart = true;
					break;
				case "--device":
					serial = Take(args, ref i, "--device");
					break;
				case "--abi":
					abi = Take(args, ref i, "--abi");
					if (abi is not ("android-x64" or "android-arm64"))
						throw new UsageException("--abi must be android-x64 or android-arm64.");
					break;
				case "--debounce":
					var text = Take(args, ref i, "--debounce");
					if (!int.TryParse(text, out debounce) || debounce < 1 || debounce > 60_000)
						throw new UsageException("--debounce must be a whole number of milliseconds from 1 to 60000.");
					break;
				default:
					throw new UsageException("Unknown argument: " + arg);
			}
		}

		return new CliOptions(help, once, serial, abi, debounce, restart);
	}

	static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";

	static string Take(IReadOnlyList<string> args, ref int index, string name)
	{
		if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
			throw new UsageException(name + " needs a value.");
		index++;
		return args[index];
	}
}
