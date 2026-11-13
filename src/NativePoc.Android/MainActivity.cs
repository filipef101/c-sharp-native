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

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, Theme = "@style/NativePocTheme", ConfigurationChanges = ConfigChanges.UiMode)]
public sealed class MainActivity : Activity
{
	public const string Tag = "NativePoc";

#if NATIVEPOC_STARTUP_MARKS
	static MainActivity()
	{
		try
		{
			StartupMarks.Phase("jni-activity");
		}
		catch (Exception)
		{
			// The type initializer must not stay failed if this runs before the clock is ready.
		}
	}
#endif

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
	bool _startupCost;
	bool _painted;
	View? _paintedRoot;
	bool _paintedDark;
	bool _restyle;

	protected override void AttachBaseContext(Context? context)
	{
		base.AttachBaseContext(ThemeRuntime.Wrap(context));
	}

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		StartupMarks.Phase("activity-enter");
		base.OnCreate(savedInstanceState);
		if ((int)Build.VERSION.SdkInt >= 30 && Window is { } window)
		{
#pragma warning disable CA1416, CA1422
			window.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1416, CA1422
		}

		StartupMarks.Phase("activity-base");
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
		CrashReport.Install();
#if NATIVEPOC_STARTUP_MARKS
		var themeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
		ThemeRuntime.Load(this);
		ThemeRuntime.ApplyWindow(this);
#if NATIVEPOC_STARTUP_MARKS
		StartupMarks.Took("theme-resolve", themeStarted);
#endif
		Current = this;
		JniSink.Deliver = (screen, field, value) =>
		{
			RunOnUiThread(() =>
			{
				ScreenNav.Shared.Bag(screen).WriteString(field, value);
				CompiledScreens.SetString(screen, field, value);
				Log.Info(Tag, "jni-sink screen=" + screen + " field=" + field + " text=" + value);
				RequestRender();
			});
		};
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
#if NATIVEPOC_WIDGET_FLOOR
		if (string.IsNullOrEmpty(requestedRoot))
			requestedRoot = "floor";
#elif NATIVEPOC_ROOT_HOME
		if (string.IsNullOrEmpty(requestedRoot))
			requestedRoot = "home";
#endif
		var launchRoot = string.IsNullOrEmpty(requestedRoot) ? "counter" : requestedRoot;
		ScreenNav.Shared.LaunchRoot = launchRoot;
		var wantsRoot = !string.Equals(launchRoot, "counter", StringComparison.Ordinal);
		var compiledRoot = wantsRoot && CompiledScreens.Knows(launchRoot);
		var navTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		var navStart = SystemClock.ElapsedRealtime();
		CompiledScreens.Install(ScreenNav.Shared);
		if (Bench || (wantsRoot && !compiledRoot))
			NavCatalog.InstallRest();
		var stagedMs = SystemClock.ElapsedRealtime() - navStart;
		var stagedUs = (System.Diagnostics.Stopwatch.GetTimestamp() - navTicks) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
		var decodeStart = SystemClock.ElapsedRealtime();
		if (Bench)
		{
			NavCatalog.InstallRoot();
			ScreenNav.Shared.Push("packing");
		}
		else if (!wantsRoot)
			NavCatalog.InstallRoot();
		else if (!ScreenNav.Shared.ReplaceRoot(launchRoot))
		{
			Log.Info(Tag, StartupGuard.RootLog(launchRoot));
			launchRoot = StartupGuard.DefaultId;
			ScreenNav.Shared.LaunchRoot = launchRoot;
			wantsRoot = false;
			compiledRoot = false;
			NavCatalog.InstallRoot();
		}
		var decodeMs = SystemClock.ElapsedRealtime() - decodeStart;
		Log.Info(
			Tag,
			"startup phase=nav ms=" + stagedMs.ToString(CultureInfo.InvariantCulture)
			+ " us=" + stagedUs.ToString(CultureInfo.InvariantCulture)
			+ " decode-ms=" + decodeMs.ToString(CultureInfo.InvariantCulture)
			+ " loaded=" + ScreenNav.Shared.Loaded.ToString(CultureInfo.InvariantCulture)
			+ " staged=" + ScreenNav.Shared.Staged.ToString(CultureInfo.InvariantCulture)
			+ " depth=" + ScreenNav.Shared.Depth.ToString(CultureInfo.InvariantCulture)
			+ " top=" + ScreenNav.Shared.TopId());
		StartupMarks.Phase("nav");
		StorageSeed.Apply(this);
#if DEBUG
		var stateStarted = SystemClock.ElapsedRealtime();
		var loadedState = StateStore.TryLoad(this);
		var stateMs = SystemClock.ElapsedRealtime() - stateStarted;
		StartupMarks.Note("state-load", stateMs);
		StartupMarks.Note("nav-restore", loadedState ? stateMs : 0);
		if (!string.IsNullOrEmpty(requestedRoot) && string.Equals(requestedRoot, launchRoot, StringComparison.Ordinal))
		{
			var claimed = ScreenNav.Shared.ClaimRoot(requestedRoot);
			Log.Info(
				Tag,
				"startup root explicit id=" + requestedRoot
				+ " claimed=" + (claimed ? "1" : "0")
				+ " top=" + ScreenNav.Shared.TopId()
				+ " note=" + ScreenNav.Shared.Bag(requestedRoot).ReadString("Note")
				+ " draft=" + ScreenNav.Shared.Bag(requestedRoot).ReadString("Draft")
				+ " saved=" + ScreenNav.Shared.Bag(requestedRoot).ReadString("Saved"));
		}
		else if (!string.IsNullOrEmpty(requestedRoot))
		{
			while (ScreenNav.Shared.DropTop())
			{
			}

			NavCatalog.InstallRoot();
			Log.Info(
				Tag,
				"startup root explicit id=" + requestedRoot
				+ " claimed=0 top=" + ScreenNav.Shared.TopId());
		}
#else
		// Release does not open nativepoc-state.bin and does not replay a nav checkpoint.
		StartupMarks.Note("state-load", 0);
		StartupMarks.Note("nav-restore", 0);
#endif
		var preparePrograms = RestoreInstance(savedInstanceState);
#if DEBUG
		preparePrograms = true;
#endif
		if (preparePrograms && !StartupGuard.Prepare(ScreenNav.Shared, (program, bag) => RecordSession.Prepare(program, bag), out var rejectedScreen, out var rejectedReason))
		{
			Log.Info(Tag, StartupGuard.RenderLog(rejectedScreen, rejectedReason));
			NavCatalog.InstallRoot();
		}
		// Notes render does not call Persist.Get. A read happens when a screen awaits one.
		StartupMarks.Note("persist-read", 0);
		if (string.Equals(Intent?.GetStringExtra("nativepoc_bench"), "tap10", StringComparison.Ordinal))
		{
			_traceTap = true;
			_traceRender = true;
			TapTrace.Enabled = true;
		}

#if DEBUG && NATIVEPOC_PROGRAM_ONLY
		var warmStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			UseProgram(RecordProgram.WarmProgram());
		}
		catch (Exception ex)
		{
			Log.Info(Tag, "reload-warm failed " + ex.GetType().Name);
		}

		_record = null;
		_programSource = "";
		var warmUs = (System.Diagnostics.Stopwatch.GetTimestamp() - warmStarted) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
		Log.Info(Tag, "reload-warm us=" + warmUs.ToString(CultureInfo.InvariantCulture));
		// The warm tree is on screen. Hold it until the requested root program
		// arrives, so the compiled counter is not shown in its place.
		var rootWaiting = ScreenNav.Shared.Depth == 0 && ScreenNav.Shared.LaunchRoot.Length > 0;
		if (rootWaiting)
			Log.Info(Tag, "startup root holding id=" + ScreenNav.Shared.LaunchRoot);
		else
