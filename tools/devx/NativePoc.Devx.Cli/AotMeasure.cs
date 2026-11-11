using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using NativePoc;

namespace NativePoc.Devx;

static partial class MeasureLoop
{
	static int Aot(string[] args)
	{
		string? requested = null;
		var editsOnly = false;
		if (args.Length < 2 || args[0] != "measure" || args[1] != "aot")
		{
			Console.Error.WriteLine("Usage: cnative measure aot [--device serial] [--edits]");
			return 2;
		}

		for (var index = 2; index < args.Length; index++)
		{
			if (args[index] == "--edits")
			{
				editsOnly = true;
				continue;
			}

			if (args[index] != "--device" || index + 1 >= args.Length)
			{
				Console.Error.WriteLine("Usage: cnative measure aot [--device serial] [--edits]");
				return 2;
			}

			requested = args[++index];
		}

		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var screenPath = Path.Combine(repo, ScreenCompiler.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		var packingPath = Path.Combine(repo, "samples", "NativePoc.Packing", "PackingScreen.cs");
		var originalScreen = File.ReadAllText(screenPath);
		var originalPacking = File.ReadAllText(packingPath);
		var log = new StringBuilder();
		var session = new ReloadSession();
		try
		{
			var serial = AndroidLoop.ResolveSerial(requested);
			Line(log, "label=emulator-run");
			Line(log, "machine=x86_64 KVM emulator, not Mac arm64");
			Line(log, "date=2026-11-11");
			Line(log, "abi=" + Prop(serial, "ro.product.cpu.abi"));
			Line(log, "sdk=" + Prop(serial, "ro.build.version.sdk"));
			Line(log, "qemu=" + Prop(serial, "ro.kernel.qemu"));
			Line(log, "host-qemu=" + HostQemu());
			if (!editsOnly)
			{
				var releaseApk = PublishedApk(repo, "Release");
				var devApk = PublishedApk(repo, "Debug");
				DescribeBinary(log, "release", releaseApk);
				DescribeBinary(log, "dev-listener", devApk);
				var releaseStarts = ColdStarts(log, serial, releaseApk, "release");
				var devStarts = ColdStarts(log, serial, devApk, "dev-listener");
				Line(log, "startup-delta-p50-ms=" + (Percentile(devStarts, 1, 2) - Percentile(releaseStarts, 1, 2)).ToString("F4", CultureInfo.InvariantCulture));
			}
			var dynamic = AndroidLoop.Adb(serial, "logcat", "-d", "-s", "NativePoc:I");
			var dynamicOk = dynamic.Stdout.Contains("IsDynamicCodeSupported=False", StringComparison.Ordinal);
			Line(log, "IsDynamicCodeSupported=False logged=" + dynamicOk);
			if (!dynamicOk)
				throw new InvalidOperationException("logcat did not show IsDynamicCodeSupported=False.");
			session.Token = AndroidLoop.WaitToken(serial);
			session.Open(repo, serial);
			var resolved = AndroidLoop.Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
			var component = ActivityName.FromResolve(resolved.Stdout)
				?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout);
			var front = AndroidLoop.Adb(serial, "shell", "am", "start", "-n", component, "--activity-reorder-to-front");
			if (front.Code != 0)
				throw new InvalidOperationException("The counter did not return: " + front.Stderr + front.Stdout);
			var warmup = AndroidLoop.SendFile(session);
			if (!warmup.Applied || !warmup.Text.Contains("program=1", StringComparison.Ordinal) || !warmup.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a presented program: " + warmup.Text);
			var entered = session.Connection.Apply(ScreenQuery.EnterId("name", "Ada"));
			if (!entered.Ok || !Ui(serial).Contains("text=\"Ada\"", StringComparison.Ordinal))
				throw new InvalidOperationException("The name was not entered: " + entered.Text);
			var pid = Pid(serial);
			Line(log, "edit-pid=" + pid);
			PresentSeries(log, serial, session, pid, 10, "title", index =>
			{
				var prefix = "P" + index.ToString("00", CultureInfo.InvariantCulture) + " ";
				File.WriteAllText(screenPath, WithPrefix(originalScreen, prefix));
				var pushed = AndroidLoop.SendFile(session);
				return (pushed.Applied, pushed.Text, "text=\"" + prefix, true);
			}, () => Ui(serial).Contains("text=\"Ada\"", StringComparison.Ordinal));
			File.WriteAllText(screenPath, originalScreen);
			var started = AndroidLoop.Adb(serial, "shell", "am", "start", "-n", LaunchCommand.Package + "/nativepoc.PackingActivity", "--activity-reorder-to-front");
			if (started.Code != 0)
				throw new InvalidOperationException("Packing did not start: " + started.Stderr + started.Stdout);
			WaitUi(serial, "Canvas tent");
			var packing = AndroidLoop.SendPacking(session, repo);
			if (!packing.Applied || !packing.Text.Contains("program=1", StringComparison.Ordinal) || !packing.Text.Contains("present=1", StringComparison.Ordinal))
				throw new InvalidOperationException("The packing program was not applied: " + packing.Text);
			PressPacked(session);
			var kept = Items(State(session));
			Line(log, "items-after-press=" + kept.Replace('\n', '|'));
			if (!TentPacked(kept))
				throw new InvalidOperationException("The packed row was not kept: " + kept);
			if (Pid(serial) != pid)
				throw new InvalidOperationException("Packing left the process.");
			PresentSeries(log, serial, session, pid, 10, "packing", index =>
			{
				var title = "P" + index.ToString("00", CultureInfo.InvariantCulture);
				File.WriteAllText(packingPath, WithRowTitle(originalPacking, title));
				var pushed = AndroidLoop.SendPacking(session, repo);
				return (pushed.Applied, pushed.Text, "text=\"" + title + "\"", Items(State(session)) == kept);
			}, () => true);
			FocusProof(log, serial, session, pid, packingPath, originalPacking);
			var behaved = originalPacking.Replace("state.DraftPacked = false;", "state.DraftPacked = true;", StringComparison.Ordinal);
			File.WriteAllText(packingPath, behaved);
			var behavior = AndroidLoop.SendPacking(session, repo);
			var added = session.Connection.Apply(ScreenQuery.PressId("list/add"));
			var after = State(session);
			var behaviorOk = behavior.Applied
				&& added.Ok
				&& Pid(serial) == pid
				&& after.Contains("draftPacked=true", StringComparison.Ordinal);
			Line(log, "behavior-edit ok=" + behaviorOk + " pid=" + Pid(serial));
			if (!behaviorOk)
				throw new InvalidOperationException("The packing behavior edit did not apply in place: " + behavior.Text + " " + after);
			File.WriteAllText(packingPath, originalPacking);
			var restored = AndroidLoop.SendPacking(session, repo);
			if (!restored.Applied || Pid(serial) != pid)
				throw new InvalidOperationException("The packing screen was not restored: " + restored.Text);
			Directory.CreateDirectory("/opt/cursor/artifacts");
			File.WriteAllText("/opt/cursor/artifacts/measure-aot.txt", log.ToString());
			Shot(serial, "/opt/cursor/artifacts/aot-dev-listener.png");
			return 0;
		}
		finally
		{
			if (!string.Equals(File.ReadAllText(screenPath), originalScreen, StringComparison.Ordinal))
				File.WriteAllText(screenPath, originalScreen);
			if (!string.Equals(File.ReadAllText(packingPath), originalPacking, StringComparison.Ordinal))
				File.WriteAllText(packingPath, originalPacking);
			session.Dispose();
		}
	}

	static void FocusProof(StringBuilder log, string serial, ReloadSession session, string pid, string packingPath, string original)
	{
		var opened = session.Connection.Apply(ScreenQuery.PressId("list/tent/edit"));
		if (!opened.Ok)
			throw new InvalidOperationException("The editor did not open: " + opened.Text);
		WaitUi(serial, "Item name");
		TapLabel(serial, "Item name");
		Thread.Sleep(300);
		var noted = LastLog(serial, "field-note key=name ");
		File.WriteAllText(packingPath, original.Replace("new Button(\"Save\"", "new Button(\"Keep\"", StringComparison.Ordinal));
		var pushed = AndroidLoop.SendPacking(session, repo: RepoRoot.Find(Directory.GetCurrentDirectory()));
		WaitUi(serial, "Keep");
		var focused = Ui(serial);
		var restored = LastLog(serial, "field-focus key=name ");
		var nameOk = pushed.Applied && Pid(serial) == pid && FieldFocused(focused, "Item name") && SameCaret(noted, restored);
		Line(log, "focus-name ok=" + nameOk + " note=" + noted + " focus=" + restored + " pid=" + Pid(serial));
		if (!nameOk)
			throw new InvalidOperationException("The name field did not keep focus.");
		var note = WithDraftNote(original);
		File.WriteAllText(packingPath, note);
		var notedField = AndroidLoop.SendPacking(session, RepoRoot.Find(Directory.GetCurrentDirectory()));
		if (!notedField.Applied || !Tree(session).Contains("id=\"editor/note\"", StringComparison.Ordinal))
			throw new InvalidOperationException("The note field did not land: " + notedField.Text);
		WaitUi(serial, "Note");
		TapLabel(serial, "Note");
		Thread.Sleep(300);
		var noteBefore = LastLog(serial, "field-note key=note ");
		File.WriteAllText(packingPath, note.Replace("new Button(\"Back\"", "new Button(\"Leave\"", StringComparison.Ordinal));
		var again = AndroidLoop.SendPacking(session, RepoRoot.Find(Directory.GetCurrentDirectory()));
		WaitUi(serial, "Leave");
		var noteFocus = LastLog(serial, "field-focus key=note ");
		var noteOk = again.Applied && Pid(serial) == pid && FieldFocused(Ui(serial), "Note") && SameCaret(noteBefore, noteFocus);
		Line(log, "focus-note ok=" + noteOk + " note=" + noteBefore + " focus=" + noteFocus + " pid=" + Pid(serial));
		if (!noteOk)
			throw new InvalidOperationException("The note field did not keep focus.");
		File.WriteAllText(packingPath, original);
		var back = AndroidLoop.SendPacking(session, RepoRoot.Find(Directory.GetCurrentDirectory()));
		if (!back.Applied)
			throw new InvalidOperationException("The editor was not restored: " + back.Text);
		var list = session.Connection.Apply(ScreenQuery.PressId("editor/back"));
		if (!list.Ok)
			throw new InvalidOperationException("The list did not return: " + list.Text);
	}

	static void PresentSeries(
		StringBuilder log,
		string serial,
		ReloadSession session,
		string pid,
		int samples,
		string name,
		Func<int, (bool Applied, string Text, string Needle, bool Kept)> edit,
		Func<bool> extra)
	{
		var onDraw = new double[samples];
		var frames = new double[samples];
		for (var index = 0; index < samples; index++)
		{
			ResetFrames(serial);
			var frameBefore = LatestFrame(serial);
			var started = Stopwatch.GetTimestamp();
			var pushed = edit(index);
			var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			onDraw[index] = elapsed;
			var frameMs = -1.0;
			var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
			while (frameMs < 0 && Stopwatch.GetTimestamp() < deadline)
			{
				if (LatestFrame(serial) > frameBefore)
					frameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			}

			frames[index] = frameMs;
			var visible = Ui(serial);
			var ok = pushed.Applied
				&& Pid(serial) == pid
				&& pushed.Text.Contains("program=1", StringComparison.Ordinal)
				&& pushed.Text.Contains("present=1", StringComparison.Ordinal)
				&& visible.Contains(pushed.Needle, StringComparison.Ordinal)
				&& pushed.Kept
				&& extra()
				&& frameMs >= 0;
			Line(log, name + " sample=" + index.ToString(CultureInfo.InvariantCulture)
				+ " ondraw-ms=" + elapsed.ToString("F4", CultureInfo.InvariantCulture)
				+ " frame-ms=" + frameMs.ToString("F4", CultureInfo.InvariantCulture)
				+ " pid=" + Pid(serial)
				+ " reply=" + pushed.Text.Replace('\n', ' ')
				+ " ok=" + ok);
			if (!ok)
				throw new InvalidOperationException(name + " sample " + index.ToString(CultureInfo.InvariantCulture) + " did not present in place.");
		}

		Line(log, name + "-n=" + samples.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-ondraw-p50-ms=" + Percentile(onDraw, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-ondraw-p90-ms=" + Percentile(onDraw, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-frame-p50-ms=" + Percentile(frames, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-frame-p90-ms=" + Percentile(frames, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-percentile=Hyndman-Fan type 7");
		Line(log, name + "-frame-timestamp=gfxinfo FrameCompleted");
	}

	static List<double> ColdStarts(StringBuilder log, string serial, string apk, string name)
	{
		InstallApk(serial, apk);
		var resolved = AndroidLoop.Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
		var component = ActivityName.FromResolve(resolved.Stdout)
			?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout);
		AndroidLoop.Adb(serial, "logcat", "-c");
		var samples = new List<double>();
		for (var index = 0; index < 20; index++)
		{
			var started = AndroidLoop.Adb(serial, "shell", "am", "start", "-W", "-S", "-n", component);
			if (started.Code != 0)
				throw new InvalidOperationException(name + " start failed: " + started.Stderr + started.Stdout);
			var text = started.Stdout.Replace("\r", "", StringComparison.Ordinal);
			var total = OneField(text, "TotalTime:");
			var state = OneField(text, "LaunchState:");
			if (state != "COLD")
				throw new InvalidOperationException(name + " sample " + index.ToString(CultureInfo.InvariantCulture) + " is " + state + ".");
			samples.Add(double.Parse(total, CultureInfo.InvariantCulture));
			Line(log, name + " startup sample=" + index.ToString(CultureInfo.InvariantCulture) + " TotalTime=" + total + " LaunchState=COLD");
		}

		Line(log, name + "-startup-n=20");
		Line(log, name + "-startup-ms=" + string.Join(",", samples.Select(value => value.ToString("F0", CultureInfo.InvariantCulture))));
		Line(log, name + "-startup-p50-ms=" + Percentile(samples, 1, 2).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-startup-p90-ms=" + Percentile(samples, 9, 10).ToString("F4", CultureInfo.InvariantCulture));
		Line(log, name + "-startup-percentile=Hyndman-Fan type 7");
		return samples;
	}

	static string OneField(string text, string label)
	{
		var count = 0;
		var value = "";
		foreach (var raw in text.Split('\n'))
		{
			var line = raw.Trim();
			if (!line.StartsWith(label, StringComparison.Ordinal))
				continue;
			count++;
			value = line[label.Length..].Trim();
		}

		if (count != 1 || value.Length == 0)
			throw new InvalidOperationException(label + " was not in the start reply.");
		return value;
	}

	static void InstallApk(string serial, string apk)
	{
		var installed = AndroidLoop.Adb(serial, "install", "-r", "-d", apk);
		if (installed.Code == 0 && installed.Stdout.Contains("Success", StringComparison.Ordinal))
			return;
		AndroidLoop.Adb(serial, "uninstall", LaunchCommand.Package);
		var again = AndroidLoop.Adb(serial, "install", "-r", apk);
		if (again.Code != 0 || !again.Stdout.Contains("Success", StringComparison.Ordinal))
			throw new InvalidOperationException("Install failed: " + again.Stderr + again.Stdout + installed.Stderr + installed.Stdout);
	}

	static void DescribeBinary(StringBuilder log, string name, string apk)
	{
		var info = new FileInfo(apk);
		using var zip = ZipFile.OpenRead(apk);
		var entry = zip.GetEntry("lib/x86_64/libNativePoc.Android.so")
			?? throw new InvalidOperationException(apk + " has no lib/x86_64/libNativePoc.Android.so.");
		var extracted = Path.Combine(Path.GetTempPath(), name + "-libNativePoc.Android.so");
		entry.ExtractToFile(extracted, overwrite: true);
		Line(log, name + "-apk-bytes=" + info.Length.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-so-bytes=" + entry.Length.ToString(CultureInfo.InvariantCulture));
		var listen = CountText(extracted, "hotreload-listen");
		var loader = CountText(extracted, "no assembly loader");
		var context = CountText(extracted, "AssemblyLoadContext");
		Line(log, name + "-so-hotreload-listen=" + listen.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-so-no-assembly-loader=" + loader.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-so-assembly-load-context=" + context.ToString(CultureInfo.InvariantCulture));
		var configuration = name == "release" ? "Release" : "Debug";
		var projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(apk)!, "..", "..", "..", "..", ".."));
		var managed = Path.Combine(projectDir, "obj", configuration, "net10.0-android", "android-x64", "linked", "NativePoc.Android.dll");
		var managedListen = CountText(managed, "hotreload-listen");
		var managedLoader = CountText(managed, "no assembly loader");
		var managedContext = CountText(managed, "AssemblyLoadContext");
		Line(log, name + "-managed-hotreload-listen=" + managedListen.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-managed-no-assembly-loader=" + managedLoader.ToString(CultureInfo.InvariantCulture));
		Line(log, name + "-managed-assembly-load-context=" + managedContext.ToString(CultureInfo.InvariantCulture));
		if (name == "release" && managedListen != 0)
			throw new InvalidOperationException("The Release assembly contains the listener.");
		if (name == "dev-listener" && (managedListen == 0 || managedLoader == 0 || managedContext != 0))
			throw new InvalidOperationException("The dev listener assembly does not match the program-only listener.");
	}

	static bool FieldFocused(string xml, string label)
	{
		var at = xml.IndexOf("content-desc=\"" + label + "\"", StringComparison.Ordinal);
		if (at < 0)
			return false;
		var nodeStart = xml.LastIndexOf("<node", at, StringComparison.Ordinal);
		var nodeEnd = xml.IndexOf("/>", at, StringComparison.Ordinal);
		if (nodeStart < 0 || nodeEnd < 0)
			return false;
		return xml[nodeStart..nodeEnd].Contains("focused=\"true\"", StringComparison.Ordinal);
	}

	static bool SameCaret(string noted, string restored)
	{
		var noteAt = noted.IndexOf("key=", StringComparison.Ordinal);
		var focusAt = restored.IndexOf("key=", StringComparison.Ordinal);
		return noteAt >= 0 && focusAt >= 0 && noted[noteAt..] == restored[focusAt..];
	}

	static string PublishedApk(string repo, string configuration)
	{
		var dir = Path.Combine(repo, "src", "NativePoc.Android", "bin", configuration, "net10.0-android", "android-x64", "publish");
		if (!Directory.Exists(dir))
			throw new InvalidOperationException("Missing publish directory " + dir);
		var apks = Directory.GetFiles(dir, "*-Signed.apk");
		if (apks.Length != 1)
			throw new InvalidOperationException("Expected one signed apk in " + dir + ".");
		return apks[0];
	}

	static void TapLabel(string serial, string label)
	{
		var xml = Ui(serial);
		var marker = "content-desc=\"" + label + "\"";
		var at = xml.IndexOf(marker, StringComparison.Ordinal);
		if (at < 0)
			throw new InvalidOperationException("No control labelled " + label + ".");
		var nodeEnd = xml.IndexOf("/>", at, StringComparison.Ordinal);
		var bounds = xml.IndexOf("bounds=\"[", at, StringComparison.Ordinal);
		if (bounds < 0 || nodeEnd < 0 || bounds > nodeEnd)
			throw new InvalidOperationException("No bounds for " + label + ".");
		var end = xml.IndexOf(']', xml.IndexOf(']', bounds) + 1);
		var text = xml[(bounds + "bounds=\"[".Length)..end];
		var pair = text.Split("][");
		var first = pair[0].Split(',');
		var second = pair[1].Split(',');
		var x = (int.Parse(first[0], CultureInfo.InvariantCulture) + int.Parse(second[0], CultureInfo.InvariantCulture)) / 2;
		var y = (int.Parse(first[1], CultureInfo.InvariantCulture) + int.Parse(second[1], CultureInfo.InvariantCulture)) / 2;
		var tapped = AndroidLoop.Adb(serial, "shell", "input", "tap", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture));
		if (tapped.Code != 0)
			throw new InvalidOperationException("tap failed: " + tapped.Stderr + tapped.Stdout);
	}

	static string LastLog(string serial, string prefix)
	{
		var dumped = AndroidLoop.Adb(serial, "logcat", "-d", "-s", "NativePoc:I");
		var found = "";
		foreach (var raw in dumped.Stdout.Split('\n'))
		{
			var at = raw.IndexOf(prefix, StringComparison.Ordinal);
			if (at >= 0)
				found = raw[at..].Trim();
		}

		return found;
	}

	static string WithDraftNote(string source)
	{
		const string needle = "{ Key = \"name\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,";
		const string inserted = "{ Key = \"name\" },\n\t\t\tnew TextField(\n\t\t\t\tstate.DraftNote,\n\t\t\t\tvalue =>\n\t\t\t\t{\n\t\t\t\t\tstate.DraftNote = value;\n\t\t\t\t\thost.RequestRender();\n\t\t\t\t},\n\t\t\t\t\"Note\",\n\t\t\t\t\"Note\")\n\t\t\t{ Key = \"note\" },\n\t\t\tnew Switch(\n\t\t\t\tstate.DraftPacked,";
		if (!source.Contains(needle, StringComparison.Ordinal))
			throw new InvalidOperationException("The editor name field was not found.");
		return source.Replace(needle, inserted, StringComparison.Ordinal);
	}

	static string HostQemu()
	{
		var listed = ProcessRunner.Run("ps", ["-eo", "args="]);
		foreach (var line in listed.Stdout.Split('\n'))
		{
			if (line.Contains("qemu-system-x86_64", StringComparison.Ordinal))
				return line.Trim();
		}

		return "";
	}
}
