using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace NativePoc;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, Theme = "@style/NativePocTheme")]
public sealed class MainActivity : Activity
{
	public const string Tag = "NativePoc";

	public static MainActivity? Current { get; private set; }

	Func<AppState, IPlatform, Element> _render = Screen.Render;
	RecordProgram.Bound? _record;
	AndroidHost? _host;
	ViewReconciler<View>? _reconciler;
	PlatformBridge? _platform;
	string _programSource = "";
	Element? _good;
	bool _inRender;
	bool _renderAgain;
	bool _benchFilled;
	bool _traceRender;
	bool _traceTap;
	bool _notedWindow;
	bool _painted;
	View? _paintedRoot;
	bool _paintedDark;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		ReloadDispatch.Install();
		ActionLog.OnScript = (micros, steps, stop) =>
		{
			if (stop is not null)
				Log.Info(Tag, stop);
			Log.Info(Tag, "action-us=" + micros.ToString(CultureInfo.InvariantCulture) + " steps=" + steps.ToString(CultureInfo.InvariantCulture));
			var tap = TapTrace.Line(micros);
			if (tap.Length > 0)
				Log.Info(Tag, tap);
			var phase = TapTrace.TakePhase();
			var deferred = TapTrace.TakeDeferred();
			if (phase.Length > 0)
				deferred = deferred.Length == 0 ? phase : deferred + " | " + phase;
			if (deferred.Length > 0)
			{
				var logStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				Log.Info(Tag, deferred);
				var phaselog = (System.Diagnostics.Stopwatch.GetTimestamp() - logStarted) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
				Log.Info(Tag, "phaselog-us=" + phaselog.ToString(CultureInfo.InvariantCulture));
			}

			if (_traceTap && Looper.MainLooper?.IsCurrentThread == true)
			{
				var started = System.Diagnostics.Stopwatch.GetTimestamp();
				Choreographer.Instance?.PostFrameCallback(new FrameWait(started));
			}
		};
#if DEBUG
		ActionLog.OnFault = (span, message) =>
		{
			DevOverlay.Show(this, "handler", message, span);
			Log.Info(Tag, "handler-fault span=" + span);
		};
#endif
		StartupMarks.Activity();
		ThemeRuntime.Load(this);
		ThemeRuntime.ApplyWindow(this);
		Current = this;
		Log.Info(Tag, "activity-start tick=" + System.Environment.TickCount64);
		Log.Info(
			Tag,
			"runtime IsDynamicCodeSupported="
			+ RuntimeFeature.IsDynamicCodeSupported
			+ " IsDynamicCodeCompiled="
			+ RuntimeFeature.IsDynamicCodeCompiled);
		_platform = new PlatformBridge(this);
		_host = new AndroidHost(this, () => _reconciler?.Current);
		_reconciler = new ViewReconciler<View>(_host);
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
		LocaleRuntime.Apply(this);
#if NATIVEPOC_RECYCLER
		if (string.Equals(Intent?.GetStringExtra("nativepoc_bind_log"), "1", StringComparison.Ordinal))
			KeyedList.LogBinds = true;
#endif
		var requestedRoot = Intent?.GetStringExtra("nativepoc_root");
#if NATIVEPOC_ROOT_HOME
		if (string.IsNullOrEmpty(requestedRoot))
			requestedRoot = "home";
#endif
		var wantsRoot = !string.IsNullOrEmpty(requestedRoot) && !string.Equals(requestedRoot, "counter", StringComparison.Ordinal);
		var navStart = SystemClock.ElapsedRealtime();
		CompiledScreens.Install(ScreenNav.Shared);
		NavCatalog.InstallRoot();
		if (Bench || wantsRoot)
			NavCatalog.InstallRest();
		var stagedMs = SystemClock.ElapsedRealtime() - navStart;
		var decodeStart = SystemClock.ElapsedRealtime();
		if (Bench)
			ScreenNav.Shared.Push("packing");
		else if (!string.IsNullOrEmpty(requestedRoot) && !string.Equals(requestedRoot, "counter", StringComparison.Ordinal))
			ScreenNav.Shared.Push(requestedRoot);
		var decodeMs = SystemClock.ElapsedRealtime() - decodeStart;
		Log.Info(
			Tag,
			"startup phase=nav ms=" + stagedMs.ToString(CultureInfo.InvariantCulture)
			+ " decode-ms=" + decodeMs.ToString(CultureInfo.InvariantCulture)
			+ " loaded=" + ScreenNav.Shared.Loaded.ToString(CultureInfo.InvariantCulture)
			+ " staged=" + ScreenNav.Shared.Staged.ToString(CultureInfo.InvariantCulture));
		StorageSeed.Apply(this);
#if DEBUG
		StateStore.TryLoad(this);
		ScreenNav.Shared.Prepare((program, bag) => RecordSession.Prepare(program, bag));
#endif
		if (string.Equals(Intent?.GetStringExtra("nativepoc_bench"), "tap10", StringComparison.Ordinal))
		{
			_traceTap = true;
			_traceRender = true;
			TapTrace.Enabled = true;
		}

