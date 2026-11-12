using System.Globalization;
using Android.Provider;
using Android.Views;

namespace NativePoc;

static class AnimatedPlayer
{
	sealed class Run
	{
		public View View = null!;
		public string Key = "";
		public int From;
		public int To;
		public int Duration;
		public bool Spring;
		public long StartNanos;
		public long PreviousNanos;
		public int Frames;
		public int Dropped;
		public long MaxGap;
	}

	sealed class Frame : Java.Lang.Object, Choreographer.IFrameCallback
	{
		public void DoFrame(long frameTimeNanos) => Step(frameTimeNanos);
	}

	static readonly Dictionary<string, View> Views = new(StringComparer.Ordinal);
	static readonly List<Run> Live = new();
	static Frame? _clock;

	public static void Bind(string key, View view)
	{
		if (key.Length == 0)
			return;
		Views[key] = view;
	}

	public static void Unbind(View view)
	{
		string? found = null;
		foreach (var pair in Views)
		{
			if (!ReferenceEquals(pair.Value, view))
				continue;
			found = pair.Key;
			break;
		}

		if (found is not null)
			Views.Remove(found);
		Stop(view);
	}

	public static void Play(Activity activity, string key, int from, int to, int basis, bool spring)
	{
		if (!Views.TryGetValue(key, out var view) || view.Handle == IntPtr.Zero)
		{
			Android.Util.Log.Info(MainActivity.Tag, "animated miss key=" + key);
			return;
		}

		var animator = 1f;
		var transition = 1f;
		var resolver = activity.ContentResolver;
		if (resolver is not null)
		{
			animator = Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f);
			transition = Settings.Global.GetFloat(resolver, Settings.Global.TransitionAnimationScale, 1f);
		}

		Stop(view);
		var duration = AnimatedMath.Duration(basis, animator, transition);
		var instant = duration <= 0;
		Android.Util.Log.Info(
			MainActivity.Tag,
			"animated play key=" + key
			+ " curve=" + (spring ? "spring" : "timing")
			+ " from=" + from.ToString(CultureInfo.InvariantCulture)
			+ " to=" + to.ToString(CultureInfo.InvariantCulture)
			+ " basis=" + basis.ToString(CultureInfo.InvariantCulture)
			+ " ms=" + duration.ToString(CultureInfo.InvariantCulture)
			+ " instant=" + (instant ? "1" : "0"));
		if (instant)
		{
			Apply(view, to);
			return;
		}

		Apply(view, from);
		Live.Add(new Run
		{
			View = view,
			Key = key,
			From = from,
			To = to,
			Duration = duration,
			Spring = spring,
		});
		Arm();
	}

	static void Arm()
	{
		if (Live.Count == 0)
			return;
		var choreographer = Choreographer.Instance;
		if (choreographer is null)
			return;
		_clock ??= new Frame();
		choreographer.PostFrameCallback(_clock);
	}

	static void Step(long frameTimeNanos)
	{
		for (var index = Live.Count - 1; index >= 0; index--)
		{
			var run = Live[index];
			if (run.View.Handle == IntPtr.Zero)
			{
				Live.RemoveAt(index);
				continue;
			}

			if (run.StartNanos == 0)
			{
				run.StartNanos = frameTimeNanos;
				run.PreviousNanos = frameTimeNanos;
				run.Frames = 1;
				Apply(run.View, run.From);
				continue;
			}

			var gapMs = (frameTimeNanos - run.PreviousNanos) / 1_000_000L;
			if (gapMs > run.MaxGap)
				run.MaxGap = gapMs;
			if (gapMs > 50)
				run.Dropped++;
			run.PreviousNanos = frameTimeNanos;
			run.Frames++;
			var elapsed = (int)((frameTimeNanos - run.StartNanos) / 1_000_000L);
			var sample = AnimatedMath.Sample(run.From, run.To, elapsed, run.Duration, run.Spring);
			Apply(run.View, sample);
			if (elapsed < run.Duration)
				continue;
			Apply(run.View, run.To);
			var span = elapsed.ToString(CultureInfo.InvariantCulture);
			Android.Util.Log.Info(
				MainActivity.Tag,
				"animated end key=" + run.Key
				+ " frames=" + run.Frames.ToString(CultureInfo.InvariantCulture)
				+ " span=" + span
				+ " max-gap=" + run.MaxGap.ToString(CultureInfo.InvariantCulture)
				+ " dropped=" + run.Dropped.ToString(CultureInfo.InvariantCulture));
			Live.RemoveAt(index);
		}

		if (Live.Count > 0)
			Arm();
		else
			_clock = null;
	}

	static void Stop(View view)
	{
		for (var index = Live.Count - 1; index >= 0; index--)
		{
			if (!ReferenceEquals(Live[index].View, view))
				continue;
			Apply(view, 100);
			Live.RemoveAt(index);
		}
	}

	static void Apply(View view, int percent)
	{
		var scale = percent / 100f;
		var width = view.Width;
		var height = view.Height;
		if (width > 0)
			view.PivotX = width / 2f;
		if (height > 0)
			view.PivotY = height / 2f;
		view.ScaleX = scale;
		view.ScaleY = scale;
	}
}
