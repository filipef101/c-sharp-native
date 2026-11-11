using System.Text;
using NativePoc;

namespace NativePoc.Devx;

static class DevChecks
{
	public static int Run(string[] args)
	{
		string? serial = null;
		if (args.Length < 2 || args[0] != "measure" || args[1] != "dev")
		{
			Console.Error.WriteLine("Usage: cnative measure dev [--device serial]");
			return 2;
		}

		for (var index = 2; index < args.Length; index++)
		{
			if (args[index] != "--device" || index + 1 >= args.Length)
			{
				Console.Error.WriteLine("Usage: cnative measure dev [--device serial]");
				return 2;
			}

			serial = args[++index];
		}

		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var log = new StringBuilder();
		var session = new ReloadSession();
		try
		{
			serial = AndroidLoop.ResolveSerial(serial);
			Line(log, "label=emulator-run");
			var rid = Abi.RidFromProperty(AndroidLoop.Adb(serial, "shell", "getprop", "ro.product.cpu.abi").Stdout);
			session.Token = AndroidLoop.DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			Storage(session, serial, repo, log);
			Overlay(session, serial, log);
			Motion(session, serial, repo, log);
			Effect(session, serial, repo, log);
			Line(log, "dev-checks=ok");
			return 0;
		}
		catch (Exception ex)
		{
			Line(log, "dev-checks=failed");
			Line(log, ex.Message);
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
		finally
		{
			Directory.CreateDirectory("/opt/cursor/artifacts/progress8");
			File.WriteAllText("/opt/cursor/artifacts/progress8/dev-checks.txt", log.ToString());
			session.Dispose();
		}
	}

	static void Storage(ReloadSession session, string serial, string repo, StringBuilder log)
	{
		Restart(session, serial, repo, wipeStore: true);
		Press(session, "packing");
		Press(session, "scroll/list/settings");
		var entered = session.Connection.Apply(ScreenQuery.EnterId("settings/name", "Ada"));
		if (!entered.Ok)
			throw new InvalidOperationException(entered.Text);
		Press(session, "settings/dark");
		var state = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
		if (!state.Ok || !state.Text.Contains("Name=416461", StringComparison.Ordinal) || !state.Text.Contains("Dark=true", StringComparison.Ordinal))
			throw new InvalidOperationException(state.Text);
		Restart(session, serial, repo, wipeStore: false);
		Press(session, "packing");
		Press(session, "scroll/list/settings");
		var again = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
		if (!again.Ok || !again.Text.Contains("Name=416461", StringComparison.Ordinal) || !again.Text.Contains("Dark=true", StringComparison.Ordinal))
			throw new InvalidOperationException(again.Text);
		var logs = AndroidLoop.Adb(serial, "logcat", "-d", "-t", "400", "-s", "NativePoc:I");
		if (!logs.Stdout.Contains("storage-load key=name", StringComparison.Ordinal) || !logs.Stdout.Contains("theme mode=dark", StringComparison.Ordinal))
			throw new InvalidOperationException("storage or theme was not logged");
		Line(log, "storage=Name survived force-stop after the state file was removed");
		Line(log, "theme=dark after restart");
	}

	static void Overlay(ReloadSession session, string serial, StringBuilder log)
	{
		var before = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		if (!before.Ok)
			throw new InvalidOperationException(before.Text);
		var source = SourceOf(before.Text);
		var refused = """
			namespace Demo;
			public sealed class Box
			{
				public string Title { get; set; } = "";
			}
			public static class BoxScreen
			{
				public static Element Render(Box state, IPlatform platform) =>
					new Text(state.Title.Trim()) { Key = "title" };
			}
			""";
		var lowered = SubsetLowerer.TryLower(refused);
		if (lowered.Ok || lowered.Diagnostics.Count == 0)
			throw new InvalidOperationException("The edit was not refused.");
		var diagnostic = lowered.Diagnostics[0];
		var fault = session.Connection.Apply(ScreenQuery.FaultNote(diagnostic.Id, diagnostic.Message, diagnostic.Start, diagnostic.End, diagnostic.Line, diagnostic.Column));
		if (!fault.Ok || !fault.Text.Contains("overlay id=" + diagnostic.Id, StringComparison.Ordinal))
			throw new InvalidOperationException(fault.Text);
		var during = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		if (!during.Ok || SourceOf(during.Text) != source)
			throw new InvalidOperationException("The refused edit replaced the tree.");
		var dump = Dump(serial);
		if (!dump.Contains(diagnostic.Message, StringComparison.Ordinal) || !dump.Contains("Dismiss", StringComparison.Ordinal))
			throw new InvalidOperationException("The overlay was not on screen.");
		var motion = SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(RepoRoot.Find(Directory.GetCurrentDirectory()), "samples", "NativePoc.Motion", "MotionScreen.cs")));
		var broken = Corrupt(motion.Program ?? throw new InvalidOperationException("Motion did not lower."));
		var failed = session.Connection.Apply(broken);
		if (failed.Ok || !failed.Text.Contains("Rejected record program", StringComparison.Ordinal))
			throw new InvalidOperationException(failed.Text);

