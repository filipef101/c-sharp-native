using System.Diagnostics;
using System.Globalization;
using System.Text;
using NativePoc;

namespace NativePoc.Devx;

static partial class MeasureLoop
{
	public static int Run(string[] args)
	{
		if (args.Length > 1 && args[1] == "packing")
			return Packing(args);
		if (args.Length > 1 && args[1] == "program-only")
			return ProgramOnly(args);
		if (args.Length > 1 && args[1] == "checkpoint")
			return Checkpoint(args);
		if (args.Length > 1 && args[1] == "aot")
			return Aot(args);
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
		Line(log, "frame-clock=same write until gfxinfo FrameCompleted advances");
		Line(log, "framebuffer-clock=same write until screencap bytes change");
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
			Line(log, "source-hash-bytes=16");
			var timings = new double[samples];
			var frameTimings = new double[samples];
			var pixelTimings = new double[samples];
			for (var index = 0; index < samples; index++)
			{
				var prefix = "P" + index.ToString("00", CultureInfo.InvariantCulture) + " ";
				ResetFrames(serial);
				var frameBefore = LatestFrame(serial);
				var pixelsBefore = Capture(serial);
				var started = Stopwatch.GetTimestamp();
				File.WriteAllText(screen, WithPrefix(original, prefix));
				var pushed = AndroidLoop.SendFile(session);
				var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
				timings[index] = elapsed;
				var frameMs = -1.0;
				var pixelMs = -1.0;
				var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
				while ((frameMs < 0 || pixelMs < 0) && Stopwatch.GetTimestamp() < deadline)
				{
					if (frameMs < 0 && LatestFrame(serial) > frameBefore)
						frameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
					if (pixelMs < 0 && !Capture(serial).AsSpan().SequenceEqual(pixelsBefore))
						pixelMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
				}

				frameTimings[index] = frameMs;
				pixelTimings[index] = pixelMs;
				var now = Pid(serial);
				var expected = prefix.TrimEnd() + " 0";
				var ok = pushed.Applied
					&& now == pid
					&& pushed.Text.StartsWith(expected, StringComparison.Ordinal)
					&& pushed.Text.Contains("program=1", StringComparison.Ordinal)
					&& pushed.Text.Contains("present=1", StringComparison.Ordinal)
					&& frameMs >= 0
					&& pixelMs >= 0;
				Line(log, "sample=" + index.ToString(CultureInfo.InvariantCulture)
					+ " ondraw-ms=" + elapsed.ToString("F4", CultureInfo.InvariantCulture)
					+ " frame-ms=" + frameMs.ToString("F4", CultureInfo.InvariantCulture)
					+ " framebuffer-ms=" + pixelMs.ToString("F4", CultureInfo.InvariantCulture)
					+ " pid=" + now
					+ " reply=" + pushed.Text.Replace('\n', ' ')
					+ " ok=" + ok);
				if (!ok)
					throw new InvalidOperationException("Sample " + index.ToString(CultureInfo.InvariantCulture) + " did not present in place.");
			}

			Line(log, "samples-ondraw-ms=" + string.Join(",", timings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "samples-frame-ms=" + string.Join(",", frameTimings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "samples-framebuffer-ms=" + string.Join(",", pixelTimings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "n=" + samples.ToString(CultureInfo.InvariantCulture));
			Line(log, "ondraw-p50-ms=" + Percentile(timings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "ondraw-p90-ms=" + Percentile(timings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "frame-p50-ms=" + Percentile(frameTimings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "frame-p90-ms=" + Percentile(frameTimings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "framebuffer-p50-ms=" + Percentile(pixelTimings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "framebuffer-p90-ms=" + Percentile(pixelTimings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "percentile=Hyndman-Fan type 7");
			Line(log, "frame-timestamp=gfxinfo FrameCompleted. DisplayPresentTime and SurfaceFlinger --latency are 0 on this swiftshader emulator.");
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
		var bytes = Capture(serial);
		File.WriteAllBytes(path, bytes);
	}

	static byte[] Capture(string serial)
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
		process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0 || bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != (byte)'P' || bytes[2] != (byte)'N' || bytes[3] != (byte)'G')
			throw new InvalidOperationException("screencap failed (" + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes).");
		return bytes;
	}

	static void ResetFrames(string serial)
	{
		var dumped = AndroidLoop.Adb(serial, "shell", "dumpsys", "gfxinfo", "com.scratch.nativepoc", "reset");
		if (dumped.Code != 0)
			throw new InvalidOperationException("gfxinfo reset failed: " + dumped.Stderr + dumped.Stdout);
	}

	static long LatestFrame(string serial)
	{
		var dumped = AndroidLoop.Adb(serial, "shell", "dumpsys", "gfxinfo", "com.scratch.nativepoc", "framestats");
		if (dumped.Code != 0)
			throw new InvalidOperationException("gfxinfo failed: " + dumped.Stderr + dumped.Stdout);
		long latest = 0;
		var inTable = false;
		foreach (var raw in dumped.Stdout.Split('\n'))
		{
			var line = raw.TrimEnd('\r');
			if (line == "---PROFILEDATA---")
			{
				inTable = !inTable;
				continue;
			}

			if (!inTable || line.StartsWith("Flags,", StringComparison.Ordinal) || line.Length == 0)
				continue;
			var fields = line.Split(',');
			if (fields.Length < 17)
				continue;
			if (long.TryParse(fields[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out var completed) && completed > latest)
				latest = completed;
		}

		return latest;
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

	static int ProgramOnly(string[] args)
	{
		string? serial;
		try
		{
			serial = ParseProgramOnly(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 2;
		}

		try
		{
			return ExecuteProgramOnly(serial);
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

	static string? ParseProgramOnly(IReadOnlyList<string> args)
	{
		if (args.Count < 2 || args[0] != "measure" || args[1] != "program-only")
			throw new UsageException("Usage: cnative measure program-only [--device serial]");
		string? serial = null;
		for (var index = 2; index < args.Count; index++)
		{
			switch (args[index])
			{
				case "--device":
					if (index + 1 >= args.Count)
						throw new UsageException("--device needs a serial.");
					serial = args[++index];
					break;
				default:
					throw new UsageException("Usage: cnative measure program-only [--device serial]");
			}
		}

		return serial;
	}

	static int ExecuteProgramOnly(string? requestedSerial)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var screen = Path.Combine(repo, ScreenCompiler.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		var original = File.ReadAllText(screen);
		var serial = AndroidLoop.ResolveSerial(requestedSerial);
		var rid = Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		var dll = Path.Combine(repo, "src", "NativePoc.Android", "bin", "Debug", "net10.0-android", rid, "NativePoc.Android.dll");
		var log = new StringBuilder();
		Line(log, "label=emulator-run for the edit; the loader scan is the built assembly");
		Line(log, "variant=Debug NativePocProgramOnly, listener compiled in, assembly loader compiled out");
		Line(log, "proxy=closest Android stand-in for an iOS NativeAOT process. This is not an iOS run.");
		Line(log, "abi=" + Prop(serial, "ro.product.cpu.abi"));
		Line(log, "sdk=" + Prop(serial, "ro.build.version.sdk"));
		Line(log, "qemu=" + Prop(serial, "ro.kernel.qemu"));
		var session = new ReloadSession();
		try
		{
			BuildAndroid(repo, rid, programOnly: false, trimAnalyzer: false);
			var loaderBefore = CountText(dll, "AssemblyLoadContext") + CountText(dll, "LoadFromStream");
			Line(log, "default-build loader-hits=" + loaderBefore.ToString(CultureInfo.InvariantCulture));
			if (loaderBefore == 0)
				throw new InvalidOperationException("The default Debug assembly did not contain the loader.");
			BuildAndroid(repo, rid, programOnly: true, trimAnalyzer: true);
			var loaderAfter = CountText(dll, "AssemblyLoadContext") + CountText(dll, "LoadFromStream");
			Line(log, "program-only-build loader-hits=" + loaderAfter.ToString(CultureInfo.InvariantCulture) + " bytes=" + new FileInfo(dll).Length.ToString(CultureInfo.InvariantCulture));
			if (loaderAfter != 0)
				throw new InvalidOperationException("The program-only assembly still contains the loader.");
			session.Token = AndroidLoop.DeployAndLaunch(
				repo,
				rid,
				serial,
				["-p:NativePocProgramOnly=true", "-p:EnableTrimAnalyzer=true"]);
			var installed = CountText(dll, "AssemblyLoadContext") + CountText(dll, "LoadFromStream");
			Line(log, "installed loader-hits=" + installed.ToString(CultureInfo.InvariantCulture));
			if (installed != 0)
				throw new InvalidOperationException("The installed assembly still contains the loader.");
			session.Open(repo, serial);
			var warmup = AndroidLoop.SendFile(session);
			if (!warmup.Applied || !warmup.Text.Contains("program=1", StringComparison.Ordinal) || !warmup.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a presented program: " + warmup.Text);
			var entered = session.Connection.Apply(ScreenQuery.EnterId("name", "Ada"));
			if (!entered.Ok)
				throw new InvalidOperationException("The name was not entered: " + entered.Text);
			var pid = Pid(serial);
			Line(log, "pid=" + pid);
			var started = Stopwatch.GetTimestamp();
			File.WriteAllText(screen, WithPrefix(original, "AOT "));
			var pushed = AndroidLoop.SendFile(session);
			var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			var now = Pid(serial);
			var ui = Ui(serial);
			var editOk = pushed.Applied
				&& now == pid
				&& pushed.Text.StartsWith("AOT 0", StringComparison.Ordinal)
				&& pushed.Text.Contains("program=1", StringComparison.Ordinal)
				&& pushed.Text.Contains("present=1", StringComparison.Ordinal)
				&& ui.Contains("text=\"AOT 0\"", StringComparison.Ordinal)
				&& ui.Contains("text=\"Ada\"", StringComparison.Ordinal);
			Line(log, "edit ms=" + elapsed.ToString("F4", CultureInfo.InvariantCulture) + " pid=" + now + " reply=" + pushed.Text.Replace('\n', ' ') + " ok=" + editOk);
			if (!editOk)
				throw new InvalidOperationException("The title edit did not present in the program-only build.");
			Shot(serial, "/opt/cursor/artifacts/program-only-present.png");
			var denied = session.Connection.Apply(Encoding.UTF8.GetBytes("not-a-program"));
			var deniedPid = Pid(serial);
			var still = Ui(serial);
			var deniedOk = !denied.Ok
				&& denied.Text.Contains("no assembly loader", StringComparison.Ordinal)
				&& deniedPid == pid
				&& still.Contains("text=\"AOT 0\"", StringComparison.Ordinal)
				&& still.Contains("text=\"Ada\"", StringComparison.Ordinal);
			Line(log, "loader-denied ok=" + deniedOk + " pid=" + deniedPid + " text=" + denied.Text.Replace('\n', ' '));
			if (!deniedOk)
				throw new InvalidOperationException("A non-program payload was not rejected in place.");
			File.WriteAllText(screen, original);
			Directory.CreateDirectory("/opt/cursor/artifacts");
			File.WriteAllText("/opt/cursor/artifacts/measure-program-only.txt", log.ToString());
			return 0;
		}
		finally
		{
			if (!string.Equals(File.ReadAllText(screen), original, StringComparison.Ordinal))
				File.WriteAllText(screen, original);
			session.Dispose();
		}
	}

	static void BuildAndroid(string repo, string rid, bool programOnly, bool trimAnalyzer)
	{
		var project = Path.Combine(repo, "src", "NativePoc.Android", "NativePoc.Android.csproj");
		var arguments = new List<string>
		{
			"build",
			project,
			"-c", "Debug",
			"-f", "net10.0-android",
			"-r", rid,
		};
		if (programOnly)
			arguments.Add("-p:NativePocProgramOnly=true");
		if (trimAnalyzer)
			arguments.Add("-p:EnableTrimAnalyzer=true");
		var build = ProcessRunner.Run("dotnet", arguments, repo, Console.WriteLine, Console.Error.WriteLine);
		if (build.Code != 0)
			throw new InvalidOperationException("Debug build exited " + build.Code + ".");
	}

	static int CountText(string path, string needle)
	{
		var bytes = File.ReadAllBytes(path);
		return CountPattern(bytes, Encoding.ASCII.GetBytes(needle)) + CountPattern(bytes, Encoding.Unicode.GetBytes(needle));
	}

	static int CountPattern(byte[] bytes, byte[] pattern)
	{
		if (pattern.Length == 0 || bytes.Length < pattern.Length)
			return 0;
		var count = 0;
		for (var index = 0; index <= bytes.Length - pattern.Length; index++)
		{
			var match = true;
			for (var offset = 0; offset < pattern.Length; offset++)
			{
				if (bytes[index + offset] != pattern[offset])
				{
					match = false;
					break;
				}
			}

			if (match)
				count++;
		}

		return count;
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
				&& noteTree.Contains("id=\"scroll/list/tent/note\"", StringComparison.Ordinal)
				&& noteTree.Contains("value=\"Note\"", StringComparison.Ordinal)
				&& Items(State(session)) == kept;
			Line(log, "new-field ok=" + noteOk + " pid=" + Pid(serial));
			if (!noteOk)
				throw new InvalidOperationException("The added text did not land in place: " + note.Text);
			Shot(serial, "/opt/cursor/artifacts/packing-program-note.png");
			File.WriteAllText(screen, SwapRow(original));
			var swapped = AndroidLoop.SendPacking(session, repo);
			var swappedTree = Tree(session);
			var editAt = swappedTree.IndexOf("id=\"scroll/list/tent/edit\"", StringComparison.Ordinal);
			var nameAt = swappedTree.IndexOf("id=\"scroll/list/tent/name\"", StringComparison.Ordinal);
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

	static int Checkpoint(string[] args)
	{
		string? serial;
		try
		{
			serial = ParseCheckpoint(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 2;
		}

		try
		{
			return ExecuteCheckpoint(serial);
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

	static string? ParseCheckpoint(IReadOnlyList<string> args)
	{
		if (args.Count < 2 || args[0] != "measure" || args[1] != "checkpoint")
			throw new UsageException("Usage: cnative measure checkpoint [--device serial]");
		string? serial = null;
		for (var index = 2; index < args.Count; index++)
		{
			switch (args[index])
			{
				case "--device":
					if (index + 1 >= args.Count)
						throw new UsageException("--device needs a serial.");
					serial = args[++index];
					break;
				default:
					throw new UsageException("Usage: cnative measure checkpoint [--device serial]");
			}
		}

		return serial;
	}

	static int ExecuteCheckpoint(string? requestedSerial)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var serial = AndroidLoop.ResolveSerial(requestedSerial);
		var rid = Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		var log = new StringBuilder();
		Line(log, "label=emulator-run");
		Line(log, "file=files/nativepoc-state.bin");
		Line(log, "token-file=files/nativepoc-hotreload.token");
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
			if (!warmup.Applied || !warmup.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a presented program: " + warmup.Text);
			var packing = AndroidLoop.SendPacking(session, repo);
			if (!packing.Applied || !packing.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("The packing program was not applied: " + packing.Text);
			var entered = session.Connection.Apply(ScreenQuery.EnterId("name", "Ada"));
			if (!entered.Ok)
				throw new InvalidOperationException("The name was not entered: " + entered.Text);
			Press(session, "save");
			StartPacking(serial);
			WaitUi(serial, "Canvas tent");
			PressPacked(session);
			var beforePid = Pid(serial);
			var before = Kept(State(session));
			if (!before.Contains("count=1\n", StringComparison.Ordinal)
				|| !before.Contains("name=416461\n", StringComparison.Ordinal)
				|| !TentPacked(before))
				throw new InvalidOperationException("The checkpoint seed was not prepared: " + before.Replace('\n', '|'));
			var listed = AndroidLoop.Adb(serial, "shell", "run-as", LaunchCommand.Package, "ls", "files");
			var fileThere = listed.Stdout.Contains("nativepoc-state.bin", StringComparison.Ordinal);
			Line(log, "state-file-before=" + fileThere);
			if (!fileThere)
				throw new InvalidOperationException("The state file was not written: " + listed.Stdout + listed.Stderr);
			RestartPlain(serial);
			var afterPid = AndroidLoop.WaitPid(serial);
			session.Token = AndroidLoop.WaitToken(serial);
			session.Open(repo, serial);
			var after = Kept(State(session));
			var forceOk = afterPid != beforePid && after == before;
			Line(log, "force-stop before-pid=" + beforePid + " after-pid=" + afterPid + " ok=" + forceOk);
			Line(log, "force-stop state=" + after.Replace('\n', '|'));
			if (!forceOk)
				throw new InvalidOperationException("A plain force-stop did not restore the checkpoint.");
			var renamed = session.Connection.Apply(ScreenQuery.EnterId("name", "Bea"));
			if (!renamed.Ok)
				throw new InvalidOperationException("The second name was not entered: " + renamed.Text);
			var hostBefore = Kept(State(session));
			if (!hostBefore.Contains("name=426561\n", StringComparison.Ordinal) || !hostBefore.Contains("count=1\n", StringComparison.Ordinal))
				throw new InvalidOperationException("The host checkpoint was not prepared: " + hostBefore.Replace('\n', '|'));
			var captured = AndroidLoop.CaptureRunning(serial);
			if (captured is null)
				throw new InvalidOperationException("The running process was not captured.");
			var removed = AndroidLoop.Adb(serial, "shell", "run-as", LaunchCommand.Package, "rm", "-f", "files/nativepoc-state.bin");
			if (removed.Code != 0)
				throw new InvalidOperationException("The state file was not removed: " + removed.Stderr + removed.Stdout);
			var stopped = AndroidLoop.Adb(serial, "shell", "am", "force-stop", LaunchCommand.Package);
			if (stopped.Code != 0)
				throw new InvalidOperationException("force-stop failed: " + stopped.Stderr + stopped.Stdout);
			var gone = AndroidLoop.Adb(serial, "shell", "run-as", LaunchCommand.Package, "ls", "files");
			var absent = !gone.Stdout.Contains("nativepoc-state.bin", StringComparison.Ordinal);
			Line(log, "state-file-removed=" + absent);
			if (!absent)
				throw new InvalidOperationException("The state file was still present: " + gone.Stdout);
			var hostPidBefore = afterPid;
			session.Token = AndroidLoop.DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			AndroidLoop.Restore(session, captured);
			var hostPid = Pid(serial);
			var hostAfter = Kept(State(session));
			var hostOk = hostPid != hostPidBefore && hostAfter == hostBefore;
			Line(log, "first-run before-pid=" + hostPidBefore + " after-pid=" + hostPid + " ok=" + hostOk);
			Line(log, "first-run state=" + hostAfter.Replace('\n', '|'));
			if (!hostOk)
				throw new InvalidOperationException("The first-run restore did not write the captured checkpoint.");
			return 0;
		}
		finally
		{
			Directory.CreateDirectory("/opt/cursor/artifacts");
			File.WriteAllText("/opt/cursor/artifacts/measure-checkpoint.txt", log.ToString());
			session.Dispose();
		}
	}

	static void RestartPlain(string serial)
	{
		var resolved = AndroidLoop.Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
		var component = ActivityName.FromResolve(resolved.Stdout)
			?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout + resolved.Stderr);
		var commands = LaunchCommand.BeforeStart(LaunchCommand.Package, component);
		for (var index = 0; index < commands.Count; index++)
		{
			var joined = string.Join(' ', commands[index]);
			if (joined.Contains("nativepoc-state.bin", StringComparison.Ordinal))
				throw new InvalidOperationException("Launch deletes the state file.");
			if (index == 1)
				Thread.Sleep(300);
			var result = AndroidLoop.Adb(serial, commands[index]);
			if (index == commands.Count - 1 && result.Code != 0)
				throw new InvalidOperationException("am start failed: " + result.Stderr + result.Stdout);
		}
	}

	static void Press(ReloadSession session, string id)
	{
		var last = "";
		for (var attempt = 0; attempt < 10; attempt++)
		{
			var pressed = session.Connection.Apply(ScreenQuery.PressId(id));
			if (pressed.Ok && pressed.Text.Contains("pressed " + id, StringComparison.Ordinal))
				return;
			last = pressed.Text;
			Thread.Sleep(250);
		}

		throw new InvalidOperationException("The control was not pressed: " + id + " " + last);
	}

	static string Kept(string state)
	{
		var builder = new StringBuilder();
		foreach (var line in state.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
		{
			if (line.StartsWith("count=", StringComparison.Ordinal)
				|| line.StartsWith("name=", StringComparison.Ordinal)
				|| line.StartsWith("item=", StringComparison.Ordinal))
				builder.Append(line).Append('\n');
		}

		return builder.ToString();
	}

	static bool TentPacked(string state)
	{
		foreach (var line in state.Split('\n'))
		{
			if (line.StartsWith("item=74656e74 ", StringComparison.Ordinal) && line.EndsWith(" true", StringComparison.Ordinal))
				return true;
		}

		return false;
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
			var pressed = session.Connection.Apply(ScreenQuery.PressId("scroll/list/tent/packed"));
			if (pressed.Ok && pressed.Text.Contains("pressed scroll/list/tent/packed", StringComparison.Ordinal))
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
