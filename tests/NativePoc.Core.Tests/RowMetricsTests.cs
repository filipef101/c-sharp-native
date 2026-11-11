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

	[Fact]
	public void The_height_prefix_matches_a_linear_walk()
	{
		var prefix = new RowPrefix(1000, 10);
		var heights = new int[1000];
		for (var index = 0; index < heights.Length; index++)
			heights[index] = 10;
		Expect(prefix, heights);
		for (var index = 0; index < heights.Length; index++)
		{
			if (index % 3 == 0)
				heights[index] += 8;
			if (index % 4 == 0)
				heights[index] += 40;
			prefix.Set(index, heights[index]);
			prefix.Set(index, heights[index]);
		}

		Expect(prefix, heights);
		Assert.Equal(0, prefix.IndexAt(0));
		Assert.Equal(999, prefix.IndexAt(prefix.Total));
		Assert.Equal(0, new RowPrefix(0, 10).IndexAt(4));
	}

	static void Expect(RowPrefix prefix, int[] heights)
	{
		var y = 0;
		for (var index = 0; index < heights.Length; index++)
		{
			Assert.Equal(y, prefix.Y(index));
			Assert.Equal(index, prefix.IndexAt(y));
			Assert.Equal(index, prefix.IndexAt(y + heights[index] - 1));
			y += heights[index];
		}

		Assert.Equal(y, prefix.Total);
		Assert.Equal(y, prefix.Y(heights.Length));
	}
}
