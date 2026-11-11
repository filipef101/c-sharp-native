using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class MeasureLoop
{
	public static int Run(string[] args)
	{
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
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.Error.WriteLine("cnative: " + ex.Message);
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
			Line(log, "samples-ms=" + string.Join(",", timings.Select(value => value.ToString("F4", CultureInfo.InvariantCulture))));
			Line(log, "n=" + samples.ToString(CultureInfo.InvariantCulture));
			Line(log, "p50-ms=" + Percentile(timings, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "p90-ms=" + Percentile(timings, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
			Line(log, "percentile=Hyndman-Fan type 7");
			Directory.CreateDirectory("/opt/cursor/artifacts");
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
		};
		start.ArgumentList.Add("-s");
		start.ArgumentList.Add(serial);
		start.ArgumentList.Add("exec-out");
		start.ArgumentList.Add("screencap");
		start.ArgumentList.Add("-p");
		using var process = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
		using var file = File.Create(path);
		process.StandardOutput.BaseStream.CopyTo(file);
		process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0 || file.Length < 8)
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

	static void Line(StringBuilder log, string text)
	{
		log.AppendLine(text);
		Console.WriteLine(text);
	}
}