		RenderNow();
#if NATIVEPOC_LOCATION
		ScheduleLocation();
#endif
		if (string.Equals(Intent?.GetStringExtra("nativepoc_keyed_move"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.GreetingFirst = true;
			RenderNow();
		}
#if DEBUG
		StateStore.Save(this);
		HotReloadServer.Start(this);
#endif
		if (!Bench && !wantsRoot)
		{
			_reconciler?.Root?.Post(() =>
			{
				var started = SystemClock.ElapsedRealtime();
				NavCatalog.InstallRest();
				StartupMarks.Note("catalog", SystemClock.ElapsedRealtime() - started);
#if DEBUG
				StateStore.Save(this);
#endif
			});
		}
		StartupMarks.Returned();
		LocaleRuntime.WarmAfterFirstFrame(this, _reconciler?.Root);
		LocaleRuntime.WhenWarmed(() => RequestRender());
		ScheduleLoopBench();
		ScheduleRenderBench();
	}

	void ScheduleRenderBench()
	{
		var bench = Intent?.GetStringExtra("nativepoc_bench");
		if (string.Equals(bench, "tap10", StringComparison.Ordinal))
		{
			LocaleRuntime.WhenWarmed(ScheduleTapBench);
			return;
		}

		if (!string.Equals(bench, "render10", StringComparison.Ordinal))
			return;
		_reconciler?.Root?.Post(() =>
		{
			for (var guard = 0; guard < 8; guard++)
			{
				if (ScreenNav.Shared.Top() is not { } top || top.ScreenId == RecordProgram.CounterScreenId)
					break;
				if (!ScreenNav.Shared.Pop())
					break;
			}

			RequestRender();
			for (var round = 0; round < 10; round++)
			{
				var top = ScreenNav.Shared.Top();
				if (top is { } bound && bound.ScreenId == RecordProgram.CounterScreenId)
				{
					bound.State.AddInt("Count", 1);
					Log.Info(Tag, "bench-count n=" + bound.State.ReadInt("Count").ToString(CultureInfo.InvariantCulture));
				}
				else
				{
					AppSession.Shared.State.Count++;
					Log.Info(Tag, "bench-count missing screen=" + (top is { } other ? other.ScreenId : "none"));
				}

				_traceRender = true;
				RequestRender();
				_traceRender = false;
			}
		});
	}

	void ScheduleTapBench()
	{
		_traceTap = true;
		_traceRender = true;
		TapTrace.Enabled = true;
		if (_reconciler?.Root is not View root || _platform is null)
			return;
		ThemeRuntime.Paint(this, root);
		_painted = true;
		_paintedRoot = root;
		_paintedDark = ThemeRuntime.IsDark(this);
		GC.Collect(0, GCCollectionMode.Forced, blocking: true);
		for (var warm = 0; warm < 8; warm++)
			RecordProgram.RunRenderRequest(_platform);
		Choreographer.Instance?.PostFrameCallback(new RunOnce(() => PostTapSeries(root)));
	}

	void PostTapSeries(View root)
	{
		if (_platform is not null)
		{
			for (var warm = 0; warm < 4; warm++)
				RecordProgram.RunRenderRequest(_platform);
		}

		Log.Info(Tag, "tap-bench-begin");
		if (_platform is not null)
			RecordProgram.RunRenderRequest(_platform);
		for (var round = 1; round < 10; round++)
		{
			var delay = round * 40;
			root.PostDelayed(() =>
			{
				if (_platform is not null)
					RecordProgram.RunRenderRequest(_platform);
			}, delay);
		}

		root.PostDelayed(() => Log.Info(Tag, "tap-single-begin"), 520);
		root.PostDelayed(() =>
		{
			var tree = CurrentTree();
			if (tree is null)
				return;
			try
			{
				ElementIds.Invoke(tree, "save", null);
			}
			catch (InvalidOperationException ex)
			{
				Log.Info(Tag, "tap-save-press " + ex.Message);
			}
		}, 600);
	}

	void ScheduleLoopBench()
	{
		if (!string.Equals(Intent?.GetStringExtra("nativepoc_bench"), "loop1000", StringComparison.Ordinal))
			return;
		_reconciler?.Root?.Post(() =>
		{
			for (var round = 0; round < 10; round++)
				LoopBench.Measure();
		});
	}

	bool Bench => string.Equals(Intent?.GetStringExtra("nativepoc_bench"), "packing1000", StringComparison.Ordinal);

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		if (intent is null)
			return;
		Intent = intent;
		if (string.Equals(intent.GetStringExtra("nativepoc_keyed_move"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.GreetingFirst = true;
			RequestRender();
		}
		if (string.Equals(intent.GetStringExtra("nativepoc_root_swap"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.PlainRoot = true;
			RequestRender();
		}

		ScheduleLoopBench();
		ScheduleRenderBench();
	}

	protected override void OnResume()
	{
		base.OnResume();
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
	}

	public void GoBack() => _platform?.Pop();

#pragma warning disable CA1422
	public override void OnBackPressed()
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			base.OnBackPressed();
			return;
		}

		if (!ActivityBack.HandleLegacy(this))
			base.OnBackPressed();
	}
#pragma warning restore CA1422

#if NATIVEPOC_LOCATION
	public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
	{
		LocationModule.OnResult(this, requestCode, permissions, grantResults);
		base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
	}

	void ScheduleLocation()
	{
		var mode = Intent?.GetStringExtra("nativepoc_location");
		if (mode is not ("read" or "probe"))
			return;
		var request = mode == "read";
		_reconciler?.Root?.Post(() =>
		{
			_ = ReportLocation(request);
		});
	}

	async Task ReportLocation(bool request)
	{
		var wire = await LocationModule.Read(this, request, CancellationToken.None);
		try
		{
			var fix = Location.Parse(wire);
			Log.Info(
				Tag,
				string.Create(
					CultureInfo.InvariantCulture,
					$"location access={fix.Access} lat={fix.Latitude} lon={fix.Longitude} accuracy={fix.AccuracyMeters}"));
		}
		catch (InvalidOperationException)
		{
			Log.Info(Tag, "location unparsed " + wire);
		}
	}
#endif

	protected override void OnDestroy()
	{
		if (ReferenceEquals(Current, this))
			Current = null;
		UiSurface.Clear(this);
		base.OnDestroy();
	}

	public void RequestRender()
	{
		if (_inRender)
		{
			_renderAgain = true;
			return;
		}

		void Run()
		{
			do
			{
				_renderAgain = false;
				_inRender = true;
				try
				{
					TapTrace.RenderIn();
					RenderNow();
				}
				finally
				{
					_inRender = false;
				}
			}
			while (_renderAgain);
			TapTrace.RenderOut();
		}

		if (Looper.MainLooper?.IsCurrentThread == true)
			Run();
		else
			WaitForUi(Run);
#if DEBUG
		var handoffStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		StateStore.Save(this);
		if (_traceRender || _traceTap)
		{
			var handoff = (System.Diagnostics.Stopwatch.GetTimestamp() - handoffStarted) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
			Log.Info(Tag, "state-handoff-us=" + handoff.ToString(CultureInfo.InvariantCulture));
		}
#endif
	}

	void WaitForUi(Action run)
	{
		Exception? error = null;
		using var done = new ManualResetEventSlim(false);
		RunOnUiThread(() =>
		{
			try
			{
				run();
			}
			catch (Exception ex)
			{
				error = ex;
			}
			finally
			{
				done.Set();
			}
		});
		if (!done.Wait(TimeSpan.FromSeconds(5)))
			throw new InvalidOperationException("The screen did not render.");
		if (error is not null)
			throw new InvalidOperationException("The screen did not render.", error);
	}

	public string UseRender(Func<AppState, IPlatform, Element> render)
	{
		var previousSource = _programSource;
		var previousRecord = _record;
		_record = null;
		_programSource = "";
		try
		{
			return ApplyRender(render);
		}
		catch
		{
			_record = previousRecord;
			_programSource = previousSource;
			throw;
		}
	}

	public string UseRecord(RecordProgram.Bound program) => UseProgram(program);

	public string UseProgram(RecordProgram.Bound program)
	{
		var previousSource = _programSource;
		var previousRecord = _record;
		_record = program;
		_programSource = program.SourceHash;
		try
		{
			RequestRender();
		}
		catch
		{
			_record = previousRecord;
			_programSource = previousSource;
			throw;
		}

#if DEBUG
		DevOverlay.Clear(this);
#endif
		return LastTitle;
	}

#if DEBUG
	string ResetKeptState()
	{
		var state = AppSession.Shared.State;
		state.Count = 0;
		state.Name = "";
		state.GreetingFirst = false;
		state.PlainRoot = false;
		PackingView.ResetToSeed();
		StateStore.Delete(this);
		ScreenNav.Shared.ResetBags();
		ScreenNav.Shared.Prepare((program, bag) => RecordSession.Prepare(program, bag));
		var top = ScreenNav.Shared.Top();
		if (top is null)
			return "state reset";
		if (PackingActivity.Current is { HasWindowFocus: true })
			PackingActivity.UseProgram(top.Value);
		else
			UseProgram(top.Value);
		return "state reset";
	}
#endif

	public string Answer(byte op, byte[] payload)
	{
		switch (op)
		{
			case ScreenQuery.Tree:
				return "source=" + _programSource + "\n" + ScreenSnapshot.FormatTree(CurrentTree()) + ScreenNav.Shared.Format();
			case ScreenQuery.State:
				var stateText = ScreenSnapshot.FormatState(AppSession.Shared.State) + PackingView.Capture().Format();
				if (_record is not null)
					stateText += _record.Value.FormatState();
				stateText += "nav=" + Convert.ToBase64String(NavCheckpoint.Write(ScreenNav.Shared.Checkpoint())) + "\n";
				return stateText;
			case ScreenQuery.Layout:
				return UiSurface.Layout();
			case ScreenQuery.Clock:
				return ClockRuntime.Step(() =>
				{
					RequestRender();
					PackingActivity.RequestCurrent();
				});
			case ScreenQuery.Restore:
				StateCheckpoint.ReadInto(payload.AsSpan(6), AppSession.Shared.State);
				if (StateCheckpoint.TryReadPacking(payload.AsSpan(6), out var packing))
					PackingView.Apply(packing);
				if (StateCheckpoint.TryReadNav(payload.AsSpan(6), out var nav))
					NavCatalog.ApplyCheckpoint(nav);
				RequestRender();
				PackingActivity.RequestCurrent();
				return "restored";
			case ScreenQuery.Press:
				return ElementIds.Invoke(RequireTree(), Encoding.UTF8.GetString(payload.AsSpan(6)), null);
			case ScreenQuery.Swipe:
				return ElementIds.Swipe(RequireTree(), Encoding.UTF8.GetString(payload.AsSpan(6)));
			case ScreenQuery.Submit:
				return ElementIds.Submit(RequireTree(), Encoding.UTF8.GetString(payload.AsSpan(6)));
			case ScreenQuery.Enter:
				var body = Encoding.UTF8.GetString(payload.AsSpan(6));
				var split = body.IndexOf('\n');
				if (split <= 0)
					throw new InvalidOperationException("Enter needs an element id.");
				return ElementIds.Invoke(RequireTree(), body[..split], body[(split + 1)..]);
			case ScreenQuery.Fault:
#if DEBUG
				var fault = Encoding.UTF8.GetString(payload.AsSpan(6));
				var lines = fault.Split('\n');
				if (lines.Length < 3)
					throw new InvalidOperationException("Rejected screen query.");
				DevOverlay.Show(this, lines[0], lines[1], lines[2]);
				return "overlay id=" + lines[0];
#else
				throw new InvalidOperationException("Unknown screen query.");
#endif
			case ScreenQuery.Dismiss:
#if DEBUG
				DevOverlay.Dismiss(this, log: true);
				return "overlay dismissed";
#else
				throw new InvalidOperationException("Unknown screen query.");
#endif
			case ScreenQuery.Reset:
#if DEBUG
				return ResetKeptState();
#else
				throw new InvalidOperationException("Unknown screen query.");
#endif
			case ScreenQuery.VmBody:
#if DEBUG
				var installed = RecordProgram.InstallReplacement(payload.AsSpan(6));
				Log.Info(Tag, "vm-body " + installed);
				return "vm-body " + installed;
#else
				throw new InvalidOperationException("Unknown screen query.");
#endif
			default:
				throw new InvalidOperationException("Unknown screen query.");
		}
	}

	Element? CurrentTree() => UiSurface.Current() ?? _reconciler?.Current;

	Element RequireTree() => CurrentTree() ?? throw new InvalidOperationException("No screen is current.");

	string ApplyRender(Func<AppState, IPlatform, Element> render)
	{
		var previous = _render;
		_render = render;
		try
		{
			RequestRender();
		}
		catch
		{
			_render = previous;
			throw;
		}
		return LastTitle;
	}

	public string LastTitle { get; private set; } = "";

	void RenderNow()
	{
		if (_reconciler is null || _platform is null || _host is null)
		{
			TapTrace.PrepDone();
			return;
		}

		var host = _host;
		var leaving = NavMotion.Detach(ref _reconciler, host);
		if (_reconciler is null)
		{
			TapTrace.PrepDone();
			return;
		}

		var gc0 = GC.CollectionCount(0);
		var alloc0 = GC.GetTotalAllocatedBytes(false);
		var prep = System.Diagnostics.Stopwatch.StartNew();
		var previousRoot = _reconciler.Root;
		var state = AppSession.Shared.State;
		var topId = ScreenNav.Shared.TopId();
		if (ScreenNav.Shared.Top() is { } top)
		{
			_record = top;
			_programSource = top.SourceHash;
			if (Bench && !_benchFilled)
			{
				BenchRows.Fill(top.State);
				_benchFilled = true;
				Log.Info(Tag, "bench rows=1000 screen=" + top.SourceHash);
			}
		}
		else if (topId.Length > 0 && CompiledScreens.Knows(topId))
		{
			_record = null;
			_programSource = "compiled";
		}

		if (_record is { } bound)
			RecordSession.Publish(bound);
		var prepUs = TakeUs(prep);
		TapTrace.PrepDone();
		Element tree;
		var clock = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			if (topId.Length > 0 && _record is null && CompiledScreens.TryRender(topId, _platform, out var compiled))
				tree = compiled;
			else
				tree = _record is { } record ? record.Render(_platform) : _render(state, _platform);
		}
		catch (Exception ex)
		{
#if DEBUG
			DevOverlay.Show(this, "render", ex.Message, "render");
#else
			_ = ex;
#endif
			throw;
		}

		var evalUs = TakeUs(clock);
		List<Patch> patches;
		try
		{
			patches = TreeDiff.Diff(_reconciler.Current, tree);
		}
		catch (Exception ex)
		{
#if DEBUG
			DevOverlay.Show(this, "render", ex.Message, "render");
#else
			_ = ex;
#endif
			throw;
		}

		var diffUs = TakeUs(clock);
		try
		{
			_reconciler.Commit(tree, patches);
		}
		catch (Exception ex)
		{
#if DEBUG
			DevOverlay.Show(this, "render", ex.Message, "render");
#else
			_ = ex;
#endif
			if (_good is not null)
			{
				try
				{
					_reconciler.Render(_good);
				}
				catch (Exception)
				{
				}
			}
			throw;
		}

		_good = tree;
		var reconcileUs = TakeUs(clock);
		if (!_notedWindow)
		{
			StartupMarks.Note("eval", evalUs / 1000);
			StartupMarks.Note("mount", reconcileUs / 1000);
			var rows = 0;
			var built = 0;
			Describe(tree, ref rows, ref built);
			var views = _reconciler.Root is View root ? CountViews(root) : 0;
			Log.Info(
				Tag,
				"startup window rows=" + rows.ToString(CultureInfo.InvariantCulture)
				+ " built=" + built.ToString(CultureInfo.InvariantCulture)
				+ " views=" + views.ToString(CultureInfo.InvariantCulture));
			_notedWindow = true;
		}

		StartupMarks.Render();
		if (_reconciler.Root is not View mounted)
			throw new InvalidOperationException("Screen did not mount a root view.");
		var bookUs = TakeUs(clock);
		if (mounted.Width > 0 && mounted.Height > 0)
		{
			mounted.Measure(
				View.MeasureSpec.MakeMeasureSpec(mounted.Width, MeasureSpecMode.Exactly),
				View.MeasureSpec.MakeMeasureSpec(mounted.Height, MeasureSpecMode.Exactly));
			mounted.Layout(mounted.Left, mounted.Top, mounted.Left + mounted.Width, mounted.Top + mounted.Height);
		}

		var layoutUs = TakeUs(clock);

		mounted.Invalidate();
		var invalidateUs = TakeUs(clock);
		var dark = ThemeRuntime.IsDark(this);
		var themeUs = TakeUs(clock);
		var paintRan = 0;
		if (!_painted || !ReferenceEquals(_paintedRoot, mounted) || _paintedDark != dark)
		{
			ThemeRuntime.Paint(this, mounted);
			_paintedRoot = mounted;
			_paintedDark = dark;
			_painted = true;
			paintRan = 1;
		}

		var paintUs = TakeUs(clock);
		LocaleRuntime.ApplyDirection(mounted);
		LocaleRuntime.ApplyDirection(this);
		var dirUs = TakeUs(clock);
		if (!ReferenceEquals(previousRoot, mounted) || leaving is not null)
			NavMotion.Present(this, mounted, leaving);
		ActivityBack.Sync(this);

		StartupMarks.Arm(this);
		var contentUs = TakeUs(clock);
		LastTitle = TitleOf(tree);
		var titleUs = TakeUs(clock);
		if (_host is not null && _reconciler.Root is not null)
			_host.RestoreFocus(_reconciler.Root);
		var focusUs = TakeUs(clock);
#if DEBUG
		if (Structural(patches) || !ReferenceEquals(previousRoot, mounted))
			LogTree();
#endif
		var afterUs = dirUs + contentUs + titleUs + focusUs;
		var gcDelta = GC.CollectionCount(0) - gc0;
		var allocDelta = GC.GetTotalAllocatedBytes(false) - alloc0;
		if (TapTrace.Enabled)
		{
			TapTrace.NotePhase(
				state.Count,
				state.Name.Length,
				LastTitle,
				evalUs,
				diffUs,
				reconcileUs,
				layoutUs,
				invalidateUs,
				themeUs,
				paintUs,
				paintRan,
				bookUs,
				prepUs,
				dirUs,
				contentUs,
				titleUs,
				focusUs,
				0,
				afterUs,
				0,
				gcDelta,
				allocDelta);
		}
		else if (_traceRender)
		{
			var rendered = "rendered count=" + state.Count.ToString(CultureInfo.InvariantCulture) + " name-length=" + state.Name.Length.ToString(CultureInfo.InvariantCulture) + " title=" + LastTitle;
			var logUs = TakeUs(clock);
			afterUs += logUs;
			var saveUs = TakeUs(clock);
			Log.Info(Tag, rendered);
			Log.Info(
				Tag,
				"render-phase eval=" + evalUs.ToString(CultureInfo.InvariantCulture)
				+ " diff=" + diffUs.ToString(CultureInfo.InvariantCulture)
				+ " reconcile=" + reconcileUs.ToString(CultureInfo.InvariantCulture)
				+ " layout=" + layoutUs.ToString(CultureInfo.InvariantCulture)
				+ " invalidate=" + invalidateUs.ToString(CultureInfo.InvariantCulture)
				+ " theme=" + themeUs.ToString(CultureInfo.InvariantCulture)
				+ " paint=" + paintUs.ToString(CultureInfo.InvariantCulture)
				+ " paint-ran=" + paintRan.ToString(CultureInfo.InvariantCulture)
				+ " book=" + bookUs.ToString(CultureInfo.InvariantCulture)
				+ " prep=" + prepUs.ToString(CultureInfo.InvariantCulture)
				+ " dir=" + dirUs.ToString(CultureInfo.InvariantCulture)
				+ " content=" + contentUs.ToString(CultureInfo.InvariantCulture)
				+ " title=" + titleUs.ToString(CultureInfo.InvariantCulture)
				+ " focus=" + focusUs.ToString(CultureInfo.InvariantCulture)
				+ " log=" + logUs.ToString(CultureInfo.InvariantCulture)
				+ " after=" + afterUs.ToString(CultureInfo.InvariantCulture)
				+ " save=" + saveUs.ToString(CultureInfo.InvariantCulture)
				+ " gc0=" + gcDelta.ToString(CultureInfo.InvariantCulture)
				+ " alloc=" + allocDelta.ToString(CultureInfo.InvariantCulture));
		}
		else
			Log.Info(Tag, "rendered count=" + state.Count.ToString(CultureInfo.InvariantCulture) + " name-length=" + state.Name.Length.ToString(CultureInfo.InvariantCulture) + " title=" + LastTitle);
	}

	static bool Structural(IReadOnlyList<Patch> patches)
	{
		foreach (var patch in patches)
		{
			if (patch is Mount or Unmount or Replace or Reorder)
				return true;
		}

		return false;
	}

	static long TakeUs(System.Diagnostics.Stopwatch clock)
	{
		var micros = clock.ElapsedTicks * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
		clock.Restart();
		return micros;
	}

	void LogTree()
	{
		if (_reconciler?.Root is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is null)
				continue;
			var text = child is TextView view ? view.Text ?? "" : "";
			Log.Info(
				Tag,
				"tree index=" + index + " handle=" + child.Handle.ToInt64() + " class=" + child.GetType().Name + " text=" + text);
		}
	}

