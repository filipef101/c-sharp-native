namespace NativePoc;

/* Swipe-back and Animated decisions the hosts share. Android maps the
   swipe onto a finger drag and predictive back. A UIKit host maps the
   same decision onto UINavigationController's interactive pop, and maps
   Animated onto UIView animations driven by CADisplayLink. Scale 0, on
   either system scale, applies the end value and asks for no frame.
   net10.0-ios is not compiled here. */
internal static class MotionContract
{
	public const float CommitFraction = 0.33f;

	public const float FlingFraction = 0.12f;

	public const float FlingVelocity = 900f;

	public static bool Commits(float fraction, float velocityX) =>
		fraction >= CommitFraction || (fraction >= FlingFraction && velocityX > FlingVelocity);

	public static int SettleMs(float fraction, bool commit, float animatorScale, float transitionScale)
	{
		if (MotionScale.Instant(animatorScale, transitionScale))
			return 0;
		var span = commit ? 1f - fraction : fraction;
		if (span < 0.2f)
			span = 0.2f;
		return (int)Math.Round(MotionScale.DurationMs(animatorScale, transitionScale) * span);
	}

	public static int TransitionMs(float animatorScale, float transitionScale) =>
		MotionScale.DurationMs(animatorScale, transitionScale);

	public static int AnimatedMs(int basis, float animatorScale, float transitionScale) =>
		AnimatedMath.Duration(basis, animatorScale, transitionScale);

	public static int AnimatedValue(int from, int to, int elapsed, int duration, bool spring) =>
		AnimatedMath.Sample(from, to, elapsed, duration, spring);
}
