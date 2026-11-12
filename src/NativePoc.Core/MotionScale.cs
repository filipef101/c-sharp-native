namespace NativePoc;

/* Nav push and pop. The Android transition module reads the two system
   scales and calls Instant before it starts a Transition. */
public static class MotionScale
{
	public const int BasisMs = 180;

	public static bool Instant(float animatorScale, float transitionScale) =>
		animatorScale <= 0f || transitionScale <= 0f;

	public static int DurationMs(float animatorScale, float transitionScale)
	{
		if (Instant(animatorScale, transitionScale))
			return 0;
		var scale = animatorScale < transitionScale ? animatorScale : transitionScale;
		var ms = (int)Math.Round(BasisMs * scale);
		return ms < 1 ? 1 : ms;
	}
}
