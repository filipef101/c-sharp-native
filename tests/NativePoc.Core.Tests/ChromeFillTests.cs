namespace NativePoc.Tests;

public sealed class ChromeFillTests
{
	[Fact]
	public void First_frame_uses_the_midpoint_when_every_ink_passes()
	{
		var style = new Style { Background = 0xFF0F6E56, Wash = 0xFF3FA888 };
		var ink = new List<ChromeInk> { new(0xFFF7F4EF, 32, FontWeight.Bold) };
		Assert.Equal(0xFF278B6Fu, ChromeFill.Mix(style.Background, style.Wash));
		Assert.Equal(ChromeFill.Mix(style.Background, style.Wash), ChromeFill.FirstFrame(style, ink));
	}

	[Fact]
	public void First_frame_uses_the_stop_that_passes_when_the_midpoint_fails()
	{
		var style = new Style { Background = 0xFF0F6E56, Wash = 0xFF3FA888 };
		var ink = new List<ChromeInk>
		{
			new(0xFFF7F4EF, 32, FontWeight.Bold),
			new(0xFFD5EBE3, 16, FontWeight.Regular),
		};
		var mid = ChromeFill.Mix(style.Background, style.Wash);
		Assert.False(ChromeFill.Legible(0xFFD5EBE3, mid, 16, FontWeight.Regular));
		Assert.True(ChromeFill.Legible(0xFFD5EBE3, style.Background, 16, FontWeight.Regular));
		Assert.Equal(style.Background, ChromeFill.FirstFrame(style, ink));
	}

	[Fact]
	public void First_frame_of_a_solid_card_is_that_fill()
	{
		var style = new Style { Background = 0xFFE4F0EB };
		var ink = new List<ChromeInk>
		{
			new(0xFF143028, 18, FontWeight.Bold),
			new(0xFF3D4F48, 13, FontWeight.Regular),
		};
		Assert.Equal(0xFFE4F0EBu, ChromeFill.FirstFrame(style, ink));
		Assert.True(ChromeFill.Legible(0xFF3D4F48, style.Background, 13, FontWeight.Regular));
	}
}
