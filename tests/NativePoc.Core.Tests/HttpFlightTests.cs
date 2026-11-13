namespace NativePoc.Tests;

public sealed class HttpFlightTests
{
	[Fact]
	public void Two_screens_cancel_independently()
	{
		var stories = HttpFlight.Open("stories");
		var visit = HttpFlight.Open("visit");
		HttpFlight.Attach("stories", stories);
		HttpFlight.Attach("visit", visit);
		Assert.True(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.Live("visit"));

		Assert.True(HttpFlight.Cancel("stories"));
		Assert.True(HttpFlight.WasCancelled("stories", stories));
		Assert.False(HttpFlight.WasCancelled("visit", visit));
		Assert.False(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.Live("visit"));
		Assert.False(HttpFlight.Cancel("stories"));

		var again = HttpFlight.Open("stories");
		HttpFlight.Attach("stories", again);
		Assert.True(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.WasCancelled("stories", stories));
		Assert.False(HttpFlight.WasCancelled("stories", again));
		Assert.True(HttpFlight.Live("visit"));

		Assert.True(HttpFlight.Cancel("visit"));
		Assert.True(HttpFlight.WasCancelled("visit", visit));
		Assert.False(HttpFlight.Live("visit"));
		Assert.True(HttpFlight.Live("stories"));
	}
}
