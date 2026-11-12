namespace NativePoc.Tests;

public sealed class MotionContractTests
{
	[Fact]
	public void A_short_swipe_cancels_and_a_committed_swipe_pops()
	{
		var host = new HeadlessHost();
		host.Show(_ => new Text("Home") { Key = "heading", Style = new Style { FontSize = 32 } });
		host.Push("shelf");
		var depth = host.NavDepth;
		Assert.False(host.SwipeBack(0.10f, 0f));
		Assert.Equal(depth, host.NavDepth);
		Assert.Contains("nav-swipe cancel", host.Calls);
		Assert.True(host.SwipeBack(0.15f, 1000f));
		Assert.Equal(depth - 1, host.NavDepth);
		Assert.Contains("nav-swipe commit", host.Calls);
		host.Push("piece");
		Assert.True(host.SwipeBack(0.40f, 0f));
		Assert.False(host.SwipeBack(0.15f, 100f));
	}

	[Fact]
	public void Scale_zero_applies_the_end_value_and_asks_for_no_frame()
	{
		var host = new HeadlessHost();
		host.Show(_ => new Text("Keep") { Key = "keep" });
		var renders = host.Renders;
		var value = host.PlayAnimated("keep", 86, 100, 220, 0f, 1f, spring: true);
		Assert.Equal(100, value);
		Assert.Equal(renders, host.Renders);
		Assert.Contains("animated key=keep ms=0 value=100", host.Calls);
		Assert.Equal(0, MotionContract.AnimatedMs(220, 0f, 1f));
		Assert.Equal(0, MotionContract.SettleMs(0.40f, true, 0f, 1f));
		Assert.Equal(0, MotionContract.TransitionMs(1f, 0f));
		Assert.Equal(100, MotionContract.AnimatedValue(86, 100, 0, 0, spring: true));
	}
}
