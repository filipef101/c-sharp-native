namespace NativePoc;

/* Managed stand-in for the UIKit host. Interactive pop is
   UINavigationController, and a running animation is a UIView animation
   ticked by CADisplayLink. Both call MotionContract. This project is
   net10.0. net10.0-ios is not compiled here. */
public static class MotionMap
{
	public static bool PopCommits(float fraction, float velocity) =>
		MotionContract.Commits(fraction, velocity);

	public static int PopMs(float fraction, bool commit, float animatorScale, float transitionScale) =>
		MotionContract.SettleMs(fraction, commit, animatorScale, transitionScale);

	public static int AnimatedMs(int basis, float animatorScale, float transitionScale) =>
		MotionContract.AnimatedMs(basis, animatorScale, transitionScale);

	public static int ValueAtScaleZero(int from, int to) =>
		MotionContract.AnimatedValue(from, to, 0, MotionContract.AnimatedMs(220, 0f, 1f), spring: true);
}
