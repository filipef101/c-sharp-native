namespace NativePoc.Tests;

public sealed class AnimatedMathTests
{
	[Fact]
	public void Scale_zero_and_a_zero_duration_are_instant()
	{
		Assert.Equal(0, AnimatedMath.Duration(220, 0f, 1f));
		Assert.Equal(0, AnimatedMath.Duration(220, 1f, 0f));
		Assert.Equal(0, AnimatedMath.Duration(0, 1f, 1f));
		Assert.Equal(100, AnimatedMath.Sample(86, 100, 0, 0, spring: true));
		Assert.Equal(100, AnimatedMath.Sample(86, 100, 0, 0, spring: false));
	}

	[Fact]
	public void Animator_scale_shortens_the_basis_and_the_smaller_scale_wins()
	{
		Assert.Equal(220, AnimatedMath.Duration(220, 1f, 1f));
		Assert.Equal(110, AnimatedMath.Duration(220, 0.5f, 1f));
		Assert.Equal(110, AnimatedMath.Duration(220, 1f, 0.5f));
		Assert.Equal(1, AnimatedMath.Duration(1, 0.1f, 1f));
	}

	[Fact]
	public void Timing_eases_out_and_spring_settles_after_an_overshoot()
	{
		Assert.Equal(0f, AnimatedMath.Timing(0f));
		Assert.Equal(1f, AnimatedMath.Timing(1f));
		Assert.Equal(0.875f, AnimatedMath.Timing(0.5f), 3);
		Assert.True(AnimatedMath.Timing(0.5f) > 0.5f);
		Assert.Equal(0f, AnimatedMath.Spring(0f));
		Assert.Equal(1f, AnimatedMath.Spring(1f));
		var peak = 0f;
		for (var step = 1; step < 20; step++)
			peak = MathF.Max(peak, AnimatedMath.Spring(step / 20f));
		Assert.True(peak > 1f);
		Assert.Equal(86, AnimatedMath.Sample(86, 100, 0, 220, spring: false));
		Assert.Equal(100, AnimatedMath.Sample(86, 100, 220, 220, spring: true));
		var mid = AnimatedMath.Sample(0, 100, 110, 220, spring: false);
		Assert.True(mid > 50);
	}
}
