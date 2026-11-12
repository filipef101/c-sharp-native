using System.Globalization;

namespace NativePoc;

/* A screen calls Timing or Spring. Nothing is scheduled until that call.
   Duration 0, or either system scale at 0, applies the end value and
   does not ask for a frame. */
public static class Animated
{
	public static void Timing(IPlatform platform, string key, int from, int to, int duration) =>
		Play(platform, key, from, to, duration, "Timing");

	public static void Spring(IPlatform platform, string key, int from, int to, int duration) =>
		Play(platform, key, from, to, duration, "Spring");

	static void Play(IPlatform platform, string key, int from, int to, int duration, string curve)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("animated", curve, [
			key,
			from.ToString(CultureInfo.InvariantCulture),
			to.ToString(CultureInfo.InvariantCulture),
			duration.ToString(CultureInfo.InvariantCulture),
		]);
	}
}

internal static class AnimatedMath
{
	public static int Duration(int basis, float animatorScale, float transitionScale)
	{
		if (MotionScale.Instant(animatorScale, transitionScale) || basis <= 0)
			return 0;
		var scale = animatorScale < transitionScale ? animatorScale : transitionScale;
		var ms = (int)Math.Round(basis * scale);
		return ms < 1 ? 1 : ms;
	}

	public static int Sample(int from, int to, int elapsed, int duration, bool spring)
	{
		if (duration <= 0 || elapsed >= duration)
			return to;
		if (elapsed <= 0)
			return from;
		var t = elapsed / (float)duration;
		var eased = spring ? Spring(t) : Timing(t);
		return from + (int)Math.Round((to - from) * eased);
	}

	public static float Timing(float t)
	{
		if (t <= 0f)
			return 0f;
		if (t >= 1f)
			return 1f;
		var remaining = 1f - t;
		return 1f - remaining * remaining * remaining;
	}

	public static float Spring(float t)
	{
		if (t <= 0f)
			return 0f;
		if (t >= 1f)
			return 1f;
		return 1f - MathF.Exp(-6f * t) * MathF.Cos(MathF.PI * 1.5f * t);
	}
}
