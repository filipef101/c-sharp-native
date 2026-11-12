namespace NativePoc.Tests;

public sealed class ThemeStampTests
{
	[Fact]
	public void Stamp_writes_dark_and_large_on_every_known_screen()
	{
		var nav = new ScreenNav();
		nav.Allow("tasks");
		nav.Allow("home");
		nav.Bag("shelf").WriteBool("Dark", false);
		nav.StampTheme(true, true);
		Assert.True(nav.Bag("tasks").ReadBool("Dark"));
		Assert.True(nav.Bag("tasks").ReadBool("Large"));
		Assert.True(nav.Bag("home").ReadBool("Dark"));
		Assert.True(nav.Bag("home").ReadBool("Large"));
		Assert.True(nav.Bag("shelf").ReadBool("Dark"));
		Assert.True(nav.Bag("shelf").ReadBool("Large"));
		Assert.DoesNotContain("settings", nav.Ids());
		nav.StampTheme(false, false);
		Assert.False(nav.Bag("tasks").ReadBool("Dark"));
		Assert.False(nav.Bag("tasks").ReadBool("Large"));
	}
}
