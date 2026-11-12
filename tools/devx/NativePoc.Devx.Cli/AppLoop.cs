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
		Install(serial, apk);
		var session = new ReloadSession();
		using var cancel = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cancel.Cancel();
		};
		try
		{
			session.Token = AndroidLoop.LaunchInstalled(serial, "--es nativepoc_root home");
			session.Open(root, serial);
			/* --once is an explicit push of the screens. A watching run
			   leaves the published UI on screen; the first edit sends the
			   file. Re-sending every screen before the user edits reloads
			   the program the publish already contains. */
			if (options.Once)
			{
				foreach (var relative in Screens(root))
				{
					var started = Stopwatch.GetTimestamp();
					var sent = AndroidLoop.SendSource(session, root, relative);
					Report(relative, sent, started);
				}

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
		Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"reloaded in {elapsed:F1} ms (apply {sent.Milliseconds} ms)"));
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