	static void Describe(Element element, ref int rows, ref int built)
	{
		switch (element)
		{
			case ListView list when list.Rows is RecordProgram.WindowList window:
				rows += window.Count;
				built += window.Built;
				return;
			case Column column:
				foreach (var child in column.Children)
					Describe(child, ref rows, ref built);
				return;
			case ScrollView scroll:
				Describe(scroll.Child, ref rows, ref built);
				return;
			default:
				return;
		}
	}

	static int CountViews(View view)
	{
		if (view is not ViewGroup group)
			return 1;
		var count = 1;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				count += CountViews(child);
		}

		return count;
	}

	static string TitleOf(Element element)
	{
		switch (element)
		{
			case Column column:
				return column.Children.Count == 0 ? "" : TitleOf(column.Children[0]);
			case ScrollView scroll:
				return TitleOf(scroll.Child);
			case ListView list:
				return list.Rows.Count == 0 ? "" : TitleOf(list.Rows[0]);
			case Text text:
				return text.Value;
			case Button:
			case TextField:
			case TextInput:
			case Switch:
			case Image:
			case HostView:
				return "";
			default:
				throw Unknown(element);
		}
	}

	static InvalidOperationException Unknown(Element element) =>
		new("Unknown element.");
}

sealed class RunOnce : Java.Lang.Object, Choreographer.IFrameCallback
{
	readonly Action _run;

	public RunOnce(Action run) => _run = run;

	public void DoFrame(long frameTimeNanos) => _run();
}

sealed class FrameWait : Java.Lang.Object, Choreographer.IFrameCallback
{
	readonly long _started;

	public FrameWait(long started) => _started = started;

	public void DoFrame(long frameTimeNanos)
	{
		var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - _started;
		var micros = elapsed * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
		Log.Info(MainActivity.Tag, "frame-wait-us=" + micros.ToString(CultureInfo.InvariantCulture));
	}
}
