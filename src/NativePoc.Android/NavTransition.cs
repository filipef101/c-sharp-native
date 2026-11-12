using System.Globalization;
using Android.Provider;
using Android.Transitions;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class NavTransition
{
	public static void Play(FrameLayout stage, View leaving, View arriving, bool push)
	{
		var animator = Read(stage.Context, Settings.Global.AnimatorDurationScale);
		var motion = Read(stage.Context, Settings.Global.TransitionAnimationScale);
		var instant = MotionScale.Instant(animator, motion);
		var ms = instant ? 0 : MotionScale.DurationMs(animator, motion);
		Log.Info(
			MainActivity.Tag,
			"nav-transition instant=" + (instant ? "1" : "0")
			+ " scale=" + animator.ToString("0.0##", CultureInfo.InvariantCulture)
			+ " motion=" + motion.ToString("0.0##", CultureInfo.InvariantCulture)
			+ " ms=" + ms.ToString(CultureInfo.InvariantCulture)
			+ " dir=" + (push ? "push" : "pop"));
		if (!instant)
		{
			var slide = new Slide(push ? GravityFlags.Right : GravityFlags.Left);
			slide.SetDuration(ms);
			TransitionManager.BeginDelayedTransition(stage, slide);
		}

		if (leaving.Handle != IntPtr.Zero && leaving.Parent == stage)
			stage.RemoveView(leaving);
		if (arriving.Parent is null)
			stage.AddView(arriving, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
	}

	static float Read(Android.Content.Context? context, string name)
	{
		if (context?.ContentResolver is not { } resolver)
			return 1f;
		return Settings.Global.GetFloat(resolver, name, 1f);
	}
}
