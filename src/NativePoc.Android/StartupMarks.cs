using System.Globalization;
using System.Runtime.CompilerServices;
using Android.OS;
using Android.Views;

namespace NativePoc;

static class StartupMarks
{
	static long _process;
	static bool _activity;
	static bool _present;
	static bool _armed;
	static Action? _afterPresent;
#if NATIVEPOC_STARTUP_MARKS
	static long _managed;
	static bool _render;
	static long _managedTick;
#endif

	public static bool Presented => _present;

	public static void AfterPresent(Action flush) => _afterPresent = flush;

#pragma warning disable CA2255
	[ModuleInitializer]
	internal static void NoteManaged()
	{
		// TickCount64 only. Android.OS.Process here runs before the JNI runtime
		// exists, and a failed type initializer stays failed for the process.
#if NATIVEPOC_STARTUP_MARKS
		_managedTick = System.Environment.TickCount64;
#endif
	}
#pragma warning restore CA2255

	public static void Activity()
	{
		if (_activity)
			return;
		_activity = true;
		_process = Process.StartElapsedRealtime;
#if NATIVEPOC_STARTUP_MARKS
		var since = SystemClock.ElapsedRealtime() - _process;
		var managed = _managedTick == 0 ? since : Math.Max(0, since - (System.Environment.TickCount64 - _managedTick));
		_managed = managed;
		LogActivity(managed, since);
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	static void LogActivity(long managed, long since)
	{
#if NATIVEPOC_STARTUP_MARKS
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=managed-main since-process-ms=" + managed.ToString(CultureInfo.InvariantCulture));
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=activity since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
		PhaseAt("process", _process);
		PhaseAt("managed", _process + managed);
		PhaseAt("runtime", SystemClock.ElapsedRealtime());
		LogGc("activity");
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void Phase(string name)
	{
#if NATIVEPOC_STARTUP_MARKS
		if (_process == 0)
			_process = Process.StartElapsedRealtime;
		PhaseAt(name, SystemClock.ElapsedRealtime());
#endif
	}

#if NATIVEPOC_STARTUP_MARKS
	static void PhaseAt(string name, long t)
	{
		if (_process == 0)
			return;
		var since = t - _process;
		if (since < 0)
			since = 0;
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup clock=elapsed phase=" + name
			+ " t=" + t.ToString(CultureInfo.InvariantCulture)
			+ " since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
	}
#endif

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void LogGc(string name)
	{
#if NATIVEPOC_STARTUP_MARKS
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup gc at=" + name
			+ " gen0=" + GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture)
			+ " gen1=" + GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture)
			+ " gen2=" + GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture));
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void Note(string name, long ms)
	{
#if NATIVEPOC_STARTUP_MARKS
		if (_process == 0)
			return;
		var since = SystemClock.ElapsedRealtime() - _process;
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup phase=" + name
			+ " ms=" + ms.ToString(CultureInfo.InvariantCulture)
			+ " since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void Frame(int binds, long textUs, long imageUs)
	{
#if NATIVEPOC_STARTUP_MARKS
		Phase("flush");
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup frame binds=" + binds.ToString(CultureInfo.InvariantCulture)
			+ " text-us=" + textUs.ToString(CultureInfo.InvariantCulture)
			+ " image-us=" + imageUs.ToString(CultureInfo.InvariantCulture));
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void Render()
	{
#if NATIVEPOC_STARTUP_MARKS
		if (_render || _process == 0)
			return;
		_render = true;
		var now = SystemClock.ElapsedRealtime();
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=first-render since-process-ms=" + (now - _process).ToString(CultureInfo.InvariantCulture));
		PhaseAt("render", now);
#endif
	}

	public static void Arm(Activity activity)
	{
		if (_armed)
			return;
		var decor = activity.Window?.DecorView;
		if (decor?.ViewTreeObserver is not { } observer)
			return;
		_armed = true;
		observer.AddOnPreDrawListener(new PresentMark(decor));
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	public static void Returned()
	{
#if NATIVEPOC_STARTUP_MARKS
		if (_process == 0)
			return;
		var since = SystemClock.ElapsedRealtime() - _process;
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=activity-return since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
#endif
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	static void Mark(string message)
	{
#if NATIVEPOC_STARTUP_MARKS
		Android.Util.Log.Info(MainActivity.Tag, message);
#endif
	}

	sealed class PresentMark : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
	{
		readonly View _view;
		bool _done;

		public PresentMark(View view) => _view = view;

		public bool OnPreDraw()
		{
			if (_done)
				return true;
			_done = true;
			_view.ViewTreeObserver?.RemoveOnPreDrawListener(this);
			if (_present || _process == 0)
				return true;
			_present = true;
#if NATIVEPOC_STARTUP_MARKS
			var now = SystemClock.ElapsedRealtime();
			Mark("startup mark=first-present since-process-ms=" + (now - _process).ToString(CultureInfo.InvariantCulture));
			PhaseAt("draw", now);
			LogGc("present");
#endif
			if (MainActivity.Current is Activity activity)
			{
				try
				{
					activity.ReportFullyDrawn();
#if NATIVEPOC_STARTUP_MARKS
					var drawn = SystemClock.ElapsedRealtime() - _process;
					Mark("startup mark=report-fully-drawn since-process-ms=" + drawn.ToString(CultureInfo.InvariantCulture));
#endif
				}
				catch (Exception)
				{
					Mark("startup report-fully-drawn failed");
				}
			}
			else
				Mark("startup report-fully-drawn skipped");
			var queue = Looper.MainLooper?.Queue;
			if (queue is not null)
				queue.AddIdleHandler(new IdleFlush(_afterPresent));
			else
			{
				LocaleRuntime.AfterLaunch();
				_afterPresent?.Invoke();
			}

			return true;
		}
	}

	// ActivityThread's Idler reports the launch. The system server records that
	// a few milliseconds after the client idle handler returns, so work on the
	// very next frame can still land inside TotalTime. Skip one frame, then
	// warm and flush.
	sealed class IdleFlush : Java.Lang.Object, MessageQueue.IIdleHandler
	{
		readonly Action? _flush;

		public IdleFlush(Action? flush) => _flush = flush;

		public bool QueueIdle()
		{
			Phase("idle");
			var choreographer = Choreographer.Instance;
			if (choreographer is not null)
				choreographer.PostFrameCallback(new AfterReported(_flush, false));
			else
			{
				LocaleRuntime.AfterLaunch();
				_flush?.Invoke();
			}

			return false;
		}
	}

	sealed class AfterReported : Java.Lang.Object, Choreographer.IFrameCallback
	{
		readonly Action? _flush;
		readonly bool _run;

		public AfterReported(Action? flush, bool run)
		{
			_flush = flush;
			_run = run;
		}

		public void DoFrame(long frameTimeNanos)
		{
			var choreographer = Choreographer.Instance;
			if (!_run && choreographer is not null)
			{
				choreographer.PostFrameCallback(new AfterReported(_flush, true));
				return;
			}

			LocaleRuntime.AfterLaunch();
			_flush?.Invoke();
		}
	}
}