#endif
		RenderNow();
		if (string.Equals(Intent?.GetStringExtra("nativepoc_a11y"), "1", StringComparison.Ordinal))
			_reconciler?.Root?.Post(() =>
			{
				if (Window?.DecorView is View decor)
					A11yTree.Write(decor);
			});
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
		if (!Bench && (compiledRoot || !wantsRoot))
		{
			LocaleRuntime.WhenWarmed(() =>
			{
#if NATIVEPOC_STARTUP_MARKS
				var started = SystemClock.ElapsedRealtime();
#endif
				if (compiledRoot)
					NavCatalog.InstallRoot();
				NavCatalog.InstallRest();
#if NATIVEPOC_STARTUP_MARKS
				StartupMarks.Note("catalog", SystemClock.ElapsedRealtime() - started);
#endif
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
		CrashReport.Arm(this);
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
		var warmRoot = intent.GetStringExtra("nativepoc_root");
		if (!string.IsNullOrEmpty(warmRoot))
		{
			var before = ScreenNav.Shared.TopId();
			var beforeDepth = ScreenNav.Shared.Depth;
			if (WarmRoot.Same(before, warmRoot))
			{
				_platform?.RefreshTop(warmRoot);
				Log.Info(
					Tag,
					"startup root warm same id=" + warmRoot
					+ " top=" + ScreenNav.Shared.TopId()
					+ " depth=" + ScreenNav.Shared.Depth.ToString(CultureInfo.InvariantCulture)
					+ " slide=0");
			}
			else
			{
				_platform?.Push(warmRoot);
				Log.Info(
					Tag,
					"startup root warm id=" + warmRoot
					+ " top=" + ScreenNav.Shared.TopId()
					+ " depth=" + ScreenNav.Shared.Depth.ToString(CultureInfo.InvariantCulture)
					+ " from=" + before
					+ " from-depth=" + beforeDepth.ToString(CultureInfo.InvariantCulture));
			}

			RequestRender();
		}
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

	protected override void OnSaveInstanceState(Bundle outState)
	{
		base.OnSaveInstanceState(outState);
		var state = AppSession.Shared.State;
		outState.PutInt(InstanceUi.CountKey, state.Count);
		outState.PutString(InstanceUi.NameKey, state.Name ?? "");
		outState.PutInt(InstanceUi.FlagsKey, InstanceUi.Flags(state));
		Log.Info(
			Tag,
			"state-save instance count=" + state.Count.ToString(CultureInfo.InvariantCulture)
			+ " name-length=" + (state.Name?.Length ?? 0).ToString(CultureInfo.InvariantCulture));
	}

	bool RestoreInstance(Bundle? saved)
	{
		if (saved is null || !saved.ContainsKey(InstanceUi.CountKey))
		{
			Log.Info(Tag, "state-restore instance=0");
			return false;
		}

		var state = AppSession.Shared.State;
		InstanceUi.Apply(state, saved.GetInt(InstanceUi.CountKey), saved.GetString(InstanceUi.NameKey), saved.GetInt(InstanceUi.FlagsKey));
		Log.Info(
			Tag,
			"state-restore instance=1 count=" + state.Count.ToString(CultureInfo.InvariantCulture)
			+ " name-length=" + state.Name.Length.ToString(CultureInfo.InvariantCulture));
		return true;
	}

	protected override void OnPause()
	{
		FieldIme.Hide(this);
		Log.Info(Tag, "lifecycle pause delivered=" + (ScreenSignals.Pause is null ? "0" : "1"));
		ScreenSignals.FirePause();
		base.OnPause();
	}

	protected override void OnResume()
	{
		base.OnResume();
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
		StartupMarks.Phase("resume");
		if (ThemeRuntime.Follow(this, "resume"))
		{
			_restyle = true;
			_painted = false;
			RequestRender();
		}

		Log.Info(Tag, "lifecycle resume delivered=" + (ScreenSignals.Resume is null ? "0" : "1"));
		ScreenSignals.FireResume();
	}

	public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
	{
		base.OnConfigurationChanged(newConfig);
		if (!ThemeRuntime.Follow(this, "uimode"))
			return;
		_restyle = true;
		_painted = false;
		RequestRender();
	}

	public override void OnWindowFocusChanged(bool hasFocus)
	{
		base.OnWindowFocusChanged(hasFocus);
		if (hasFocus)
			StartupMarks.Phase("focus");
	}

	public void GoBack() => _platform?.Pop();

	internal View? PaintBelow()
	{
		if (_platform is null)
			return null;
		var id = ScreenNav.Shared.UnderId();
		if (id.Length == 0)
			return null;
		Element tree;
		if (ScreenNav.Shared.Below() is { } bound)
			tree = bound.Render(_platform);
		else if (!CompiledScreens.TryRender(id, _platform, out tree))
			return null;
		var host = new AndroidHost(this, () => tree);
		host.PaintReady();
		var reconciler = new ViewReconciler<View>(host);
		reconciler.Render(tree);
		return reconciler.Root;
	}

	internal void GoPush(string screen) => _platform?.Push(screen);

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

#if NATIVEPOC_LOCATION || NATIVEPOC_JNI
	public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
	{
#if NATIVEPOC_LOCATION
		LocationModule.OnResult(this, requestCode, permissions, grantResults);
#endif
#if NATIVEPOC_JNI
		DeviceNotify.OnResult(requestCode, permissions, grantResults);
#endif
		base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
	}
#endif

#if NATIVEPOC_LOCATION
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

#if NATIVEPOC_FAST_NATIVES
	internal void FastOnCreate(Bundle? saved) => OnCreate(saved);

	internal void FastOnNewIntent(Intent? intent) => OnNewIntent(intent);

	internal void FastOnResume() => OnResume();

	internal void FastOnDestroy() => OnDestroy();

#pragma warning disable CA1422
	internal void FastOnBackPressed() => OnBackPressed();
#pragma warning restore CA1422
#endif

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

	internal string UseRecord(RecordProgram.Bound program) => UseProgram(program);

	internal string UseProgram(RecordProgram.Bound program)
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
		if (!StartupGuard.Prepare(ScreenNav.Shared, (program, bag) => RecordSession.Prepare(program, bag), out var rejectedScreen, out var rejectedReason))
		{
			Log.Info(Tag, StartupGuard.RenderLog(rejectedScreen, rejectedReason));
			NavCatalog.InstallRoot();
		}

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
		RecordProgram.Bound? opened = null;
		var fellBack = false;
		try
		{
			opened = ScreenNav.Shared.Top();
		}
		catch (Exception ex) when (StartupGuard.Rejected(ex) && _good is null)
		{
			Log.Info(Tag, StartupGuard.RenderLog(topId.Length == 0 ? StartupGuard.DefaultId : topId, ex.Message));
			ScreenNav.Shared.DropTop();
			_record = null;
			opened = null;
			topId = "";
			fellBack = true;
		}

		if (opened is { } top)
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
		// A program-only launch can be waiting on its root. The warm tree is already
		// mounted. Leave it until that root program arrives.
		if (!fellBack && _record is null && topId.Length == 0 && previousRoot is not null && ScreenNav.Shared.LaunchRoot.Length > 0)
			return;
		ScreenSignals.Pause = null;
		ScreenSignals.Resume = null;
		ScreenSignals.Leave = null;
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
			if (StartupGuard.Rejected(ex) && _good is null)
			{
				Log.Info(Tag, StartupGuard.RenderLog(topId.Length == 0 ? StartupGuard.DefaultId : topId, ex.Message));
				ScreenNav.Shared.DropTop();
				_record = null;
				topId = "";
				tree = _render(state, _platform);
			}
			else
			{
#if DEBUG
				DevOverlay.Show(this, "render", ex.Message, "render");
#endif
				throw;
			}
		}

		var evalUs = TakeUs(clock);
		if (_record is null)
			CompiledScreens.Store();
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
			StartupMarks.ReportUs("tree-build", evalUs);
			StartupMarks.ReportUs("tree-diff", diffUs);
			StartupMarks.ReportUs("view-create", reconcileUs);
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

		StartupMarks.FlushDrawables();
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
		if (_restyle)
		{
			_restyle = false;
			_reconciler.Restyle();
		}

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
		if (!_startupCost)
		{
			_startupCost = true;
			StartupMarks.Note("paint", paintUs / 1000);
			StartupMarks.Note("content", contentUs / 1000);
		}
		LastTitle = TitleOf(tree);
#if DEBUG
		Log.Info(Tag, "reload-texts " + VisibleTexts(tree));
#endif
		var titleUs = TakeUs(clock);
		if (_host is not null && _reconciler.Root is not null)
			_host.RestoreFocus(_reconciler.Root);
#if NATIVEPOC_NAV
		NavFocus.Apply(mounted);
#endif
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

#if DEBUG
	static string VisibleTexts(Element element)
	{
		var builder = new StringBuilder();
		AppendVisible(element, builder, 0);
		if (builder.Length > 400)
			builder.Length = 400;
		return builder.ToString();
	}

	static void AppendVisible(Element element, StringBuilder builder, int depth)
	{
		if (builder.Length >= 400 || depth > 8)
			return;
		switch (element)
		{
			case Column column:
				var count = column.Children.Count;
				if (count > 8)
					count = 8;
				for (var index = 0; index < count; index++)
					AppendVisible(column.Children[index], builder, depth + 1);
				return;
			case ScrollView scroll:
				AppendVisible(scroll.Child, builder, depth + 1);
				return;
			case ListView list:
				var rows = list.Rows.Count;
				if (rows > 3)
					rows = 3;
				for (var index = 0; index < rows; index++)
					AppendVisible(list.Rows[index], builder, depth + 1);
				return;
			case Text text:
				if (builder.Length > 0)
					builder.Append('|');
				builder.Append(text.Value);
				if (text.Style is { } style && style.FontSize != 0)
					builder.Append('/').Append(style.FontSize.ToString(CultureInfo.InvariantCulture));
				return;
			case Button button:
				if (builder.Length > 0)
					builder.Append('|');
				builder.Append(button.Title);
				return;
			case TextField:
			case TextInput:
			case Switch:
			case Image:
			case HostView:
				return;
			default:
				throw Unknown(element);
		}
	}
#endif

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

