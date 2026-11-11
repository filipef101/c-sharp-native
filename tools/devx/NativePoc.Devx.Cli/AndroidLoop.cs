using System.Diagnostics;
using System.Globalization;
using NativePoc;

namespace NativePoc.Devx;

sealed class ReloadSession : IDisposable
{
	FoundToken _token;
	ScreenCompiler? _compiler;
	HotReloadConnection? _connection;

	public FoundToken Token
	{
		get => _token;
		set => _token = value;
	}

	public ScreenCompiler Compiler => _compiler ?? throw new InvalidOperationException("The screen compiler is not open.");

	public HotReloadConnection Connection => _connection ?? throw new InvalidOperationException("The hot reload connection is not open.");

	public void Open(string repo, string serial)
	{
		_connection?.Dispose();
		_compiler = ScreenCompiler.Create(repo);
		_connection = new HotReloadConnection(serial, _token.Hex);
	}

	public void Dispose()
	{
		_connection?.Dispose();
		_connection = null;
	}
}

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
		var session = new ReloadSession();
		try
		{
			session.Token = DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			var initial = Push(session, options, editStartedMs: null);
			if (initial.Restarting)
				Relaunch(session, repo, rid, serial);
			else
			{
				var packingStarted = Stopwatch.GetTimestamp();
				var packing = SendPacking(session, repo);
				if (!ReportPacking(packing, editStartedMs: null, packingStarted) && options.Once)
					return 1;
			}

			var decision = InitialPush.Decide(initial.Status, initial.Restarting, options.Once);
			switch (decision)
			{
				case InitialDecision.ExitSuccess:
					return 0;
				case InitialDecision.ExitFailure:
					return 1;
				case InitialDecision.WatchAfterFailure:
					Console.Error.WriteLine(InitialPush.FailureLine);
					return Watch(repo, rid, serial, session, options, cancel.Token);
				case InitialDecision.Watch:
					return Watch(repo, rid, serial, session, options, cancel.Token);
				default:
					throw UnhandledDecision(decision);
			}
		}
		finally
		{
			session.Dispose();
		}
	}

	static int Watch(string repo, string rid, string serial, ReloadSession session, CliOptions options, CancellationToken cancel)
	{
		Console.WriteLine("cnative: watching src/NativePoc.Screen and samples/NativePoc.Packing");
		var debouncer = new ChangeDebouncer(options.DebounceMs);
		FileSystemEventHandler onChange = (_, args) =>
		{
			if (!string.IsNullOrEmpty(args.FullPath))
				debouncer.Push(Path.GetRelativePath(repo, args.FullPath), Environment.TickCount64);
		};
		RenamedEventHandler onRename = (_, args) =>
		{
			if (!string.IsNullOrEmpty(args.FullPath))
				debouncer.Push(Path.GetRelativePath(repo, args.FullPath), Environment.TickCount64);
		};
		ErrorEventHandler onError = (_, args) => Console.Error.WriteLine("cnative: watch error " + args.GetException().Message);
		using var srcWatcher = WatchDirectory(Path.Combine(repo, "src"), onChange, onRename, onError);
		using var sampleWatcher = WatchDirectory(Path.Combine(repo, "samples"), onChange, onRename, onError);
		while (!cancel.IsCancellationRequested)
		{
			var now = Environment.TickCount64;
			var remaining = debouncer.Remaining(now);
			if (remaining is null)
			{
				if (cancel.WaitHandle.WaitOne(25))
					break;
				continue;
			}

			if (remaining > 0)
			{
				if (cancel.WaitHandle.WaitOne((int)Math.Min(remaining.Value, 25)))
					break;
				continue;
			}

			var batch = debouncer.Drain(now);
			if (batch is null)
				continue;
			var effect = ChangeEffectRules.Classify(batch.Paths);
			switch (effect)
			{
				case ChangeEffect.Ignore:
					break;
				case ChangeEffect.ReloadScreen:
					NoteUnswapped(batch.Paths);
					if (batch.Paths.Any(ChangeEffectRules.IsHostScreen))
					{
						Console.WriteLine("cnative: reload src/NativePoc.Screen/Screen.cs");
						var outcome = Push(session, options, batch.FirstMs);
						if (outcome.Restarting)
							Relaunch(session, repo, rid, serial);
					}

					if (batch.Paths.Any(ChangeEffectRules.IsPackingScreen))
					{
						Console.WriteLine("cnative: reload samples/NativePoc.Packing/PackingScreen.cs");
						var started = Stopwatch.GetTimestamp();
						var packing = SendPacking(session, repo);
						if (!ReportPacking(packing, batch.FirstMs, started) && HostOutput.IsRude(packing.Text))
						{
							var packingInteractive = Interactive();
							var packingRestart = RestartPolicy.ShouldRestart(true, options.RestartOnRude, packingInteractive, ReadAnswer);
							var packingFollow = ReloadConsole.FollowUp(packingRestart, ReloadConsole.OfferRestartFlag(packingRestart, options.RestartOnRude));
							if (packingFollow is not null)
								Console.WriteLine(packingFollow);
							if (packingRestart)
								Relaunch(session, repo, rid, serial);
						}
					}

					break;
				case ChangeEffect.RestartRequired:
					ReportFileRude(batch.Paths);
					var interactive = Interactive();
					var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
					var follow = ReloadConsole.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude));
					if (follow is not null)
						Console.WriteLine(follow);
					if (restarting)
						Relaunch(session, repo, rid, serial);

					break;
				default:
					throw Unhandled(effect);
			}
		}

		Console.WriteLine("cnative: stopped");
		return 0;
	}

	static Exception UnhandledDecision(InitialDecision decision) =>
		decision switch
		{
			InitialDecision.ExitSuccess => new InvalidOperationException("ExitSuccess is handled."),
			InitialDecision.ExitFailure => new InvalidOperationException("ExitFailure is handled."),
			InitialDecision.Watch => new InvalidOperationException("Watch is handled."),
			InitialDecision.WatchAfterFailure => new InvalidOperationException("WatchAfterFailure is handled."),
		};

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
			if (normalized.StartsWith("samples/NativePoc.Packing/", StringComparison.Ordinal))
			{
				if (!ChangeEffectRules.IsPackingScreen(normalized))
					Console.WriteLine(normalized + " is not in the packing program. cnative lowers samples/NativePoc.Packing/PackingScreen.cs only.");
				continue;
			}

			if (!normalized.StartsWith("src/NativePoc.Screen/", StringComparison.Ordinal))
				continue;
			if (ChangeEffectRules.IsHostScreen(normalized))
				continue;
			Console.WriteLine(normalized + " is not in the screen program. cnative lowers src/NativePoc.Screen/Screen.cs only.");
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

	static PushOutcome Push(ReloadSession session, CliOptions options, long? editStartedMs)
	{
		var started = Stopwatch.GetTimestamp();
		CompileOutput compiled;
		try
		{
			compiled = session.Compiler.CompileFile();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Console.WriteLine("reload failed");
			Console.WriteLine(ex.Message);
			return new PushOutcome(ReloadStatus.Failed, false);
		}

		if (compiled.Assembly is null)
		{
			foreach (var error in compiled.Errors)
				Console.WriteLine(error.Display);
			if (compiled.Errors.Count == 0)
				Console.WriteLine("reload failed");
			return new PushOutcome(ReloadStatus.CompileError, false);
		}

		var lowered = ScreenLowerer.TryLower(session.Compiler.ReadSource());
		if (lowered.Program is null)
		{
			Console.WriteLine("screen program refused");
			Console.WriteLine(lowered.Reason ?? "The screen is not a program this runtime can carry.");
			return new PushOutcome(ReloadStatus.Failed, false);
		}

		ApplyResult applied;
		try
		{
			applied = session.Connection.Apply(lowered.Program);
		}
		catch (InvalidOperationException ex)
		{
			Console.WriteLine("reload failed");
			Console.WriteLine(ex.Message);
			return new PushOutcome(ReloadStatus.Failed, false);
		}

		var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		if (!applied.Ok)
		{
			if (HostOutput.IsRude(applied.Text))
			{
				Console.WriteLine("rude edit -> restart required");
				Console.WriteLine(applied.Text);
				var interactive = Interactive();
				var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
				var follow = ReloadConsole.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude));
				if (follow is not null)
					Console.WriteLine(follow);
				return new PushOutcome(ReloadStatus.RudeEdit, restarting);
			}

			Console.WriteLine("reload failed");
			Console.WriteLine(applied.Text);
			return new PushOutcome(ReloadStatus.Failed, false);
		}

		foreach (var line in ReloadConsole.AppliedLines(elapsed, applied.Milliseconds, applied.Text))
			Console.WriteLine(line);
		if (editStartedMs is long startedMs)
		{
			var watched = Environment.TickCount64 - startedMs;
			Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"edit-to-reply {watched} ms"));
		}

		return new PushOutcome(ReloadStatus.Applied, false);
	}

	internal readonly record struct ProgramPush(bool Applied, bool Refused, string Text);

	internal static ProgramPush SendFile(ReloadSession session)
	{
		CompileOutput compiled;
		try
		{
			compiled = session.Compiler.CompileFile();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return new ProgramPush(false, false, ex.Message);
		}

		if (compiled.Assembly is null)
		{
			var errors = compiled.Errors.Count == 0
				? "reload failed"
				: string.Join('\n', compiled.Errors.Select(error => error.Display));
			return new ProgramPush(false, false, errors);
		}

		var lowered = ScreenLowerer.TryLower(session.Compiler.ReadSource());
		if (lowered.Program is null)
			return new ProgramPush(false, true, lowered.Reason ?? "The screen is not a program this runtime can carry.");
		var applied = session.Connection.Apply(lowered.Program);
		return new ProgramPush(applied.Ok, false, applied.Text);
	}

	internal readonly record struct PackingSend(bool Applied, bool Refused, string Text, double Milliseconds);

	internal static PackingSend SendPacking(ReloadSession session, string repo)
	{
		var path = Path.Combine(repo, "samples", "NativePoc.Packing", "PackingScreen.cs");
		string source;
		try
		{
			source = File.ReadAllText(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return new PackingSend(false, false, ex.Message, 0);
		}

		var lowered = PackingLowerer.TryLower(source);
		if (lowered.Program is null)
			return new PackingSend(false, true, lowered.Reason ?? "The packing screen is not a program this runtime can carry.", 0);
		try
		{
			var applied = session.Connection.Apply(lowered.Program);
			return new PackingSend(applied.Ok, false, applied.Text, applied.Milliseconds);
		}
		catch (InvalidOperationException ex)
		{
			return new PackingSend(false, false, ex.Message, 0);
		}
	}

	static bool ReportPacking(PackingSend sent, long? editStartedMs, long startedTimestamp)
	{
		if (sent.Refused)
		{
			Console.WriteLine("packing program refused");
			Console.WriteLine(sent.Text);
			return false;
		}

		if (!sent.Applied)
		{
			Console.WriteLine(HostOutput.IsRude(sent.Text) ? "rude edit -> restart required" : "reload failed");
			Console.WriteLine(sent.Text);
			return false;
		}

		var elapsed = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
		foreach (var line in ReloadConsole.AppliedLines(elapsed, sent.Milliseconds, sent.Text))
			Console.WriteLine(line);
		if (editStartedMs is long startedMs)
			Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"edit-to-reply {Environment.TickCount64 - startedMs} ms"));
		return true;
	}

	static FileSystemWatcher WatchDirectory(
		string directory,
		FileSystemEventHandler onChange,
		RenamedEventHandler onRename,
		ErrorEventHandler onError)
	{
		var watcher = new FileSystemWatcher(directory)
		{
			IncludeSubdirectories = true,
			Filter = "*.cs",
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
		};
		watcher.Changed += onChange;
		watcher.Created += onChange;
		watcher.Renamed += onRename;
		watcher.Error += onError;
		watcher.EnableRaisingEvents = true;
		return watcher;
	}

	static bool Interactive() => !Console.IsInputRedirected;

	static string? ReadAnswer()
	{
		Console.Write("Restart the app? [y/N] ");
		return Console.ReadLine();
	}

	static void Relaunch(ReloadSession session, string repo, string rid, string serial)
	{
		byte[]? checkpoint = null;
		try
		{
			var reply = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
			if (!reply.Ok)
				throw new InvalidOperationException(reply.Text);
			var state = new AppState();
			if (!ScreenSnapshot.TryReadState(reply.Text, state))
				throw new InvalidOperationException("The state reply was not a checkpoint.");
			PackingCheckpoint? packing = null;
			if (PackingCheckpoint.TryParse(reply.Text, out var parsed))
				packing = parsed;
			checkpoint = StateCheckpoint.Write(state, packing);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.WriteLine("state was not captured: " + ex.Message);
		}

		session.Token = DeployAndLaunch(repo, rid, serial);
		session.Open(repo, serial);
		if (checkpoint is not null)
		{
			try
			{
				var restored = session.Connection.Apply(ScreenQuery.RestoreState(checkpoint));
				if (!restored.Ok || restored.Text != "restored")
					Console.WriteLine("state was not restored: " + restored.Text);
				else
					Console.WriteLine("state restored");
			}
			catch (InvalidOperationException ex)
			{
				Console.WriteLine("state was not restored: " + ex.Message);
			}
		}

		var pushed = SendFile(session);
		if (!pushed.Applied)
			Console.WriteLine("screen program was not restored: " + pushed.Text);
		ReportPacking(SendPacking(session, repo), editStartedMs: null, Stopwatch.GetTimestamp());
	}

	internal static FoundToken DeployAndLaunch(string repo, string rid, string serial, IReadOnlyList<string>? extraBuild = null)
	{
		Console.WriteLine("cnative: building Debug " + rid + " for " + serial);
		var project = Path.Combine(repo, "src", "NativePoc.Android", "NativePoc.Android.csproj");
		var arguments = new List<string>
		{
			"build",
			project,
			"-c", "Debug",
			"-f", "net10.0-android",
			"-r", rid,
			"-t:Run",
			"-p:AdbTarget=-s " + serial,
		};
		if (extraBuild is not null)
		{
			foreach (var extra in extraBuild)
				arguments.Add(extra);
		}

		var build = ProcessRunner.Run(
			"dotnet",
			arguments,
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
		var commands = LaunchCommand.BeforeStart(LaunchCommand.Package, component);
		for (var i = 0; i < commands.Count; i++)
		{
			if (i == 1)
				Thread.Sleep(300);
			var result = Adb(serial, commands[i]);
			if (i == commands.Count - 1 && result.Code != 0)
				throw new InvalidOperationException("am start failed: " + result.Stderr + result.Stdout);
		}
		var pid = WaitPid(serial);
		Console.WriteLine("cnative: pid " + pid);
		var token = WaitToken(serial);
		Console.WriteLine("cnative: token read via adb run-as (" + token.Source + ")");
		return token;
	}

	internal static string ResolveSerial(string? requested)
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

	internal static FoundToken WaitToken(string serial)
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

	internal static (int Code, string Stdout, string Stderr) Adb(string serial, params string[] args)
	{
		var all = new List<string> { "-s", serial };
		all.AddRange(args);
		return ProcessRunner.Run("adb", all);
	}
}
