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

	[Fact]
	public void An_image_join_dies_with_its_screen()
	{
		var stories = HttpFlight.Open("stories-image");
		var image = HttpFlight.Join("stories-image");
		Assert.Equal(stories, image);
		var visit = HttpFlight.Join("visit-image");
		Assert.False(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));

		Assert.True(HttpFlight.Cancel("stories-image"));
		Assert.True(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));
		var late = HttpFlight.Join("stories-image");
		Assert.Equal(image, late);
		Assert.True(HttpFlight.WasCancelled("stories-image", late));

		var again = HttpFlight.Open("stories-image");
		Assert.False(HttpFlight.WasCancelled("stories-image", again));
		Assert.True(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));
	}
}
