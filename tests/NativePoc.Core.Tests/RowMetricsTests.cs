using NativePoc;

namespace NativePoc.Tests;

public sealed class RowMetricsTests
{
	[Fact]
	public void A_subtitle_and_an_image_make_different_row_heights()
	{
		var plain = new RowFace("a", "Item", "", "");
		var note = new RowFace("b", "Item", "Ridge line", "");
		var picture = new RowFace("c", "Item", "", "marker");
		var both = new RowFace("d", "Item", "Ridge line", "marker");
		var density = 2.75f;
		var plainHeight = RowMetrics.Height(plain, density, 1);
		var noteHeight = RowMetrics.Height(note, density, 1);
		var pictureHeight = RowMetrics.Height(picture, density, 1);
		var bothHeight = RowMetrics.Height(both, density, 1);
		Assert.True(noteHeight > plainHeight);
		Assert.True(pictureHeight > noteHeight);
		Assert.Equal(noteHeight + RowMetrics.ImageBlock(density), bothHeight);
		Assert.True(RowMetrics.Height(plain, density, 2) > plainHeight);
		Assert.Equal(RowMetrics.ImageBlock(density), pictureHeight - plainHeight);
		Assert.Equal(RowMetrics.ImageBlock(density), RowMetrics.Height(picture, density, 2) - RowMetrics.Height(plain, density, 2));
	}

	[Fact]
	public void Rebinding_a_slot_keeps_the_row_key_it_shows()
	{
		var slot = new RowSlotState();
		var noted = new Column(
		[
			new Text("Canvas tent") { Key = "name" },
			new Text("Ridge line") { Key = "note" },
			new Image("") { Key = "picture" },
		])
		{ Key = "tent" };
		var pictured = new Column(
		[
			new Text("Camp stove") { Key = "name" },
			new Text("") { Key = "note" },
			new Image("marker") { Key = "picture" },
		])
		{ Key = "stove" };
		slot.Bind(noted);
		Assert.Equal("tent", slot.Key);
		Assert.Equal("Ridge line", slot.Subtitle);
		Assert.Equal("", slot.Picture);
		slot.Bind(pictured);
		Assert.Equal("stove", slot.Key);
		Assert.Equal("", slot.Subtitle);
		Assert.Equal("marker", slot.Picture);
		slot.Bind(noted);
		Assert.Equal("tent", slot.Key);
		Assert.Equal("Canvas tent", slot.Title);
		Assert.Equal("Ridge line", slot.Subtitle);
		Assert.Equal("", slot.Picture);
	}
}
