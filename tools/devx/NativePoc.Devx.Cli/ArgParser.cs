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
	bool RestartOnRude,
	bool Listener)
{
	public const int DefaultDebounceMs = 40;
}

static class ArgParser
{
	public const string Usage =
		"""
		cnative new <name>
		cnative doctor
		cnative reset-state [--device <serial>]
		cnative run android [options]

		  cnative doctor checks this machine. It prints OK or FAIL for the
		  .NET 10 SDK, the android workload, adb, an online emulator or
		  device, and the NativeAOT toolchain. A FAIL line names the next
		  command. It does not build or install the app.

		  Build Debug, install it on the running emulator or device, launch it,
		  and push Screen.cs edits. The emulator or device must already be online.

		  src/NativePoc.Screen/Screen.cs, a samples/<name>/*Screen.cs file, and a
		  shared component beside those screens reload in place. A save under
		  src/NativePoc.Core, src/NativePoc.Android, or a [ReloadCallable] source
		  asks before a restart. A method body in the call table stays the
		  previous body until that relaunch.
		  "reloaded in" is the whole push. "apply" is the device exchange.
		  A screen edit is lowered to a program the running process interprets.
		  An edit the program cannot carry is refused. cnative prints
		  file:line:column: id: message and leaves the last program up.
		  When a live value differs from the initializer in the edit, cnative
		  prints one "state kept" line. cnative reset-state clears the bags
		  and files/nativepoc-state.bin, then shows those initializers.
		  --restart-on-rude relaunches the installed process and restores
		  state, then applies the last program that lowered. A Core or
		  Android edit is rebuilt before that relaunch.

		  cnative agent tree|state
		  cnative agent screenshot <png>
		  cnative agent tap <x> <y>
		  cnative agent type <text>
		  cnative agent press <id>
		  cnative agent swipe <id>
		  cnative agent enter <id> <text>
		  cnative agent submit <id>
		  cnative agent a11y [--check]|layout|logs|clock
		    a11y is the accessibility tree. --check reports touch targets
		    under 48dp, unlabeled controls, TalkBack order reversals,
		    clipped text, and how many headings it found.
		    layout is bounds per id.
		    logs is the NativePoc logcat. clock is 0 until a screen calls Every.
		    --running drives the program on screen, not only Screen.cs.
		  cnative lower <file> [--json] [--bytes] [--apply]
		    Lower any screen in the documented subset. --json prints
		    diagnostics with stable ids and source spans.
		  cnative test android
		    Run tests/device/*.app.test. A step is screen, enter, press,
		    state, tree, or a11y. type is enter. tap is press.
		  A drive refuses a Screen.cs hash that does not match the running
		  program and names: tools/devx/cnative run android --once

		  --device <serial>     adb serial. Default: the only online device.
		  --abi <rid>           android-x64 or android-arm64. Default: the device ABI.
		  --listener            publish the Debug NativeAOT listener for that ABI.
		  --debounce <ms>       quiet period before a reload. Default: 40.
		  --restart-on-rude     relaunch when an edit cannot be applied.
		                        A Core, Android, or call-table edit is rebuilt first.
		  --once                push the current screens once and exit.
		                    A samples/<name> screen from cnative new is shown too.
		  --help                show this text.
		""";

	public static CliOptions Parse(IReadOnlyList<string> args)
	{
		if (args.Count == 0)
			throw new UsageException("Usage: cnative run android");
		if (args.Count == 1 && IsHelp(args[0]))
			return new CliOptions(true, false, null, null, CliOptions.DefaultDebounceMs, false, false);
		if (args[0] != "run" || args.Count < 2 || args[1] != "android")
			throw new UsageException("Usage: cnative run android");

		var help = false;
		var once = false;
		string? serial = null;
		string? abi = null;
		var debounce = CliOptions.DefaultDebounceMs;
		var restart = false;
		var listener = false;
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
				case "--listener":
					listener = true;
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

		return new CliOptions(help, once, serial, abi, debounce, restart, listener);
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
