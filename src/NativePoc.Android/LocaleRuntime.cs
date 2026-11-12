using Android.OS;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class LocaleRuntime
{
	static int _applied = -1;
	static bool _warmed;
	static bool _arming;
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
		if (_arming)
			return;
		_arming = true;
		_ = Looper.MainLooper?.IsCurrentThread;
		var choreographer = Choreographer.Instance;
		if (root is null || choreographer is null)
		{
			Resolve(activity, root);
			return;
		}

		choreographer.PostFrameCallback(new DirectionWarm(activity, root));
	}

	static void Resolve(Activity activity, View? root)
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var generation = Generation();
		if (activity.Window?.DecorView is View decor)
			Commit(decor, generation);
		var decorUs = TakeUs(clock);
		if (root is not null)
			Commit(root, generation);
		var rootUs = TakeUs(clock);
		var probe = new TextView(activity)
		{
			Text = "Warm",
		};
		Commit(probe, generation);
		var width = View.MeasureSpec.MakeMeasureSpec(1080, MeasureSpecMode.AtMost);
		var height = View.MeasureSpec.MakeMeasureSpec(200, MeasureSpecMode.AtMost);
		probe.Measure(width, height);
		var textUs = TakeUs(clock);
		if (root is { Width: > 0, Height: > 0 })
		{
			root.Measure(
				View.MeasureSpec.MakeMeasureSpec(root.Width, MeasureSpecMode.Exactly),
				View.MeasureSpec.MakeMeasureSpec(root.Height, MeasureSpecMode.Exactly));
			root.Layout(root.Left, root.Top, root.Left + root.Width, root.Top + root.Height);
		}

		var layoutUs = TakeUs(clock);
		_applied = generation;
		Android.Util.Log.Info(
			MainActivity.Tag,
			"startup direction decor=" + decorUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " root=" + rootUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " text=" + textUs.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " layout=" + layoutUs.ToString(System.Globalization.CultureInfo.InvariantCulture));
		var choreographer = Choreographer.Instance;
		if (root is null || choreographer is null)
		{
			Finish();
			return;
		}

		choreographer.PostFrameCallback(new AfterDirection(root));
	}

	static void Finish()
	{
		if (_warmed)
			return;
		_warmed = true;
		var pending = _pending;
		_pending = null;
		pending?.Invoke();
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

	sealed class DirectionWarm : Java.Lang.Object, Choreographer.IFrameCallback
	{
		readonly Activity _activity;
		readonly View _root;

		public DirectionWarm(Activity activity, View root)
		{
			_activity = activity;
			_root = root;
		}

		public void DoFrame(long frameTimeNanos)
		{
			_root.Post(() => Resolve(_activity, _root));
		}
	}

	sealed class AfterDirection : Java.Lang.Object, Choreographer.IFrameCallback
	{
		readonly View _root;

		public AfterDirection(View root) => _root = root;

		public void DoFrame(long frameTimeNanos)
		{
			_root.Post(Finish);
		}
	}
}
