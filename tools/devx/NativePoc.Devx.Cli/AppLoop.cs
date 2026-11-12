using System.Diagnostics;

namespace NativePoc.Devx;

static class AppRoot
{
	public static bool TryFind(string start, out string root)
	{
		root = "";
		var dir = new DirectoryInfo(Path.GetFullPath(start));
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return false;
			if (File.Exists(Path.Combine(dir.FullName, "cnative-app.csproj")))
			{
				root = dir.FullName;
				return true;
			}

			dir = dir.Parent;
		}

		return false;
	}
}

static class AppLoop
{
	public static int Run(string root, CliOptions options)
	{
		var serial = AndroidLoop.ResolveSerial(options.Serial);
		var abi = Prop(serial);
		var rid = options.Abi ?? Abi.RidFromProperty(abi);
		Console.WriteLine("cnative: app " + root + " " + serial + " rid=" + rid);
		var project = Path.Combine(root, "cnative-app.csproj");
		var publishDir = AndroidBuild.AppPublishDirectory(root, "Debug", rid);
		var apk = AndroidBuild.ExistingSignedApk(publishDir);
		if (ListenerApkCurrent(project, apk))
		{
			Console.WriteLine("cnative: listener apk is newer than cnative-app.csproj. Skipping publish.");
		}
		else
		{
			Console.WriteLine("cnative: publishing NativeAOT listener " + rid + " for " + serial);
			var published = ProcessRunner.Run(
				"dotnet",
				AndroidBuild.AppListenerPublish(project, rid),
				root,
				Console.WriteLine,
				Console.Error.WriteLine);
			if (published.Code != 0)
				throw new InvalidOperationException("Listener publish exited " + published.Code + ".");
			apk = AndroidBuild.RequireSignedApk(publishDir);
		}
		if (apk is null)
			throw new InvalidOperationException("Missing signed apk.");
		var session = new ReloadSession();
		using var cancel = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cancel.Cancel();
		};
		try
		{
			var attached = AndroidLoop.InstallOrAttach(serial, apk, "--es nativepoc_root home", out var token);
			session.Token = token;
			session.Open(root, serial);
			/* --once after a fresh launch pushes the screens. An attach
			   pushes only a screen newer than the last run. A watching
			   run leaves the published UI up until the first edit. */
			if (options.Once)
			{
				var screens = Screens(root);
				var send = attached ? NewerScreens(root, screens, StampPath(root)) : screens;
				if (send.Count == 0)
					Console.WriteLine("cnative: screens unchanged. Skipping reload.");
				foreach (var relative in send)
				{
					var started = Stopwatch.GetTimestamp();
					var sent = AndroidLoop.SendSource(session, root, relative);
					Report(relative, sent, started);
				}

				TouchStamp(StampPath(root));
				return 0;
			}

			return Watch(root, session, options.DebounceMs, cancel.Token);
		}
		finally
		{
			session.Dispose();
		}
	}

	static int Watch(string root, ReloadSession session, int debounceMs, CancellationToken cancel)
	{
		var screens = Path.Combine(root, "screens");
		var warmStarted = Stopwatch.GetTimestamp();
		foreach (var relative in Screens(root))
		{
			var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(path))
				continue;
			var source = File.ReadAllText(path);
			SubsetLowerer.TryLower(source, originFile: relative);
			SubsetLowerer.TryLower(source, originFile: relative);
		}

		var warmMs = Stopwatch.GetElapsedTime(warmStarted).TotalMilliseconds;
		Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"cnative: warm lower {warmMs:F1} ms"));
		Console.WriteLine("cnative: watching " + screens);
		var debouncer = new ChangeDebouncer(debounceMs);
		FileSystemEventHandler onChange = (_, args) =>
		{
			if (string.IsNullOrEmpty(args.FullPath))
				return;
			debouncer.Push(ChangeEffectRules.Normalize(Path.GetRelativePath(root, args.FullPath)), Environment.TickCount64);
		};
		using var watcher = new FileSystemWatcher(screens)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
			EnableRaisingEvents = true,
		};
		watcher.Changed += onChange;
		watcher.Created += onChange;
		watcher.Renamed += (_, args) =>
		{
			if (string.IsNullOrEmpty(args.FullPath))
				return;
			debouncer.Push(ChangeEffectRules.Normalize(Path.GetRelativePath(root, args.FullPath)), Environment.TickCount64);
		};
		while (!cancel.IsCancellationRequested)
		{
			var batch = debouncer.Drain(Environment.TickCount64);
			if (batch is null)
			{
				Thread.Sleep(20);
				continue;
			}

			var waited = Environment.TickCount64 - batch.FirstMs;
			Console.WriteLine("cnative: watcher-wait " + waited.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
			foreach (var relative in batch.Paths)
			{
				if (!relative.EndsWith("Screen.cs", StringComparison.Ordinal))
					continue;
				var started = Stopwatch.GetTimestamp();
				var sent = AndroidLoop.SendSource(session, root, relative);
				Report(relative, sent, started);
			}
		}

		return 0;
	}

	static void Report(string relative, AndroidLoop.PackingSend sent, long started)
	{
		if (!sent.Applied)
		{
			Console.WriteLine(relative + ": " + sent.Text);
			return;
		}

		var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"reloaded in {elapsed:F1} ms (lower {sent.LowerMilliseconds:F1} ms, apply {sent.Milliseconds:F1} ms)"));
		Console.WriteLine(sent.Text);
	}

	/* Screen edits reload through the listener, so they do not make the apk
	   stale. A newer project file or nuget.config does. */
	internal static bool ListenerApkCurrent(string projectPath, string? apkPath)
	{
		if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath) || !File.Exists(projectPath))
			return false;
		var apkTime = File.GetLastWriteTimeUtc(apkPath);
		if (File.GetLastWriteTimeUtc(projectPath) > apkTime)
			return false;
		var nuget = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "nuget.config");
		if (File.Exists(nuget) && File.GetLastWriteTimeUtc(nuget) > apkTime)
			return false;
		return true;
	}

	internal static string StampPath(string root) => Path.Combine(root, ".cnative", "run-stamp");

	internal static List<string> NewerScreens(string root, IReadOnlyList<string> screens, string stampPath)
	{
		DateTime? stamp = File.Exists(stampPath) ? File.GetLastWriteTimeUtc(stampPath) : null;
		var newer = new List<string>();
		foreach (var relative in screens)
		{
			var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(path))
				continue;
			if (stamp is null || File.GetLastWriteTimeUtc(path) > stamp.Value)
				newer.Add(relative);
		}

		return newer;
	}

	static void TouchStamp(string stampPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
		File.WriteAllText(stampPath, "run\n");
		File.SetLastWriteTimeUtc(stampPath, DateTime.UtcNow);
	}

	static List<string> Screens(string root)
	{
		var dir = Path.Combine(root, "screens");
		var found = new List<string>();
		if (!Directory.Exists(dir))
			return found;
		foreach (var file in Directory.EnumerateFiles(dir, "*Screen.cs"))
			found.Add("screens/" + Path.GetFileName(file));
		found.Sort(StringComparer.Ordinal);
		return found;
	}

	static void Install(string serial, string apk)
	{
		var installed = AndroidLoop.Adb(serial, "install", "-r", "-d", apk);
		if (installed.Code == 0 && installed.Stdout.Contains("Success", StringComparison.Ordinal))
			return;
		AndroidLoop.Adb(serial, "uninstall", LaunchCommand.Package);
		var again = AndroidLoop.Adb(serial, "install", "-r", apk);
		if (again.Code != 0 || !again.Stdout.Contains("Success", StringComparison.Ordinal))
			throw new InvalidOperationException("Install failed: " + again.Stderr + again.Stdout + installed.Stderr + installed.Stdout);
	}

	static string Prop(string serial)
	{
		var result = AndroidLoop.Adb(serial, "shell", "getprop", "ro.product.cpu.abi");
		if (result.Code != 0)
			throw new InvalidOperationException("getprop failed: " + result.Stderr);
		return result.Stdout.Trim();
	}
}
