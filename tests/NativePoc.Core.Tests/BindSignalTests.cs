namespace NativePoc.Tests;

public sealed class BindSignalTests
{
	[Fact]
	public void A_capacity_outside_0_to_100_is_unknown()
	{
		Assert.Equal("unknown", BatteryReading.FromCapacity(-1).Text);
		Assert.False(BatteryReading.FromCapacity(-1).Known);
		Assert.Equal("unknown", BatteryReading.FromCapacity(101).Text);
		Assert.Equal("0", BatteryReading.FromCapacity(0).Text);
		Assert.True(BatteryReading.FromCapacity(0).Known);
		Assert.Equal("100", BatteryReading.FromCapacity(100).Text);
		Assert.Equal(100, BatteryReading.FromCapacity(100).Percent);
	}

	[Fact]
	public void The_same_clip_inside_the_window_is_one_post()
	{
		var frequency = 1_000L;
		Assert.True(ClipDedupe.Repeat(7, "pier", 0, 7, "pier", 50, frequency));
		Assert.True(ClipDedupe.Repeat(0, "pier", 0, 0, "pier", 99, frequency));
		Assert.False(ClipDedupe.Repeat(0, "pier", 0, 0, "pier", 100, frequency));
		Assert.False(ClipDedupe.Repeat(7, "pier", 0, 8, "other", 10, frequency));
		Assert.False(ClipDedupe.Repeat(0, null, 0, 0, "pier", 10, frequency));
	}

	[Fact]
	public void Dark_mode_flips_only_the_light_tokens()
	{
		Assert.Equal(ThemeTokens.CardLight, ThemeTokens.Adapt(ThemeTokens.CardLight, false));
		Assert.Equal(ThemeTokens.CardDark, ThemeTokens.Adapt(ThemeTokens.CardLight, true));
		Assert.Equal(ThemeTokens.TextDark, ThemeTokens.Adapt(ThemeTokens.TextLight, true));
		Assert.Equal(ThemeTokens.PageDark, ThemeTokens.Adapt(ThemeTokens.PageLight, true));
		Assert.Equal(ThemeTokens.MutedDark, ThemeTokens.Adapt(ThemeTokens.MutedLight, true));
		Assert.Equal(ThemeTokens.AccentDark, ThemeTokens.Adapt(ThemeTokens.AccentLight, true));
		Assert.Equal(0u, ThemeTokens.Adapt(0, true));
		Assert.Equal(0xFF0000FFu, ThemeTokens.Adapt(0xFF0000FFu, true));
		Assert.True(ThemeTokens.TryNamed("CardLight", out var card));
		Assert.Equal(ThemeTokens.CardLight, card);
		Assert.False(ThemeTokens.TryNamed("Missing", out _));
	}
}
