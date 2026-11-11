using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class MeasureLoop
{
	public static int Run(string[] args)
	{
		if (args.Length > 1 && args[1] == "packing")
			return Packing(args);
		int samples;
		string? serial;
		try
		{
			(samples, serial) = Parse(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 2;
		}

		try
		{
			return Execute(samples, serial);
		}
		catch (Exception ex)
		{
			try
			{
				Console.Error.WriteLine("cnative: " + ex.GetType().Name + " " + ex.Message);
			}
			catch (Exception write) when (write is IOException or ArgumentException)
			{
				File.AppendAllText("/opt/cursor/artifacts/measure-error.txt", ex + "\n" + write);
			}

			return 1;
		}
	}

	static (int Samples, string? Serial) Parse(IReadOnlyList<string> args)
	{
		if (args.Count < 2 || args[0] != "measure" || args[1] != "android")
			throw new UsageException("Usage: cnative measure android [--samples N] [--device serial]");
		var samples = 12;
		string? serial = null;
		for (var index = 2; index < args.Count; index++)
		{
			switch (args[index])
			{
				case "--samples":
					if (index + 1 >= args.Count
						|| !int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out samples)
						|| samples < 10
						|| samples > 30)
						throw new UsageException("--samples must be a whole number from 10 to 30.");
					break;
				case "--device":
					if (index + 1 >= args.Count)
						throw new UsageException("--device needs a serial.");
					serial = args[++index];
					break;
				default:
					throw new UsageException("Usage: cnative measure android [--samples N] [--device serial]");
			}
		}

		return (samples, serial);
	}

	static int Execute(int samples, string? requestedSerial)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var screen = Path.Combine(repo, ScreenCompiler.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		var original = File.ReadAllText(screen);
		var serial = AndroidLoop.ResolveSerial(requestedSerial);
		var rid = Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		var log = new StringBuilder();
		Line(log, "clock=Screen.cs write, then compile, lower, socket apply, UI render, OnDraw, reply");
		Line(log, "present=the reply is written after PresentDraw.OnDraw on the decor view");
		Line(log, "label=emulator-run");
		Line(log, "abi=" + Prop(serial, "ro.product.cpu.abi"));
		Line(log, "sdk=" + Prop(serial, "ro.build.version.sdk"));
		Line(log, "boot=" + Prop(serial, "sys.boot_completed"));
		Line(log, "qemu=" + Prop(serial, "ro.kernel.qemu"));
		var session = new ReloadSession();
		try
		{
			session.Token = AndroidLoop.DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			var warmup = AndroidLoop.SendFile(session);
			if (!warmup.Applied || !warmup.Text.Contains("program=1", StringComparison.Ordinal) || !warmup.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a presented program: " + warmup.Text);
			var entered = session.Connection.Apply(ScreenQuery.EnterId("name", "Ada"));
			if (!entered.Ok)
				throw new InvalidOperationException("The name was not entered: " + entered.Text);
			var before = Ui(serial);
			if (!before.Contains("text=\"Ada\"", StringComparison.Ordinal))
				throw new InvalidOperationException("The EditText did not show Ada before the series.");
			var pid = Pid(serial);
			Line(log, "pid=" + pid);
			var timings = new double[samples];
			for (var index = 0; index < samples; index++)
			{
				var prefix = "P" + index.ToString("00", CultureInfo.InvariantCulture) + " ";
				var started = Stopwatch.GetTimestamp();
				File.WriteAllText(screen, WithPrefix(original, prefix));
				var pushed = AndroidLoop.SendFile(session);
				var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
				timings[index] = elapsed;
				var now = Pid(serial);
				var expected = prefix.TrimEnd() + " 0";
				var ok = pushed.Applied
					&& now == pid
					&& pushed.Text.StartsWith(expected, StringComparison.Ordinal)
					&& pushed.Text.Contains("program=1", StringComparison.Ordinal)
					&& pushed.Text.Contains("present=1", StringComparison.Ordinal);
				Line(log, "sample=" + index.ToString(CultureInfo.InvariantCulture)
					+ " ms=" + elapsed.ToString("F4", CultureInfo.InvariantCulture)
					+ " pid=" + now
					+ " reply=" + pushed.Text.Replace('\n', ' ')
					+ " ok=" + ok);
				if (!ok)
					throw new InvalidOperationException("Sample " + index.ToString(CultureInfo.InvariantCulture) + " did not present in place.");
			}

			Line(log, "samples-ms=" + string.Join(",", timings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "n=" + samples.ToString(CultureInfo.InvariantCulture));
			Line(log, "p50-ms=" + Percentile(timings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "p90-ms=" + Percentile(timings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "percentile=Hyndman-Fan type 7");
			Directory.CreateDirectory("/opt/cursor/artifacts");
			File.WriteAllText("/opt/cursor/artifacts/measure-screen-program.txt", log.ToString());
			var last = "P" + (samples - 1).ToString("00", CultureInfo.InvariantCulture) + " 0";
			var after = Ui(serial);
			if (!after.Contains("text=\"" + last + "\"", StringComparison.Ordinal) || !after.Contains("text=\"Ada\"", StringComparison.Ordinal))
				throw new InvalidOperationException("The UI dump did not keep Ada and show " + last + ".");
			Shot(serial, Path.Combine("/opt/cursor/artifacts", "screen-program-present.png"));
			var broken = original.Replace(
				"public const string TitlePrefix",
				"public static int Extra => 1;\n\tpublic const string TitlePrefix",
				StringComparison.Ordinal);
			File.WriteAllText(screen, broken);
			var refused = AndroidLoop.SendFile(session);
			var refusedPid = Pid(serial);
			var refusalOk = refused.Refused && refusedPid == pid && refused.Text.Contains("Extra", StringComparison.Ordinal);
			Line(log, "refusal ok=" + refusalOk + " pid=" + refusedPid + " text=" + refused.Text.Replace('\n', ' '));
			if (!refusalOk)
				throw new InvalidOperationException("The unsupported edit was not refused in place.");
			var still = Ui(serial);
			if (!still.Contains("text=\"" + last + "\"", StringComparison.Ordinal) || !still.Contains("text=\"Ada\"", StringComparison.Ordinal))
				throw new InvalidOperationException("The refusal changed the visible screen.");
			File.WriteAllText(screen, original);
			var restored = AndroidLoop.SendFile(session);
			if (!restored.Applied)
				throw new InvalidOperationException("The original screen was not restored: " + restored.Text);
			File.WriteAllText("/opt/cursor/artifacts/measure-screen-program.txt", log.ToString());
			return 0;
		}
		finally
		{
			if (!string.Equals(File.ReadAllText(screen), original, StringComparison.Ordinal))
				File.WriteAllText(screen, original);
			session.Dispose();
		}
	}

	static string WithPrefix(string source, string prefix)
	{
		const string marker = "public const string TitlePrefix = \"";
		var start = source.IndexOf(marker, StringComparison.Ordinal);
		if (start < 0)
			throw new InvalidOperationException("TitlePrefix was not found.");
		start += marker.Length;
		var end = source.IndexOf('"', start);
		if (end < 0)
			throw new InvalidOperationException("TitlePrefix was not closed.");
		return source[..start] + prefix + source[end..];
	}

	static double Percentile(IReadOnlyList<double> samples, int numerator, int denominator)
	{
		var ordered = samples.OrderBy(value => value).ToArray();
		var count = ordered.Length;
		var num = denominator + (count - 1) * numerator;
		var lo = num / denominator;
		var frac = num % denominator;
		if (lo < 1)
			lo = 1;
		var hi = frac > 0 ? lo + 1 : lo;
		if (hi > count)
			hi = count;
		return ordered[lo - 1] + (ordered[hi - 1] - ordered[lo - 1]) * frac / denominator;
	}

	static string Ui(string serial)
	{
		var dumped = AndroidLoop.Adb(serial, "shell", "uiautomator", "dump", "/sdcard/nativepoc-ui.xml");
		if (dumped.Code != 0)
			throw new InvalidOperationException("uiautomator dump failed: " + dumped.Stderr + dumped.Stdout);
		var local = Path.Combine(Path.GetTempPath(), "nativepoc-ui.xml");
		var pulled = AndroidLoop.Adb(serial, "pull", "/sdcard/nativepoc-ui.xml", local);
		if (pulled.Code != 0)
			throw new InvalidOperationException("uiautomator pull failed: " + pulled.Stderr + pulled.Stdout);
		return File.ReadAllText(local);
	}

	static void Shot(string serial, string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
		process.StandardError.ReadToEnd();
		process.WaitForExit();
		File.WriteAllBytes(path, bytes);
		if (process.ExitCode != 0 || bytes.Length < 8)
			throw new InvalidOperationException("screencap failed.");
	}

	static string Pid(string serial)
	{
		var result = AndroidLoop.Adb(serial, "shell", "pidof", "com.scratch.nativepoc");
		var pid = result.Stdout.Trim();
		if (pid.Length == 0)
			throw new InvalidOperationException("The app process is not running.");
		return pid;
	}

	static string Prop(string serial, string name)
	{
		var result = AndroidLoop.Adb(serial, "shell", "getprop", name);
		return result.Stdout.Trim();
	}

	static int Packing(string[] args)
	{
		int samples;
		string? serial;
		try
		{
			(samples, serial) = ParsePacking(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 2;
		}

		try
		{
			return ExecutePacking(samples, serial);
		}
		catch (Exception ex)
		{
			try
			{
				Console.Error.WriteLine("cnative: " + ex.GetType().Name + " " + ex.Message);
			}
			catch (Exception write) when (write is IOException or ArgumentException)
			{
				File.AppendAllText("/opt/cursor/artifacts/measure-error.txt", ex + "\n" + write);
			}

			return 1;
		}
	}

	static (int Samples, string? Serial) ParsePacking(IReadOnlyList<string> args)
	{
		if (args.Count < 2 || args[0] != "measure" || args[1] != "packing")
			throw new UsageException("Usage: cnative measure packing [--samples N] [--device serial]");
		var samples = 12;
		string? serial = null;
		for (var index = 2; index < args.Count; index++)
		{
			switch (args[index])
			{
				case "--samples":
					if (index + 1 >= args.Count
						|| !int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out samples)
						|| samples < 10
						|| samples > 30)
						throw new UsageException("--samples must be a whole number from 10 to 30.");
					break;
				case "--device":
					if (index + 1 >= args.Count)
						throw new UsageException("--device needs a serial.");
					serial = args[++index];
					break;
				default:
					throw new UsageException("Usage: cnative measure packing [--samples N] [--device serial]");
			}
		}

		return (samples, serial);
	}

	static int ExecutePacking(int samples, string? requestedSerial)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var screen = Path.Combine(repo, "samples", "NativePoc.Packing", "PackingScreen.cs");
		var original = File.ReadAllText(screen);
		var serial = AndroidLoop.ResolveSerial(requestedSerial);
		var rid = Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		var log = new StringBuilder();
		Line(log, "clock=PackingScreen.cs write, then lower, socket apply, UI render, OnDraw, reply");
		Line(log, "present=the reply is written after PresentDraw.OnDraw on the packing decor view");
		Line(log, "label=emulator-run");
		Line(log, "abi=" + Prop(serial, "ro.product.cpu.abi"));
		Line(log, "sdk=" + Prop(serial, "ro.build.version.sdk"));
		Line(log, "boot=" + Prop(serial, "sys.boot_completed"));
		Line(log, "qemu=" + Prop(serial, "ro.kernel.qemu"));
		var session = new ReloadSession();
		try
		{
			session.Token = AndroidLoop.DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			var warmup = AndroidLoop.SendFile(session);
			if (!warmup.Applied || !warmup.Text.Contains("program=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a program: " + warmup.Text);
			var packing = AndroidLoop.SendPacking(session, repo);
			if (!packing.Applied || !packing.Text.Contains("program=1", StringComparison.Ordinal))
				throw new InvalidOperationException("The packing program was not applied: " + packing.Text);
			StartPacking(serial);
			WaitUi(serial, "Canvas tent");
			PressPacked(session);
			var kept = Items(State(session));
			if (!kept.Contains(" true", StringComparison.Ordinal))
				throw new InvalidOperationException("The packed row was not kept: " + kept);
			var pid = Pid(serial);
			Line(log, "pid=" + pid);
			Line(log, "items-before=" + kept.Replace('\n', '|'));
			var timings = new double[samples];
			for (var index = 0; index < samples; index++)
			{
				var title = "P" + index.ToString("00", CultureInfo.InvariantCulture);
				var started = Stopwatch.GetTimestamp();
				File.WriteAllText(screen, WithRowTitle(original, title));
				var pushed = AndroidLoop.SendPacking(session, repo);
				var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
				timings[index] = elapsed;
				var now = Pid(serial);
				var ui = Ui(serial);
				var ok = pushed.Applied
					&& now == pid
					&& pushed.Text.Contains("program=1", StringComparison.Ordinal)
					&& pushed.Text.Contains("present=1", StringComparison.Ordinal)
					&& ui.Contains("text=\"" + title + "\"", StringComparison.Ordinal)
					&& Items(State(session)) == kept;
				Line(log, "sample=" + index.ToString(CultureInfo.InvariantCulture)
					+ " ms=" + elapsed.ToString("F4", CultureInfo.InvariantCulture)
					+ " pid=" + now
					+ " reply=" + pushed.Text.Replace('\n', ' ')
					+ " ok=" + ok);
				if (!ok)
					throw new InvalidOperationException("Sample " + index.ToString(CultureInfo.InvariantCulture) + " did not present in place.");
			}

			Line(log, "samples-ms=" + string.Join(",", timings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "n=" + samples.ToString(CultureInfo.InvariantCulture));
			Line(log, "p50-ms=" + Percentile(timings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "p90-ms=" + Percentile(timings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "percentile=Hyndman-Fan type 7");
			var noted = WithNote(original);
			File.WriteAllText(screen, noted);
			var note = AndroidLoop.SendPacking(session, repo);
			var noteTree = Tree(session);
			var noteOk = note.Applied && Pid(serial) == pid && note.Text.Contains("present=1", StringComparison.Ordinal)
				&& noteTree.Contains("id=\"list/tent/note\"", StringComparison.Ordinal)
				&& noteTree.Contains("value=\"Note\"", StringComparison.Ordinal)
				&& Items(State(session)) == kept;
			Line(log, "new-field ok=" + noteOk + " pid=" + Pid(serial));
			if (!noteOk)
				throw new InvalidOperationException("The added text did not land in place: " + note.Text);
			Shot(serial, "/opt/cursor/artifacts/packing-program-note.png");
			File.WriteAllText(screen, SwapRow(original));
			var swapped = AndroidLoop.SendPacking(session, repo);
			var swappedTree = Tree(session);
			var editAt = swappedTree.IndexOf("id=\"list/tent/edit\"", StringComparison.Ordinal);
			var nameAt = swappedTree.IndexOf("id=\"list/tent/name\"", StringComparison.Ordinal);
			var swapOk = swapped.Applied && Pid(serial) == pid && editAt >= 0 && nameAt > editAt && Items(State(session)) == kept;
			Line(log, "layout ok=" + swapOk + " edit-at=" + editAt.ToString(CultureInfo.InvariantCulture) + " name-at=" + nameAt.ToString(CultureInfo.InvariantCulture));
			if (!swapOk)
				throw new InvalidOperationException("The row order did not land in place.");
			var broken = original.Replace(
				"public static Element View(PackingItem item",
				"public static int Extra => 1;\n\tpublic static Element View(PackingItem item",
				StringComparison.Ordinal);
			File.WriteAllText(screen, broken);
			var refused = AndroidLoop.SendPacking(session, repo);
			var refusalOk = refused.Refused && Pid(serial) == pid && refused.Text.Contains("Extra", StringComparison.Ordinal) && Items(State(session)) == kept;
			Line(log, "refusal ok=" + refusalOk + " pid=" + Pid(serial) + " text=" + refused.Text.Replace('\n', ' '));
			if (!refusalOk)
				throw new InvalidOperationException("The unsupported edit was not refused in place.");
			File.WriteAllText(screen, original);
			var restored = AndroidLoop.SendPacking(session, repo);
			if (!restored.Applied || Pid(serial) != pid || Items(State(session)) != kept)
				throw new InvalidOperationException("The original packing screen was not restored: " + restored.Text);
			Directory.CreateDirectory("/opt/cursor/artifacts");
			File.WriteAllText("/opt/cursor/artifacts/measure-packing-program.txt", log.ToString());
			return 0;
		}
		finally
		{
			if (!string.Equals(File.ReadAllText(screen), original, StringComparison.Ordinal))
				File.WriteAllText(screen, original);
			session.Dispose();
		}
	}

	static string WithRowTitle(string source, string title)
	{
		const string needle = "new Button(\"Edit\", onEdit,";
		var index = source.IndexOf(needle, StringComparison.Ordinal);
		if (index < 0)
			throw new InvalidOperationException("The row edit button was not found.");
		return source.Replace(needle, "new Button(\"" + title + "\", onEdit,", StringComparison.Ordinal);
	}

	static string WithNote(string source)
	{
		var lines = source.Split('\n');
		for (var index = 0; index < lines.Length; index++)
		{
			if (!lines[index].Contains("new Text(item.Name)", StringComparison.Ordinal))
				continue;
			var indent = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
			var extra = indent + "new Text(\"Note\") { Key = \"note\" },";
			var rewritten = new string[lines.Length + 1];
			Array.Copy(lines, rewritten, index + 1);
			rewritten[index + 1] = extra;
			Array.Copy(lines, index + 1, rewritten, index + 2, lines.Length - index - 1);
			return string.Join('\n', rewritten);
		}

		throw new InvalidOperationException("The row name was not found.");
	}

	static string SwapRow(string source)
	{
		var lines = source.Split('\n');
		for (var index = 0; index < lines.Length - 2; index++)
		{
			if (!lines[index].Contains("new Text(item.Name)", StringComparison.Ordinal))
				continue;
			if (!lines[index + 1].Contains("new Switch(item.Packed", StringComparison.Ordinal))
				continue;
			if (!lines[index + 2].Contains("new Button(\"Edit\"", StringComparison.Ordinal))
				continue;
			(lines[index], lines[index + 2]) = (lines[index + 2], lines[index]);
			return string.Join('\n', lines);
		}

		throw new InvalidOperationException("The row layout was not found.");
	}

	static void StartPacking(string serial)
	{
		var started = AndroidLoop.Adb(serial, "shell", "am", "start", "-n", "com.scratch.nativepoc/nativepoc.PackingActivity");
		if (started.Code != 0)
			throw new InvalidOperationException("Packing did not start: " + started.Stderr + started.Stdout);
	}

	static string WaitUi(string serial, string needle)
	{
		var last = "";
		for (var attempt = 0; attempt < 20; attempt++)
		{
			last = Ui(serial);
			if (last.Contains(needle, StringComparison.Ordinal))
				return last;
			Thread.Sleep(250);
		}

		throw new InvalidOperationException("The UI did not show " + needle + ".");
	}

	static void PressPacked(ReloadSession session)
	{
		var last = "";
		for (var attempt = 0; attempt < 10; attempt++)
		{
			var pressed = session.Connection.Apply(ScreenQuery.PressId("list/tent/packed"));
			if (pressed.Ok && pressed.Text.Contains("pressed list/tent/packed", StringComparison.Ordinal))
				return;
			last = pressed.Text;
			Thread.Sleep(250);
		}

		throw new InvalidOperationException("The packed switch was not pressed: " + last);
	}

	static string State(ReloadSession session)
	{
		var state = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
		if (!state.Ok)
			throw new InvalidOperationException("State was not read: " + state.Text);
		return state.Text;
	}

	static string Tree(ReloadSession session)
	{
		var tree = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
		if (!tree.Ok)
			throw new InvalidOperationException("The tree was not read: " + tree.Text);
		return tree.Text;
	}

	static string Items(string state)
	{
		var builder = new StringBuilder();
		foreach (var line in state.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
		{
			if (line.StartsWith("item=", StringComparison.Ordinal))
				builder.AppendLine(line);
		}

		return builder.ToString();
	}

	static void Line(StringBuilder log, string text)
	{
		log.AppendLine(text);
		try
		{
			Console.WriteLine(text);
		}
		catch (Exception ex) when (ex is IOException or ArgumentException)
		{
			log.AppendLine("console-write " + ex.GetType().Name);
		}
	}
}
