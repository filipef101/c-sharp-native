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

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		ActionLog.OnScript = (micros, steps, stop) =>
		{
			if (stop is not null)
				Log.Info(Tag, stop);
			Log.Info(Tag, "action-us=" + micros.ToString(CultureInfo.InvariantCulture) + " steps=" + steps.ToString(CultureInfo.InvariantCulture));
		};
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
		var requestedRoot = Intent?.GetStringExtra("nativepoc_root");
		var wantsRoot = !string.IsNullOrEmpty(requestedRoot) && !string.Equals(requestedRoot, "counter", StringComparison.Ordinal);
		var navStart = SystemClock.ElapsedRealtime();
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
		RenderNow();
		if (string.Equals(Intent?.GetStringExtra("nativepoc_keyed_move"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.GreetingFirst = true;
			RenderNow();
		}
#if DEBUG
		HotReloadServer.Start(this);
#endif
		if (!Bench && !wantsRoot)
		{
			_reconciler?.Root?.Post(() =>
			{
				var started = SystemClock.ElapsedRealtime();
				NavCatalog.InstallRest();
				StartupMarks.Note("catalog", SystemClock.ElapsedRealtime() - started);
			});
		}
		StartupMarks.Returned();
		ScheduleLoopBench();
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
	}

	protected override void OnResume()
	{
		base.OnResume();
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
	}

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
					RenderNow();
				}
				finally
				{
					_inRender = false;
				}
			}
			while (_renderAgain);
		}

		if (Looper.MainLooper?.IsCurrentThread == true)
			Run();
		else
			WaitForUi(Run);
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
		if (_reconciler is null || _platform is null)
			return;
		var previousRoot = _reconciler.Root;
		var state = AppSession.Shared.State;
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

		if (_record is { } bound)
			RecordSession.Publish(bound);
		Element tree;
		var evalStart = SystemClock.ElapsedRealtime();
		try
		{
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

		var evalMs = SystemClock.ElapsedRealtime() - evalStart;
		var mountStart = SystemClock.ElapsedRealtime();
		try
		{
			_reconciler.Render(tree);
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
		var mountMs = SystemClock.ElapsedRealtime() - mountStart;
		StartupMarks.Note("eval", evalMs);
		StartupMarks.Note("mount", mountMs);
		var rows = 0;
		var built = 0;
		Describe(tree, ref rows, ref built);
		var views = _reconciler.Root is View root ? CountViews(root) : 0;
		Log.Info(
			Tag,
			"startup window rows=" + rows.ToString(CultureInfo.InvariantCulture)
			+ " built=" + built.ToString(CultureInfo.InvariantCulture)
			+ " views=" + views.ToString(CultureInfo.InvariantCulture));
		StartupMarks.Render();
		if (_reconciler.Root is not View mounted)
			throw new InvalidOperationException("Screen did not mount a root view.");
		ThemeRuntime.Paint(this, mounted);
		LocaleRuntime.ApplyDirection(mounted);
		LocaleRuntime.ApplyDirection(this);
		if (!ReferenceEquals(previousRoot, mounted))
		{
			SetContentView(mounted);
			Log.Info(Tag, "set-content-view");
		}

		StartupMarks.Arm(this);
		LastTitle = TitleOf(tree);
		if (_host is not null && _reconciler.Root is not null)
			_host.RestoreFocus(_reconciler.Root);
#if DEBUG
		LogTree();
#endif
		Log.Info(Tag, "rendered count=" + state.Count + " name-length=" + state.Name.Length + " title=" + LastTitle);
#if DEBUG
		StateStore.Save(this);
#endif
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