		var kept = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		if (!kept.Ok || SourceOf(kept.Text) != source)
			throw new InvalidOperationException("The render failure replaced the tree.");
		var logs = AndroidLoop.Adb(serial, "logcat", "-d", "-t", "400", "-s", "NativePoc:I");
		if (!logs.Stdout.Contains("dev-overlay id=render", StringComparison.Ordinal) || !logs.Stdout.Contains("dev-overlay id=" + diagnostic.Id, StringComparison.Ordinal))
			throw new InvalidOperationException("The overlay was not logged.");
		var dismiss = session.Connection.Apply(ScreenQuery.DismissOverlay());
		if (!dismiss.Ok || !dismiss.Text.Contains("overlay dismissed", StringComparison.Ordinal))
			throw new InvalidOperationException(dismiss.Text);
		Line(log, "overlay=refused edit and render failure, last tree kept, dismiss cleared it");
	}

	static void Motion(ReloadSession session, string serial, string repo, StringBuilder log)
	{
		var source = File.ReadAllText(Path.Combine(repo, "samples", "NativePoc.Motion", "MotionScreen.cs"));
		var lowered = SubsetLowerer.TryLower(source);
		if (!lowered.Ok || lowered.Program is null)
			throw new InvalidOperationException("Motion did not lower.");
		AndroidLoop.Adb(serial, "shell", "rm", "-f", "/sdcard/motion.mp4");
		var record = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
		{
			FileName = "adb",
			Arguments = "-s " + serial + " shell screenrecord --time-limit 3 /sdcard/motion.mp4",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		}) ?? throw new InvalidOperationException("screenrecord did not start.");
		Thread.Sleep(400);
		var applied = session.Connection.Apply(lowered.Program);
		if (!applied.Ok)
			throw new InvalidOperationException(applied.Text);
		Thread.Sleep(500);
		var reloaded = session.Connection.Apply(lowered.Program);
		if (!reloaded.Ok)
			throw new InvalidOperationException(reloaded.Text);
		Thread.Sleep(1600);
		var still = SubsetLowerer.TryLower("""
			namespace Demo;
			public sealed class StillState
			{
				public string Label { get; set; } = "Still";
			}
			public static class StillScreen
			{
				public static Element Render(StillState state, IPlatform platform) =>
					new Text(state.Label) { Key = "rise" };
			}
			""");
		if (!still.Ok || still.Program is null)
			throw new InvalidOperationException("The still screen did not lower.");
		var parked = session.Connection.Apply(still.Program);
		if (!parked.Ok)
			throw new InvalidOperationException(parked.Text);
		var stepped = session.Connection.Apply(lowered.Program);
		if (!stepped.Ok)
			throw new InvalidOperationException(stepped.Text);
		var first = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Clock));
		if (!first.Ok || !first.Text.Contains("animations=1", StringComparison.Ordinal))
			throw new InvalidOperationException(first.Text);
		var mid = "";
		for (var step = 0; step < 12; step++)
			mid = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Clock)).Text;
		if (!mid.Contains("animations=0", StringComparison.Ordinal))
			throw new InvalidOperationException(mid);
		record.WaitForExit(8000);
		var pull = AndroidLoop.Adb(serial, "pull", "/sdcard/motion.mp4", "/opt/cursor/artifacts/progress8/motion.mp4");
		if (pull.Code != 0)
			throw new InvalidOperationException(pull.Stderr);
		Line(log, "motion=clock stepped a 900 ms opacity/translate/scale run to completion, then reloaded");
		Line(log, "capture=/opt/cursor/artifacts/progress8/motion.mp4");
	}

	static void Effect(ReloadSession session, string serial, string repo, StringBuilder log)
	{
		var source = """
			namespace Demo;
			public sealed class Boom
			{
				public string Banner { get; set; } = "loading";
			}
			public static class BoomScreen
			{
				public const string ScreenId = "boom";
				public static Element Render(Boom state, IPlatform platform)
				{
					platform.Effect("Banner", "throw");
					return new Text(state.Banner) { Key = "banner" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		if (!lowered.Ok || lowered.Program is null)
			throw new InvalidOperationException("The effect screen did not lower.");
		var before = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		var applied = session.Connection.Apply(lowered.Program);
		if (!applied.Ok)
			throw new InvalidOperationException(applied.Text);
		var overlay = false;
		for (var attempt = 0; attempt < 20 && !overlay; attempt++)
		{
			Thread.Sleep(100);
			var logs = AndroidLoop.Adb(serial, "logcat", "-d", "-t", "200", "-s", "NativePoc:I");
			overlay = logs.Stdout.Contains("dev-overlay id=effect span=throw", StringComparison.Ordinal);
		}

		if (!overlay)
			throw new InvalidOperationException("The effect overlay was not logged.");
		var during = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		if (!during.Ok || !during.Text.Contains("banner", StringComparison.Ordinal))
			throw new InvalidOperationException(during.Text);
		_ = before;
		var cleared = session.Connection.Apply(SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(repo, "samples", "NativePoc.Motion", "MotionScreen.cs"))).Program!);
		if (!cleared.Ok)
			throw new InvalidOperationException(cleared.Text);
		var logsAfter = AndroidLoop.Adb(serial, "logcat", "-d", "-t", "200", "-s", "NativePoc:I");
		if (!logsAfter.Stdout.Contains("dev-overlay clear", StringComparison.Ordinal))
			throw new InvalidOperationException("A successful reload did not clear the overlay.");
		Line(log, "effect-overlay=cleared on the next successful reload");
	}

	static void Restart(ReloadSession session, string serial, string repo, bool wipeStore)
	{
		session.Dispose();
		var resolved = AndroidLoop.Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
		var component = ActivityName.FromResolve(resolved.Stdout)
			?? throw new InvalidOperationException(resolved.Stdout);
		foreach (var command in LaunchCommand.BeforeStart(LaunchCommand.Package, component))
		{
			AndroidLoop.Adb(serial, command);
			if (command.Length > 2 && command[2] == "force-stop")
			{
				AndroidLoop.Adb(serial, "shell", "run-as", LaunchCommand.Package, "rm", "-f", "files/nativepoc-state.bin");
				if (wipeStore)
					AndroidLoop.Adb(serial, "shell", "run-as", LaunchCommand.Package, "rm", "-f", "shared_prefs/nativepoc-settings.xml");
			}
		}
		session.Token = AndroidLoop.WaitToken(serial);
		session.Open(repo, serial);
	}

	static void Press(ReloadSession session, string id)
	{
		var last = "";
		for (var attempt = 0; attempt < 8; attempt++)
		{
			var pressed = session.Connection.Apply(ScreenQuery.PressId(id));
			if (pressed.Ok && pressed.Text.Contains("pressed " + id, StringComparison.Ordinal))
				return;
			last = pressed.Text;
			Thread.Sleep(200);
		}

		throw new InvalidOperationException("The control was not pressed: " + id + " " + last);
	}

	static string Dump(string serial)
	{
		AndroidLoop.Adb(serial, "shell", "uiautomator", "dump", "/sdcard/dev-overlay.xml");
		var read = AndroidLoop.Adb(serial, "shell", "cat", "/sdcard/dev-overlay.xml");
		return read.Stdout;
	}

	static string SourceOf(string tree)
	{
		var line = tree.Split('\n')[0].Trim();
		const string prefix = "source=";
		if (!line.StartsWith(prefix, StringComparison.Ordinal))
			throw new InvalidOperationException(tree);
		return line[prefix.Length..];
	}

	static byte[] Corrupt(byte[] program)
	{
		var copy = program.ToArray();
		var cursor = 6;
		var strings = ReadU16(copy, ref cursor);
		for (var index = 0; index < strings; index++)
		{
			var length = ReadU16(copy, ref cursor);
			cursor += length;
		}

		var props = ReadU16(copy, ref cursor);
		for (var index = 0; index < props; index++)
			SkipProp(copy, ref cursor);
		var code = ReadU16(copy, ref cursor);
		if (code < 1 || cursor >= copy.Length)
			throw new InvalidOperationException("The program has no code to break.");
		copy[cursor] = 255;
		return copy;
	}

	static void SkipProp(byte[] program, ref int cursor)
	{
		var type = program[cursor++];
		_ = ReadU16(program, ref cursor);
		switch (type)
		{
			case 1:
			case 5:
				_ = ReadU16(program, ref cursor);
				return;
			case 2:
				cursor++;
				return;
			case 3:
				cursor += 4;
				return;
			case 4:
				var fields = ReadU16(program, ref cursor);
				cursor += fields * 2;
				var rows = ReadU16(program, ref cursor);
				cursor += rows * fields * 2;
				return;
			default:
				throw new InvalidOperationException("The program property was not skipped.");
		}
	}

	static int ReadU16(byte[] program, ref int cursor)
	{
		var value = program[cursor] | (program[cursor + 1] << 8);
		cursor += 2;
		return value;
	}

	static void Line(StringBuilder log, string text)
	{
		log.AppendLine(text);
		Console.WriteLine(text);
	}
}
