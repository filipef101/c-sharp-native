using System.Diagnostics;

namespace NativePoc.Devx;

static class TokenFiles
{
	public static readonly string[] RelativePaths =
	[
		"files/nativepoc-hotreload.token",
		"files/hotreload.token",
		"cache/nativepoc-hotreload.token",
	];
}

readonly record struct FoundToken(string Hex, string Source);

static class AndroidLoop
{
	const int WaitMs = 30_000;

	public static int Run(CliOptions options)
	{
		try
		{
			return Execute(options);
		}
		catch (InvalidOperationException ex)
		{
			Console.Error.WriteLine("cnative: " + ex.Message);
			return 1;
		}
	}

	static int Execute(CliOptions options)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var serial = ResolveSerial(options.Serial);
		var rid = options.Abi ?? Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		using var cancel = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cancel.Cancel();
		};
		FoundToken token = default;
		try
		{
			token = DeployAndLaunch(repo, rid, serial);
			var initial = Push(repo, token.Hex, options);
			if (initial.Restarting)
				token = DeployAndLaunch(repo, rid, serial);
			if (options.Once)
				return initial.Status == ReloadStatus.Applied || initial.Restarting ? 0 : 1;
			if (initial.Status == ReloadStatus.Failed)
				return 1;
			return Watch(repo, rid, serial, token, options, cancel.Token);
		}
		finally
		{
			Adb(serial, "forward", "--remove", "tcp:" + HotReloadPort);
		}
	}

	const int HotReloadPort = 18765;

	static int Watch(string repo, string rid, string serial, FoundToken token, CliOptions options, CancellationToken cancel)
	{
		Console.WriteLine("cnative: watching src/NativePoc.Screen");
		var debouncer = new ChangeDebouncer(options.DebounceMs);
		var src = Path.Combine(repo, "src");
		using var watcher = new FileSystemWatcher(src)
		{
			IncludeSubdirectories = true,
			Filter = "*.cs",
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
		};
		FileSystemEventHandler onChange = (_, args) =>
		{
			if (!string.IsNullOrEmpty(args.FullPath))
				debouncer.Push(Path.GetRelativePath(repo, args.FullPath), Environment.TickCount64);
		};
		watcher.Changed += onChange;
		watcher.Created += onChange;
		watcher.Renamed += (_, args) =>
		{
			if (!string.IsNullOrEmpty(args.FullPath))
				debouncer.Push(Path.GetRelativePath(repo, args.FullPath), Environment.TickCount64);
		};
		watcher.Error += (_, args) => Console.Error.WriteLine("cnative: watch error " + args.GetException().Message);
		watcher.EnableRaisingEvents = true;
		while (!cancel.IsCancellationRequested)
		{
			var batch = debouncer.Poll(Environment.TickCount64);
			if (batch is { Count: > 0 })
			{
				var effect = ChangeEffectRules.Classify(batch);
				switch (effect)
				{
					case ChangeEffect.Ignore:
						break;
					case ChangeEffect.ReloadScreen:
						NoteUnswapped(batch);
						if (!batch.Any(ChangeEffectRules.IsHostScreen))
							break;
						Console.WriteLine("cnative: reload src/NativePoc.Screen/Screen.cs");
						var outcome = Push(repo, token.Hex, options);
						if (outcome.Restarting)
							token = DeployAndLaunch(repo, rid, serial);
						break;
					case ChangeEffect.RestartRequired:
						ReportFileRude(batch);
						var interactive = Interactive();
						var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
						var follow = ReloadConsole.FollowUp(restarting, !restarting && !interactive && !options.RestartOnRude);
						if (follow is not null)
							Console.WriteLine(follow);
						if (restarting)
							token = DeployAndLaunch(repo, rid, serial);
						break;
					default:
						throw Unhandled(effect);
				}
			}

			if (cancel.WaitHandle.WaitOne(40))
				break;
		}

		Console.WriteLine("cnative: stopped");
		return 0;
	}

	static Exception Unhandled(ChangeEffect effect) =>
		effect switch
		{
			ChangeEffect.Ignore => new InvalidOperationException("Ignore is handled."),
			ChangeEffect.ReloadScreen => new InvalidOperationException("ReloadScreen is handled."),
			ChangeEffect.RestartRequired => new InvalidOperationException("RestartRequired is handled."),
		};

	static void NoteUnswapped(IReadOnlyList<string> batch)
	{
		foreach (var path in batch)
		{
			var normalized = ChangeEffectRules.Normalize(path);
			if (!normalized.StartsWith("src/NativePoc.Screen/", StringComparison.Ordinal))
				continue;
			if (ChangeEffectRules.IsHostScreen(normalized))
				continue;
			Console.WriteLine(normalized + " is not in the swapped assembly. The host compiles src/NativePoc.Screen/Screen.cs only.");
		}
	}

	static void ReportFileRude(IReadOnlyList<string> batch)
	{
		Console.WriteLine("rude edit -> restart required");
		foreach (var path in batch)
		{
			var normalized = ChangeEffectRules.Normalize(path);
			if (normalized.StartsWith("src/NativePoc.Core/", StringComparison.Ordinal)
				|| normalized.StartsWith("src/NativePoc.Android/", StringComparison.Ordinal))
				Console.WriteLine(normalized);
		}
	}

	readonly record struct PushOutcome(ReloadStatus Status, bool Restarting);

	static PushOutcome Push(string repo, string tokenHex, CliOptions options)
	{
		var started = Stopwatch.GetTimestamp();
		var (code, stdout, stderr) = HotReloadHostClient.Once(repo, tokenHex);
		var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		var parsed = HostOutput.Parse(code, stdout, stderr, repo);
		var status = HostOutput.Classify(parsed);
		var restarting = false;
		if (status == ReloadStatus.RudeEdit)
		{
			Console.WriteLine("rude edit -> restart required");
			var detail = ReloadConsole.RudeDetail(parsed.Raw);
			if (detail is not null)
				Console.WriteLine(detail);
			var interactive = Interactive();
			restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
			var follow = ReloadConsole.FollowUp(restarting, !restarting && !interactive && !options.RestartOnRude);
			if (follow is not null)
				Console.WriteLine(follow);
			return new PushOutcome(status, restarting);
		}

		foreach (var line in ReloadConsole.Format(status, parsed, elapsed))
			Console.WriteLine(line);
		return new PushOutcome(status, false);
	}

	static bool Interactive() => !Console.IsInputRedirected;

	static string? ReadAnswer()
	{
		Console.Write("Restart the app? [y/N] ");
		return Console.ReadLine();
	}

	static FoundToken DeployAndLaunch(string repo, string rid, string serial)
	{
		Console.WriteLine("cnative: building Debug " + rid + " for " + serial);
		var project = Path.Combine(repo, "src", "NativePoc.Android", "NativePoc.Android.csproj");
		var build = ProcessRunner.Run(
			"dotnet",
			[
				"build",
				project,
				"-c", "Debug",
				"-f", "net10.0-android",
				"-r", rid,
				"-t:Run",
				"-p:AdbTarget=-s " + serial,
			],
			repo,
			Console.WriteLine,
			Console.Error.WriteLine);
		if (build.Code != 0)
			throw new InvalidOperationException("Debug deploy exited " + build.Code + ".");
		var resolved = Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
		var component = ActivityName.FromResolve(resolved.Stdout)
			?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout + resolved.Stderr);
		var startArgs = LaunchCommand.Start(component);
		if (LaunchCommand.PassesToken(startArgs))
			throw new InvalidOperationException("Refusing to pass the hot reload token as a launch extra.");
		Console.WriteLine("cnative: launching " + component);
		Adb(serial, "shell", "am", "force-stop", LaunchCommand.Package);
		Thread.Sleep(300);
		var started = Adb(serial, startArgs);
		if (started.Code != 0)
			throw new InvalidOperationException("am start failed: " + started.Stderr + started.Stdout);
		var pid = WaitPid(serial);
		Console.WriteLine("cnative: pid " + pid);
		var token = WaitToken(serial);
		Console.WriteLine("cnative: token read via adb run-as (" + token.Source + ")");
		return token;
	}

	static string ResolveSerial(string? requested)
	{
		var listed = ProcessRunner.Run("adb", ["devices"]);
		if (listed.Code != 0)
			throw new InvalidOperationException("adb devices failed: " + listed.Stderr + listed.Stdout);
		return DeviceList.Require(DeviceList.Online(listed.Stdout), requested);
	}

	static string Prop(string serial, string name)
	{
		var result = Adb(serial, "shell", "getprop", name);
		if (result.Code != 0 || result.Stdout.Trim().Length == 0)
			throw new InvalidOperationException("adb getprop " + name + " failed: " + result.Stderr + result.Stdout);
		return result.Stdout;
	}

	static string WaitPid(string serial)
	{
		var started = Environment.TickCount64;
		while (Environment.TickCount64 - started < WaitMs)
		{
			var result = Adb(serial, "shell", "pidof", LaunchCommand.Package);
			var pid = result.Stdout.Trim();
			if (pid.Length > 0)
				return pid;
			Thread.Sleep(200);
		}

		throw new InvalidOperationException("The app process did not start.");
	}

	static FoundToken WaitToken(string serial)
	{
		Console.WriteLine("cnative: waiting for a hot reload token via adb run-as");
		var notes = new System.Text.StringBuilder();
		var started = Environment.TickCount64;
		while (Environment.TickCount64 - started < WaitMs)
		{
			notes.Clear();
			foreach (var relative in TokenFiles.RelativePaths)
			{
				var result = Adb(serial, "shell", "run-as", LaunchCommand.Package, "cat", relative);
				if (TokenText.TryParse(result.Stdout, out var hex))
					return new FoundToken(hex, relative);
				notes.Append(relative).Append(" exit=").Append(result.Code).Append(' ').Append(One(result.Stderr)).Append('\n');
			}

			foreach (var dir in new[] { "files", "cache" })
			{
				var listing = Adb(serial, "shell", "run-as", LaunchCommand.Package, "ls", dir);
				var listed = listing.Stdout.Trim();
				if (listed.Length == 0)
					listed = listing.Stderr.Trim();
				if (listed.Length == 0)
					listed = "(empty)";
				notes.Append(dir).Append(" ls=").Append(One(listed)).Append('\n');
				foreach (var name in TokenText.TokenFileNames(listing.Stdout))
				{
					var relative = dir + "/" + name;
					if (TokenFiles.RelativePaths.Contains(relative))
						continue;
					var result = Adb(serial, "shell", "run-as", LaunchCommand.Package, "cat", relative);
					if (TokenText.TryParse(result.Stdout, out var hex))
						return new FoundToken(hex, relative);
				}
			}

			var unix = Adb(serial, "shell", "run-as", LaunchCommand.Package, "cat", "/proc/net/unix");
			var fromSocket = TokenText.FromUnixTable(unix.Stdout);
			if (fromSocket is not null)
				return new FoundToken(fromSocket, "/proc/net/unix");
			if (unix.Code != 0 || unix.Stderr.Trim().Length > 0)
				notes.Append("unix exit=").Append(unix.Code).Append(' ').Append(One(unix.Stderr)).Append('\n');
			else
				notes.Append("unix=no npoc- socket\n");
			Thread.Sleep(300);
		}

		throw new InvalidOperationException(
			"The app did not publish a hot reload token for adb run-as. Launch extras are not used.\n" + notes);
	}

	static string One(string text)
	{
		var trimmed = text.Trim().Replace('\n', ' ');
		return trimmed.Length <= 180 ? trimmed : trimmed[..180];
	}

	static (int Code, string Stdout, string Stderr) Adb(string serial, params string[] args)
	{
		var all = new List<string> { "-s", serial };
		all.AddRange(args);
		return ProcessRunner.Run("adb", all);
	}
}
