using System.Globalization;
using Android.Views;
using Android.Views.Animations;

namespace NativePoc;

static class MotionPlayer
{
	sealed class Run
	{
		public View View = null!;
		public int FromOpacity;
		public int ToOpacity;
		public float FromX;
		public float ToX;
		public float FromY;
		public float ToY;
		public float FromScale;
		public float ToScale;
		public int Elapsed;
		public int Duration;
		public bool Stopped;
		public bool Property;
	}

	readonly record struct Named(View View, Motion Motion, float Density);

	static readonly List<Run> _live = new();
	static readonly Dictionary<string, Named> _named = new();
	static bool _manual;

	public static void Remember(string key, View view, Motion motion, float density)
	{
		if (key.Length == 0)
			return;
		_named[key] = new Named(view, motion, density);
	}

	public static void Replay(string key)
	{
		if (!_named.TryGetValue(key, out var named) || named.View.Handle == IntPtr.Zero)
		{
			Android.Util.Log.Info(MainActivity.Tag, "motion miss key=" + key);
			return;
		}

		Play(named.View, named.Motion, named.Density, key, "handler");
	}

	public static void Play(View view, Motion motion, float density, string key = "", string source = "mount")
	{
		var current = Find(view);
		var fromOpacity = current is null ? motion.FromOpacity : OpacityOf(view);
		var fromX = current is null ? motion.FromX * density : view.TranslationX;
		var fromY = current is null ? motion.FromY * density : view.TranslationY;
		var fromScale = current is null ? motion.FromScale / 100f : view.ScaleX;
		Stop(view);
		if (motion.Duration <= 0 || MotionCodec.IsRest(motion))
		{
			Apply(view, motion.Opacity, motion.X * density, motion.Y * density, motion.Scale / 100f);
			return;
		}

		var run = new Run
		{
			View = view,
			FromOpacity = fromOpacity,
			ToOpacity = motion.Opacity,
			FromX = fromX,
			ToX = motion.X * density,
			FromY = fromY,
			ToY = motion.Y * density,
			FromScale = fromScale,
			ToScale = motion.Scale / 100f,
			Duration = motion.Duration,
		};
		Apply(view, run.FromOpacity, run.FromX, run.FromY, run.FromScale);
		_live.Add(run);
		Android.Util.Log.Info(
			MainActivity.Tag,
			"motion play key=" + key
			+ " duration=" + motion.Duration.ToString(CultureInfo.InvariantCulture)
			+ " easing=" + motion.Easing.ToString(CultureInfo.InvariantCulture)
			+ " source=" + source);
		if (_manual)
			return;
		run.Property = true;
		Animator(view)
			.Alpha(Math.Clamp(run.ToOpacity, 0, 100) / 100f)
			.TranslationX(run.ToX)
			.TranslationY(run.ToY)
			.ScaleX(run.ToScale)
			.ScaleY(run.ToScale)
			.SetDuration(motion.Duration)
			.SetInterpolator(Interpolator(motion.Easing))
			.WithEndAction(new End(() =>
			{
				if (!run.Stopped)
					Finish(run);
			}))
			.Start();
	}

	sealed class End : Java.Lang.Object, Java.Lang.IRunnable
	{
		readonly Action _done;

		public End(Action done) => _done = done;

		public void Run() => _done();
	}

	static ViewPropertyAnimator Animator(View view) =>
		view.Animate() ?? throw new InvalidOperationException("View animator was missing.");

	static Android.Animation.ITimeInterpolator Interpolator(int easing) => easing switch
	{
		0 => new LinearInterpolator(),
		1 => new DecelerateInterpolator(),
		2 => new AccelerateInterpolator(),
		3 => new AccelerateDecelerateInterpolator(),
		_ => new LinearInterpolator(),
	};

	public static int Advance(int quantumMs)
	{
		_manual = true;
		var stepped = 0;
		for (var index = _live.Count - 1; index >= 0; index--)
		{
			var run = _live[index];
			if (run.View.Handle == IntPtr.Zero)
			{
				_live.RemoveAt(index);
				continue;
			}

			if (run.Property && run.View.Handle != IntPtr.Zero)
				Animator(run.View).Cancel();
			run.Property = false;
			run.Elapsed = Math.Min(run.Duration, run.Elapsed + quantumMs);
			Apply(
				run.View,
				MotionMath.Lerp(run.FromOpacity, run.ToOpacity, run.Elapsed, run.Duration),
				Lerp(run.FromX, run.ToX, run.Elapsed, run.Duration),
				Lerp(run.FromY, run.ToY, run.Elapsed, run.Duration),
				Lerp(run.FromScale, run.ToScale, run.Elapsed, run.Duration));
			Android.Util.Log.Info(
				MainActivity.Tag,
				"motion step elapsed=" + run.Elapsed.ToString(CultureInfo.InvariantCulture)
				+ " opacity=" + MotionMath.Lerp(run.FromOpacity, run.ToOpacity, run.Elapsed, run.Duration).ToString(CultureInfo.InvariantCulture));
			stepped++;
			if (run.Elapsed >= run.Duration)
				_live.RemoveAt(index);
		}

		return stepped;
	}

	public static void Stop(View view)
	{
		for (var index = _live.Count - 1; index >= 0; index--)
		{
			if (!ReferenceEquals(_live[index].View, view))
				continue;
			var run = _live[index];
			run.Stopped = true;
			if (run.Property && run.View.Handle != IntPtr.Zero)
				Animator(run.View).Cancel();
			run.Property = false;
			_live.RemoveAt(index);
		}
	}

	static Run? Find(View view)
	{
		foreach (var run in _live)
		{
			if (ReferenceEquals(run.View, view))
				return run;
		}

		return null;
	}

	static void Finish(Run run)
	{
		run.Stopped = true;
		_live.Remove(run);
	}

	static int OpacityOf(View view) => (int)Math.Round(view.Alpha * 100f);

	static void Apply(View view, int opacity, float x, float y, float scale)
	{
		if (view.Handle == IntPtr.Zero)
			return;
		view.Alpha = Math.Clamp(opacity, 0, 100) / 100f;
		view.TranslationX = x;
		view.TranslationY = y;
		view.ScaleX = scale;
		view.ScaleY = scale;
	}

	static float Lerp(float from, float to, int elapsed, int duration)
	{
		if (duration <= 0 || elapsed >= duration)
			return to;
		if (elapsed <= 0)
			return from;
		return from + (to - from) * elapsed / duration;
	}
}
