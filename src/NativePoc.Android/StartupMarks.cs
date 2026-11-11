using System.Globalization;
using System.Runtime.CompilerServices;
using Android.OS;
using Android.Views;

namespace NativePoc;

static class StartupMarks
{
	static long _process;
	static long _managed;
	static bool _activity;
	static bool _render;
	static bool _present;
	static bool _armed;

	static long _managedTick;

#pragma warning disable CA2255
	[ModuleInitializer]
	internal static void NoteManaged()
	{
		_managedTick = System.Environment.TickCount64;
	}
#pragma warning restore CA2255

	public static void Activity()
	{
		if (_activity)
			return;
		_activity = true;
		_process = Android.OS.Process.StartElapsedRealtime;
		var since = SystemClock.ElapsedRealtime() - _process;
		var managed = _managedTick == 0 ? since : Math.Max(0, since - (System.Environment.TickCount64 - _managedTick));
		_managed = managed;
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=managed-main since-process-ms=" + managed.ToString(CultureInfo.InvariantCulture));
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=activity since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
	}

	public static void Note(string name, long ms)
	{
		if (_process == 0)
			return;
		var since = SystemClock.ElapsedRealtime() - _process;
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup phase=" + name
			+ " ms=" + ms.ToString(CultureInfo.InvariantCulture)
			+ " since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
	}

	public static void Render()
	{
		if (_render || _process == 0)
			return;
		_render = true;
		var since = SystemClock.ElapsedRealtime() - _process;
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=first-render since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
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

	public static void Returned()
	{
		if (_process == 0)
			return;
		var since = SystemClock.ElapsedRealtime() - _process;
		Android.Util.Log.Info(MainActivity.Tag, "startup mark=activity-return since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
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
			var since = SystemClock.ElapsedRealtime() - _process;
			Android.Util.Log.Info(MainActivity.Tag, "startup mark=first-present since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
			return true;
		}
	}
}
