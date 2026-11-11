namespace NativePoc.Tests;

public sealed class StyleEffectTests
{
	[Fact]
	public void A_pending_effect_applies_once_across_a_reload()
	{
		var gate = new EffectGate();
		Assert.True(gate.TryStart("Banner", "places"));
		Assert.False(gate.TryStart("Banner", "places"));
		gate.NoteReload();
		Assert.False(gate.TryStart("Banner", "places"));
		Assert.True(gate.TryApply("Banner", "places"));
		Assert.False(gate.TryApply("Banner", "places"));
		Assert.Equal(1, gate.ApplyCount("Banner", "places"));
		Assert.True(gate.TryStart("Banner", "camps"));
		Assert.False(gate.TryApply("Banner", "places"));
		Assert.True(gate.TryApply("Banner", "camps"));
		Assert.Equal(1, gate.ApplyCount("Banner", "camps"));
	}

	[Fact]
	public void Style_bytes_round_trip()
	{
		var style = new Style
		{
			Padding = 12,
			Margin = 4,
			Gap = 8,
			Grow = 1,
			Align = Align.Center,
			FontSize = 22,
			Weight = FontWeight.Bold,
			Radius = 10,
			Color = 0xFF1B3A2F,
			Background = 0xFFF4F1EA,
		};
		var bytes = new byte[StyleCodec.Bytes];
		StyleCodec.Write(style, bytes);
		Assert.Equal(style, StyleCodec.Read(bytes));
	}
}
