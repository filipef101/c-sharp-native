using System.Globalization;
using Android.Animation;
using Android.Views;

namespace NativePoc;

static class MotionPlayer
{
	sealed class Run
	{
		public View View = null!;
		public ValueAnimator? Animator;
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
	}

	static readonly List<Run> _live = new();
	static bool _manual;

	public static void Play(View view, Motion motion, float density)
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
		if (_manual)
			return;
		var animator = ValueAnimator.OfFloat(0f, 1f)!;
		animator.SetDuration(motion.Duration);
		animator.Update += (_, _) =>
		{
			if (run.Stopped || view.Handle == IntPtr.Zero)
				return;
			var fraction = animator.AnimatedFraction;
			var elapsed = (int)(motion.Duration * fraction);
			run.Elapsed = elapsed;
			Apply(
				view,
				MotionMath.Lerp(run.FromOpacity, run.ToOpacity, elapsed, motion.Duration),
				Lerp(run.FromX, run.ToX, elapsed, motion.Duration),
				Lerp(run.FromY, run.ToY, elapsed, motion.Duration),
				Lerp(run.FromScale, run.ToScale, elapsed, motion.Duration));
		};
		animator.AnimationEnd += (_, _) =>
		{
			if (!run.Stopped)
				Finish(run);
		};
		run.Animator = animator;
		animator.Start();
	}

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

			run.Animator?.Cancel();
			run.Animator = null;
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
			run.Animator?.Cancel();
			run.Animator = null;
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
