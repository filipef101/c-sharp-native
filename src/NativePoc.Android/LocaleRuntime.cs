using Android.OS;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class LocaleRuntime
{
	static int _applied = -1;
	static bool _warmed;
	static bool _arming;
	static bool _deferred;
	static Activity? _deferredActivity;
	static View? _deferredRoot;
	static Action? _pending;

	public static void Apply(Activity activity)
	{
		var extra = activity.Intent?.GetStringExtra("nativepoc_locale");
		var tag = string.IsNullOrEmpty(extra) ? DeviceTag(activity) : extra;
		StringTable.Use(tag);
		Android.Util.Log.Info(MainActivity.Tag, "startup locale=" + tag + " rtl=" + (StringTable.RightToLeft ? "1" : "0"));
		ApplyDirection(activity);
	}

	public static void ApplyDirection(Activity activity)
	{
		var generation = Generation();
		if (_applied == generation)
			return;
		if (activity.Window?.DecorView is View decor)
			Commit(decor, generation);
		_applied = generation;
	}

	public static void ApplyDirection(View view)
	{
		var generation = Generation();
		if (_applied == generation)
			return;
		Commit(view, generation);
		_applied = generation;
	}

	public static void WhenWarmed(Action next)
	{
		if (_warmed)
			next();
		else
			_pending += next;
	}

	public static void WarmAfterFirstFrame(Activity activity, View? root)
	{
		if (_arming || _deferred)
			return;
		_deferred = true;
		_deferredActivity = activity;
		_deferredRoot = root;
	}

	public static void AfterLaunch()
	{
		if (_arming || !_deferred)
			return;
		_arming = true;
		_deferred = false;
		var activity = _deferredActivity;
		var root = _deferredRoot;
		_deferredActivity = null;
		_deferredRoot = null;
		if (activity is null)
		{
			Finish();
			return;
		}

		Resolve(activity, root);
	}

	static void Resolve(Activity activity, View? root)
	{
		StartupMarks.Phase("direction");
#if NATIVEPOC_STARTUP_MARKS
		var clock = System.Diagnostics.Stopwatch.StartNew();
#endif
		var generation = Generation();
		if (activity.Window?.DecorView is View decor)
			Commit(decor, generation);
#if NATIVEPOC_STARTUP_MARKS
		var decorUs = TakeUs(clock);
#endif
		if (root is not null)
			Commit(root, generation);
#if NATIVEPOC_STARTUP_MARKS
		var rootUs = TakeUs(clock);
#endif
		var probe = new TextView(activity)
		{
			Text = "Warm",
		};
		Commit(probe, generation);
		var width = View.MeasureSpec.MakeMeasureSpec(1080, MeasureSpecMode.AtMost);
		var height = View.MeasureSpec.MakeMeasureSpec(200, MeasureSpecMode.AtMost);
		probe.Measure(width, height);
#if NATIVEPOC_STARTUP_MARKS
		var textUs = TakeUs(clock);
		// The first frame already laid the attached root out. Measuring it again
		// walked the feed list on the main thread and held the launch Idler.
		LogDirection(decorUs, rootUs, textUs, 0);
#endif
		_applied = generation;
		StartupMarks.Phase("direction-done");
		Finish();
	}

	static void Finish()
	{
		if (_warmed)
			return;
		StartupMarks.Phase("warm");
		_warmed = true;
		var pending = _pending;
		_pending = null;
		pending?.Invoke();
	}

	[System.Diagnostics.Conditional("NATIVEPOC_STARTUP_MARKS")]
	static void LogDirection(long decorUs, long rootUs, long textUs, long layoutUs)
	{
#if NATIVEPOC_STARTUP_MARKS
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup direction decor=" + decorUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " root=" + rootUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " text=" + textUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " layout=" + layoutUs.ToString(System.Globalization.CultureInfo.InvariantCulture));
#endif
	}

	static void Commit(View view, int generation)
	{
		var next = generation == 1 ? LayoutDirection.Rtl : LayoutDirection.Ltr;
		var text = generation == 1 ? TextDirection.Rtl : TextDirection.Ltr;
		view.LayoutDirection = next;
		_ = view.LayoutDirection;
		view.TextDirection = text;
		_ = view.TextDirection;
	}

	static int Generation() => StringTable.RightToLeft ? 1 : 0;

	static long TakeUs(System.Diagnostics.Stopwatch clock)
	{
		var micros = clock.ElapsedTicks * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
		clock.Restart();
		return micros;
	}

	static string DeviceTag(Activity activity)
	{
		var locales = activity.Resources?.Configuration?.Locales;
		if (locales is not null && !locales.IsEmpty)
		{
			var tag = locales.Get(0)?.ToLanguageTag();
			if (!string.IsNullOrEmpty(tag))
				return tag;
		}

		return "en";
	}

}
