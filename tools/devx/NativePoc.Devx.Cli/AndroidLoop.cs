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

	public string Root { get; private set; } = "";

	public void Open(string repo, string serial)
	{
		Root = repo;
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
		if (AppRoot.TryFind(Directory.GetCurrentDirectory(), out var app))
			return AppLoop.Run(app, options);
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var serial = ResolveSerial(options.Serial);
		var abi = Prop(serial, "ro.product.cpu.abi");
		var rid = options.Abi ?? Abi.RidFromProperty(abi);
		Console.WriteLine(
			"cnative: " + serial
			+ " ro.product.cpu.abi=" + abi
			+ " rid=" + rid
			+ (options.Listener ? " NativeAOT-listener" : " Debug"));
		using var cancel = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cancel.Cancel();
		};
		var session = new ReloadSession();
		var kept = new KeptPrograms();
		try
		{
			var checkpoint = CaptureRunning(serial);
			session.Token = DeployAndLaunch(repo, rid, serial, listener: options.Listener, reuseRunning: true, launchExtras: LaunchRootExtra(options.Root));
			session.Open(repo, serial);
			Restore(session, checkpoint);
			var initial = Push(session, options, editStartedMs: null);
			if (initial.Restarting)
				Restart(session, repo, rid, serial, options, kept, initial.SubsetRefusal);
			else
			{
				var packingStarted = Stopwatch.GetTimestamp();
				var packing = SendPacking(session, repo, kept);
				if (!ReportApplied(PackingScreen, packing, editStartedMs: null, packingStarted) && options.Once)
					return 1;
				if (!ShowGenerated(session, repo, kept, options.Root) && options.Once)
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
					return Watch(repo, rid, serial, session, options, kept, cancel.Token);
				case InitialDecision.Watch:
					return Watch(repo, rid, serial, session, options, kept, cancel.Token);
				default:
					throw UnhandledDecision(decision);
			}
		}
		finally
		{
			session.Dispose();
		}
	}

	static int Watch(string repo, string rid, string serial, ReloadSession session, CliOptions options, KeptPrograms kept, CancellationToken cancel)
	{
		Console.WriteLine("cnative: watching src/NativePoc.Screen, samples/NativePoc.Packing, samples/NativePoc.Library, and samples/<name>");
		var debouncer = new ChangeDebouncer(options.DebounceMs);
		var knownScreens = new HashSet<string>(NewLoop.AppScreens(repo), StringComparer.Ordinal);
		var knownGate = new object();
		FileSystemEventHandler onChange = (_, args) =>
		{
			if (string.IsNullOrEmpty(args.FullPath))
				return;
			var relative = ChangeEffectRules.Normalize(Path.GetRelativePath(repo, args.FullPath));
			lock (knownGate)
				knownScreens.Add(relative);
			debouncer.Push(relative, Environment.TickCount64);
		};
		RenamedEventHandler onRename = (_, args) =>
		{
			if (string.IsNullOrEmpty(args.FullPath))
				return;
			var relative = ChangeEffectRules.Normalize(Path.GetRelativePath(repo, args.FullPath));
			lock (knownGate)
				knownScreens.Add(relative);
			debouncer.Push(relative, Environment.TickCount64);
		};
		ErrorEventHandler onError = (_, args) => Console.Error.WriteLine("cnative: watch error " + args.GetException().Message);
		using var srcWatcher = WatchDirectory(Path.Combine(repo, "src"), onChange, onRename, onError);
		using var sampleWatcher = WatchDirectory(Path.Combine(repo, "samples"), onChange, onRename, onError);
		var scanned = Environment.TickCount64;
		while (!cancel.IsCancellationRequested)
		{
			var now = Environment.TickCount64;
			if (now - scanned >= 400)
			{
				scanned = now;
				foreach (var relative in NewLoop.AppScreens(repo))
				{
					var added = false;
					lock (knownGate)
						added = knownScreens.Add(relative);
					if (added)
						debouncer.Push(relative, now);
				}
			}
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
					ApplyScreens(session, repo, rid, serial, options, kept, batch.Paths, batch.FirstMs);
					break;
				case ChangeEffect.RestartRequired:
					if (batch.Paths.Any(ChangeEffectRules.IsHostEdit))
					{
						ReportFileRude(batch.Paths);
					}
					else if (TrySwapBodies(session, repo, CallablePaths(batch.Paths), batch.FirstMs, out var swapRefusal))
					{
						if (swapRefusal is null)
						{
							var rest = batch.Paths.Where(path => !ChangeEffectRules.IsCallableSource(path)).ToList();
							if (rest.Count > 0)
								ApplyScreens(session, repo, rid, serial, options, kept, rest, batch.FirstMs);
							break;
						}

						Console.WriteLine(swapRefusal);
					}
					else
						ReportFileRude(batch.Paths);
					var interactive = Interactive();
					var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
					var follow = ReloadConsole.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude));
					if (follow is not null)
						Console.WriteLine(follow);
					if (restarting)
						Relaunch(session, repo, rid, serial, options.Listener, kept, options.Root);

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

	static List<string> CallablePaths(IReadOnlyList<string> paths)
	{
		var callables = new List<string>();
		foreach (var path in paths)
		{
			if (ChangeEffectRules.IsCallableSource(path))
				callables.Add(path);
		}

		return callables;
	}

	static void ApplyScreens(ReloadSession session, string repo, string rid, string serial, CliOptions options, KeptPrograms kept, IReadOnlyList<string> paths, long? firstMs)
	{
		NoteUnswapped(paths);
		if (paths.Any(ChangeEffectRules.IsHostScreen))
		{
			Console.WriteLine("cnative: reload src/NativePoc.Screen/Screen.cs");
			var outcome = Push(session, options, firstMs);
			if (outcome.Restarting)
				Restart(session, repo, rid, serial, options, kept, outcome.SubsetRefusal);
		}

		var screens = new List<string>();
		foreach (var path in paths)
		{
			var relative = ChangeEffectRules.Normalize(path);
			if (ChangeEffectRules.IsAppScreen(relative) || ChangeEffectRules.IsProjectScreen(relative))
				RememberScreen(screens, relative);
			else if (ChangeEffectRules.IsSampleCompanion(relative))
			{
				var folder = relative[..(relative.LastIndexOf('/') + 1)];
				foreach (var screen in NewLoop.AppScreens(repo))
				{
					if (screen.StartsWith(folder, StringComparison.Ordinal))
						RememberScreen(screens, screen);
				}
			}
		}

		foreach (var relative in screens)
		{
			Console.WriteLine("cnative: reload " + relative);
			var startedApp = Stopwatch.GetTimestamp();
			var sent = SendSource(session, repo, relative, kept);
			if (!ReportApplied(relative, sent, firstMs, startedApp) && (sent.Refused || HostOutput.IsRude(sent.Text)))
				OfferRestart(session, repo, rid, serial, options, kept, sent.Refused);
		}

		if (paths.Any(ChangeEffectRules.IsPackingScreen))
		{
			Console.WriteLine("cnative: reload samples/NativePoc.Packing/PackingScreen.cs");
			var started = Stopwatch.GetTimestamp();
			var packing = SendPacking(session, repo, kept);
			if (!ReportApplied(PackingScreen, packing, firstMs, started) && (packing.Refused || HostOutput.IsRude(packing.Text)))
				OfferRestart(session, repo, rid, serial, options, kept, packing.Refused);
		}
	}

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
			if (ChangeEffectRules.IsCallableSource(normalized))
				Console.WriteLine(normalized + " is compiled into the call table. The method body was not swapped. The running handler keeps the previous body until relaunch.");
		}
	}

	const string PackingScreen = "samples/NativePoc.Packing/PackingScreen.cs";

	static void RememberScreen(List<string> screens, string relative)
	{
		if (!screens.Contains(relative, StringComparer.Ordinal))
			screens.Add(relative);
	}

	static IReadOnlyList<string> CompanionTexts(string repo, string relative)
	{
		var texts = new List<string>();
		var normalized = ChangeEffectRules.Normalize(relative);
		var slash = normalized.LastIndexOf('/');
		if (slash <= 0)
			return texts;
		var dir = Path.Combine(repo, normalized[..slash].Replace('/', Path.DirectorySeparatorChar));
		if (!Directory.Exists(dir))
			return texts;
		foreach (var file in Directory.EnumerateFiles(dir, "*.cs").OrderBy(Path.GetFileName, StringComparer.Ordinal))
		{
			var candidate = normalized[..(slash + 1)] + Path.GetFileName(file);
			if (!ChangeEffectRules.IsSampleCompanion(candidate))
				continue;
			texts.Add(File.ReadAllText(file));
		}

		return texts;
	}

	static IReadOnlyList<string> CallableTexts(string repo)
	{
		var texts = new List<string>();
		if (repo.Length == 0)
			return texts;
		var service = Path.Combine(repo, "src", "NativePoc.Screen", "CounterService.cs");
		if (File.Exists(service))
			texts.Add(File.ReadAllText(service));
		var lib = Path.Combine(repo, "samples", "NativePoc.Library");
		if (!Directory.Exists(lib))
			return texts;
		foreach (var file in Directory.EnumerateFiles(lib, "*.cs").OrderBy(Path.GetFileName, StringComparer.Ordinal))
			texts.Add(File.ReadAllText(file));
		return texts;
	}

	readonly record struct PushOutcome(ReloadStatus Status, bool Restarting, bool SubsetRefusal = false);

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
			else
				ShowCompileError(session, compiled.Errors[0]);
			return new PushOutcome(ReloadStatus.CompileError, false);
		}

		var lowered = SubsetLowerer.TryLower(session.Compiler.ReadSource(), [], CallableTexts(session.Root), "src/NativePoc.Screen/Screen.cs");
		if (!lowered.Ok || lowered.Program is null)
		{
			ShowRefusal(session, lowered, "src/NativePoc.Screen/Screen.cs");
			const string host = "src/NativePoc.Screen/Screen.cs";
			if (lowered.Diagnostics.Count == 0)
				Console.WriteLine(host + ": The screen is not a program this runtime can carry.");
			else
				Console.WriteLine(DeveloperRefusal.Line(host, lowered.Diagnostics[0]));
			Console.WriteLine(DeveloperRefusal.Stays);
			var interactive = Interactive();
			var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, interactive, ReadAnswer);
			var follow = DeveloperRefusal.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude));
			if (follow is not null)
				Console.WriteLine(follow);
			return new PushOutcome(ReloadStatus.Failed, restarting, true);
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
			if (compiled.Errors.Count > 0)
				ShowCompileError(session, compiled.Errors[0]);
			return new ProgramPush(false, false, errors);
		}

		var lowered = SubsetLowerer.TryLower(session.Compiler.ReadSource(), [], CallableTexts(session.Root), "src/NativePoc.Screen/Screen.cs");
		if (!lowered.Ok || lowered.Program is null)
		{
			ShowRefusal(session, lowered, "src/NativePoc.Screen/Screen.cs");
			return new ProgramPush(false, true, lowered.Diagnostics.Count == 0 ? "The screen is not a program this runtime can carry." : lowered.Diagnostics[0].Message);
		}
		var applied = session.Connection.Apply(lowered.Program);
		return new ProgramPush(applied.Ok, false, applied.Text);
	}

	internal readonly record struct PackingSend(bool Applied, bool Refused, string Text, double Milliseconds, SubsetDiagnostic? Diagnostic = null, double LowerMilliseconds = 0);

	static bool ShowGenerated(ReloadSession session, string repo, KeptPrograms kept, string? root)
	{
		var ok = true;
		foreach (var relative in ScreensInOrder(repo, root))
		{
			Console.WriteLine("cnative: show " + relative);
			var started = Stopwatch.GetTimestamp();
			var sent = SendSource(session, repo, relative, kept);
			if (!ReportApplied(relative, sent, editStartedMs: null, started))
				ok = false;
		}

		return ok;
	}

	internal static PackingSend SendSource(ReloadSession session, string repo, string relative, KeptPrograms? kept = null, bool fault = true)
	{
		var path = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
		string source;
		try
		{
			source = File.ReadAllText(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return new PackingSend(false, false, ex.Message, 0);
		}

		return ApplySource(session, source, "The screen is not a program this runtime can carry.", kept, relative, fault, CompanionTexts(repo, relative), CallableTexts(repo));
	}

	internal static PackingSend SendPacking(ReloadSession session, string repo, KeptPrograms? kept = null, bool fault = true)
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

		return ApplySource(session, source, "The packing screen is not a program this runtime can carry.", kept, PackingScreen, fault, [], CallableTexts(repo));
	}

	static PackingSend ApplySource(
		ReloadSession session,
		string source,
		string refused,
		KeptPrograms? kept,
		string? relative,
		bool fault,
		IReadOnlyList<string>? companions = null,
		IReadOnlyList<string>? callables = null)
	{
		var lowerStarted = Stopwatch.GetTimestamp();
		var lowered = SubsetLowerer.TryLower(source, companions, callables, relative);
		var lowerMs = Stopwatch.GetElapsedTime(lowerStarted).TotalMilliseconds;
		if (!lowered.Ok || lowered.Program is null)
		{
			if (fault)
				ShowRefusal(session, lowered, relative);
			var diagnostic = lowered.Diagnostics.Count == 0 ? null : lowered.Diagnostics[0];
			return new PackingSend(false, true, diagnostic is null ? refused : diagnostic.Message, 0, diagnostic, lowerMs);
		}
		try
		{
			var applied = session.Connection.Apply(lowered.Program);
			if (applied.Ok && kept is not null && relative is not null)
				kept.Remember(relative, lowered.Program);
			return new PackingSend(applied.Ok, false, applied.Text, applied.Milliseconds, LowerMilliseconds: lowerMs);
		}
		catch (InvalidOperationException ex)
		{
			return new PackingSend(false, false, ex.Message, 0);
		}
	}

	internal static void ShowRefusal(ReloadSession session, SubsetResult lowered, string? file = null)
	{
		if (lowered.Diagnostics.Count == 0)
			return;
		var diagnostic = lowered.Diagnostics[0];
		try
		{
			session.Connection.Apply(ScreenQuery.FaultNote(diagnostic.Id, diagnostic.Message, diagnostic.Start, diagnostic.End, diagnostic.Line, diagnostic.Column, file));
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
		{
			_ = ex;
		}
	}

	static void ShowCompileError(ReloadSession session, FormattedDiagnostic error)
	{
		try
		{
			session.Connection.Apply(ScreenQuery.FaultNote(error.Code, error.Message, 0, 0, error.Line, error.Column, error.File));
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
		{
			_ = ex;
		}
	}

	static bool TrySwapBodies(ReloadSession session, string repo, IReadOnlyList<string> paths, long? editStartedMs, out string? refusal)
	{
		refusal = null;
		if (paths.Count == 0)
			return false;
		var files = new List<string>();
		foreach (var path in paths)
		{
			var normalized = ChangeEffectRules.Normalize(path);
			if (!ChangeEffectRules.IsCallableSource(normalized))
				return false;
			files.Add(normalized);
		}

		var started = Stopwatch.GetTimestamp();
		foreach (var relative in files)
		{
			string source;
			try
			{
				source = File.ReadAllText(Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar)));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				refusal = relative + " could not be read. Relaunching. " + ex.Message;
				return true;
			}

			if (!MethodBody.TryLower(source, out var bodies, out var why))
			{
				refusal = why;
				return true;
			}

			foreach (var body in bodies)
			{
				ApplyResult applied;
				try
				{
					applied = session.Connection.Apply(ScreenQuery.ReplaceBody(body.TypeName, body.MethodName, body.ReturnKind, body.ArgKinds, body.Code, body.Strings));
				}
				catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
				{
					refusal = body.TypeName + "." + body.MethodName + " was lowered, and the process did not install it. Relaunching. " + ex.Message;
					return true;
				}

				if (!applied.Ok)
				{
					refusal = body.TypeName + "." + body.MethodName + " was lowered, and the process did not install it. Relaunching. " + applied.Text;
					return true;
				}

				Console.WriteLine("swapped " + applied.Text + " in this process");
			}
		}

		var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"body-swap {elapsed:F1} ms"));
		if (editStartedMs is long startedMs)
			Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"edit-to-reply {Environment.TickCount64 - startedMs} ms"));
		return true;
	}

	static bool ReportApplied(string relative, PackingSend sent, long? editStartedMs, long startedTimestamp)
	{
		if (sent.Refused)
		{
			Console.WriteLine(sent.Diagnostic is null ? relative + ": " + sent.Text : DeveloperRefusal.Line(relative, sent.Diagnostic));
			Console.WriteLine(DeveloperRefusal.Stays);
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

	static void OfferRestart(ReloadSession session, string repo, string rid, string serial, CliOptions options, KeptPrograms kept, bool subset)
	{
		var restarting = RestartPolicy.ShouldRestart(true, options.RestartOnRude, Interactive(), ReadAnswer);
		var follow = subset
			? DeveloperRefusal.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude))
			: ReloadConsole.FollowUp(restarting, ReloadConsole.OfferRestartFlag(restarting, options.RestartOnRude));
		if (follow is not null)
			Console.WriteLine(follow);
		if (restarting)
			Restart(session, repo, rid, serial, options, kept, subset);
	}

	static void Restart(ReloadSession session, string repo, string rid, string serial, CliOptions options, KeptPrograms kept, bool subset)
	{
		if (subset)
			RelaunchInstalled(session, repo, serial, kept, options.Root);
		else
			Relaunch(session, repo, rid, serial, options.Listener, kept, options.Root);
	}

	static void Relaunch(ReloadSession session, string repo, string rid, string serial, bool listener, KeptPrograms kept, string? root)
	{
		byte[]? checkpoint = null;
		try
		{
			checkpoint = ReadCheckpoint(session.Connection);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.WriteLine("state was not captured: " + ex.Message);
		}

		session.Token = DeployAndLaunch(repo, rid, serial, listener: listener, launchExtras: LaunchRootExtra(root));
		session.Open(repo, serial);
		Restore(session, checkpoint);
		Replay(session, repo, kept, root);
	}

	static void RelaunchInstalled(ReloadSession session, string repo, string serial, KeptPrograms kept, string? root)
	{
		byte[]? checkpoint = null;
		try
		{
			checkpoint = ReadCheckpoint(session.Connection);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.WriteLine("state was not captured: " + ex.Message);
		}

		Console.WriteLine("cnative: relaunching the installed app");
		session.Token = LaunchInstalled(serial, LaunchRootExtra(root));
		session.Open(repo, serial);
		Restore(session, checkpoint);
		Replay(session, repo, kept, root);
	}

	static IEnumerable<string> ScreensInOrder(string repo, string? root)
	{
		var screens = NewLoop.AppScreens(repo);
		if (string.IsNullOrEmpty(root))
			return screens;
		var preferred = "samples/" + root + "/" + NewLoop.TypeName(root) + "Screen.cs";
		var ordered = new List<string>();
		foreach (var relative in screens)
		{
			if (!string.Equals(relative, preferred, StringComparison.Ordinal))
				ordered.Add(relative);
		}

		if (screens.Contains(preferred))
			ordered.Add(preferred);
		return ordered;
	}

	static void Replay(ReloadSession session, string repo, KeptPrograms kept, string? root)
	{
		var host = SendFile(session);
		Console.WriteLine(host.Applied ? "screen program restored: " + host.Text : "screen program was not restored: " + host.Text);
		var packingStarted = Stopwatch.GetTimestamp();
		var packing = SendPacking(session, repo, kept, fault: false);
		if (!ReportApplied(PackingScreen, packing, editStartedMs: null, packingStarted))
			RestoreRemembered(session, PackingScreen, kept);
		foreach (var relative in ScreensInOrder(repo, root))
		{
			var started = Stopwatch.GetTimestamp();
			var sent = SendSource(session, repo, relative, kept, fault: false);
			if (sent.Applied)
			{
				Console.WriteLine("cnative: show " + relative);
				ReportApplied(relative, sent, editStartedMs: null, started);
				continue;
			}

			if (!RestoreRemembered(session, relative, kept))
				ReportApplied(relative, sent, editStartedMs: null, started);
		}
	}

	static bool RestoreRemembered(ReloadSession session, string relative, KeptPrograms kept)
	{
		if (!kept.TryGet(relative, out var program))
			return false;
		var started = Stopwatch.GetTimestamp();
		try
		{
			var applied = session.Connection.Apply(program);
			if (!applied.Ok)
			{
				Console.WriteLine("cnative: last program was not restored " + relative);
				Console.WriteLine(applied.Text);
				return true;
			}

			Console.WriteLine("cnative: restored last program " + relative);
			var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			foreach (var line in ReloadConsole.AppliedLines(elapsed, applied.Milliseconds, applied.Text))
				Console.WriteLine(line);
			return true;
		}
		catch (InvalidOperationException ex)
		{
			Console.WriteLine("cnative: last program was not restored " + relative);
			Console.WriteLine(ex.Message);
			return true;
		}
	}

	internal static byte[]? CaptureRunning(string serial)
	{
		var pid = Adb(serial, "shell", "pidof", LaunchCommand.Package);
		if (pid.Stdout.Trim().Length == 0)
			return null;
		var token = ReadTokenOnce(serial);
		if (token.Hex.Length == 0)
		{
			Console.WriteLine("state was not captured: the running process has no token.");
			return null;
		}

		try
		{
			using var connection = new HotReloadConnection(serial, token.Hex);
			return ReadCheckpoint(connection);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.WriteLine("state was not captured: " + ex.Message);
			return null;
		}
	}

	internal static void Restore(ReloadSession session, byte[]? checkpoint)
	{
		if (checkpoint is null)
			return;
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

	static byte[] ReadCheckpoint(HotReloadConnection connection)
	{
		var reply = connection.Apply(ScreenQuery.Ask(ScreenQuery.State));
		if (!reply.Ok)
			throw new InvalidOperationException(reply.Text);
		var state = new AppState();
		if (!ScreenSnapshot.TryReadState(reply.Text, state))
			throw new InvalidOperationException("The state reply was not a checkpoint.");
		PackingCheckpoint? packing = null;
		if (PackingCheckpoint.TryParse(reply.Text, out var parsed))
			packing = parsed;
		NavCheckpoint? nav = null;
		if (ScreenSnapshot.TryReadNavLine(reply.Text, out var readNav))
			nav = readNav;
		return StateCheckpoint.Write(state, packing, nav);
	}

	static FoundToken ReadTokenOnce(string serial)
	{
		foreach (var relative in TokenFiles.RelativePaths)
		{
			var result = Adb(serial, "shell", "run-as", LaunchCommand.Package, "cat", relative);
			if (TokenText.TryParse(result.Stdout, out var hex))
				return new FoundToken(hex, relative);
		}

		return new FoundToken("", "");
	}

	static void InstallApk(string serial, string apk)
	{
		var installed = Adb(serial, "install", "-r", "-d", apk);
		if (installed.Code == 0 && installed.Stdout.Contains("Success", StringComparison.Ordinal))
			return;
		Adb(serial, "uninstall", LaunchCommand.Package);
		var again = Adb(serial, "install", "-r", apk);
		if (again.Code != 0 || !again.Stdout.Contains("Success", StringComparison.Ordinal))
			throw new InvalidOperationException("Install failed: " + again.Stderr + again.Stdout + installed.Stderr + installed.Stdout);
	}

	static string? LaunchRootExtra(string? root) =>
		string.IsNullOrEmpty(root) ? null : "--es nativepoc_root " + root;

	internal static FoundToken DeployAndLaunch(string repo, string rid, string serial, IReadOnlyList<string>? extraBuild = null, bool listener = false, bool reuseRunning = false, string? launchExtras = null)
	{
		var project = Path.Combine(repo, "src", "NativePoc.Android", "NativePoc.Android.csproj");
		if (listener)
		{
			Console.WriteLine("cnative: publishing NativeAOT listener " + rid + " for " + serial);
			var published = ProcessRunner.Run(
				"dotnet",
				AndroidBuild.ListenerPublish(project, rid),
				repo,
				Console.WriteLine,
				Console.Error.WriteLine);
			if (published.Code != 0)
				throw new InvalidOperationException("Listener publish exited " + published.Code + ".");
			var apk = AndroidBuild.RequireSignedApk(AndroidBuild.PublishDirectory(repo, "Debug", rid));
			if (!reuseRunning)
			{
				InstallApk(serial, apk);
				return LaunchInstalled(serial, launchExtras);
			}

			InstallOrAttach(serial, apk, launchExtras, out var token);
			return token;
		}

		if (!reuseRunning)
		{
			Console.WriteLine("cnative: building Debug " + rid + " for " + serial);
			var arguments = new List<string>(AndroidBuild.DebugRun(project, rid, serial));
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
			return LaunchInstalled(serial, launchExtras);
		}

		Console.WriteLine("cnative: building Debug " + rid + " for " + serial);
		var debugArgs = new List<string>(AndroidBuild.DebugBuild(project, rid));
		if (extraBuild is not null)
		{
			foreach (var extra in extraBuild)
				debugArgs.Add(extra);
		}

		var built = ProcessRunner.Run(
			"dotnet",
			debugArgs,
			repo,
			Console.WriteLine,
			Console.Error.WriteLine);
		if (built.Code != 0)
			throw new InvalidOperationException("Debug build exited " + built.Code + ".");
		var debugApk = AndroidBuild.RequireSignedApk(AndroidBuild.DebugDirectory(repo, "Debug", rid));
		InstallOrAttach(serial, debugApk, launchExtras, out var running);
		return running;
	}

	internal static bool InstallOrAttach(string serial, string apk, string? extras, out FoundToken token)
	{
		var local = DeviceInstall.HashFile(apk);
		var listed = Adb(serial, "shell", "pm", "path", LaunchCommand.Package);
		var remote = DeviceInstall.PackagePath(listed.Stdout);
		var matches = false;
		if (remote is not null)
		{
			var sum = Adb(serial, "shell", "sha256sum", remote);
			matches = sum.Code == 0 && DeviceInstall.SameHash(local, sum.Stdout);
		}

		if (matches)
			Console.WriteLine("cnative: installed apk matches. Skipping install.");
		else
			InstallApk(serial, apk);
		var running = ReadTokenOnce(serial);
		if (matches && running.Hex.Length > 0)
		{
			Console.WriteLine("cnative: listener already running. Skipping launch.");
			token = running;
			return true;
		}

		token = LaunchInstalled(serial, extras);
		return false;
	}

	internal static FoundToken LaunchInstalled(string serial) => LaunchInstalled(serial, null);

	internal static FoundToken LaunchInstalled(string serial, string? extras)
	{
		var resolved = Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
		var component = ActivityName.FromResolve(resolved.Stdout)
			?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout + resolved.Stderr);
		var startArgs = LaunchCommand.Start(component);
		if (LaunchCommand.PassesToken(startArgs) || (extras is not null && extras.Contains("nativepoc_hotreload", StringComparison.Ordinal)))
			throw new InvalidOperationException("Refusing to pass the hot reload token as a launch extra.");
		Console.WriteLine("cnative: launching " + component);
		var commands = LaunchCommand.BeforeStart(LaunchCommand.Package, component, extras);
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

	internal static int ResetState(string[] args)
	{
		string? serial = null;
		for (var index = 1; index < args.Length; index++)
		{
			if (args[index] == "--device" && index + 1 < args.Length && !args[index + 1].StartsWith('-'))
			{
				serial = args[++index];
				continue;
			}

			throw new UsageException("Usage: cnative reset-state [--device <serial>]");
		}

		serial = ResolveSerial(serial);
		var token = ReadTokenOnce(serial);
		if (token.Hex.Length == 0)
			throw new InvalidOperationException("The running process has no hot reload token.");
		using var connection = new HotReloadConnection(serial, token.Hex);
		var reply = connection.Apply(ScreenQuery.Ask(ScreenQuery.Reset));
		if (!reply.Ok || reply.Text != "state reset")
		{
			Console.WriteLine("state was not reset: " + reply.Text);
			return 1;
		}

		Adb(serial, "shell", "run-as", LaunchCommand.Package, "rm", "-f", "files/nativepoc-state.bin");
		Console.WriteLine("state reset");
		return 0;
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

	internal static string WaitPid(string serial)
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
