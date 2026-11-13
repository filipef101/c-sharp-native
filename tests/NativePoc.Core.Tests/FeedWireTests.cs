namespace NativePoc.Tests;

public sealed class FeedWireTests
{
	[Fact]
	public void Source_generated_json_reads_the_feed_document()
	{
		var json = "{\"items\":[{\"id\":\"mark\",\"title\":\"Harbor glaze\"},{\"id\":\"0001\",\"title\":\"Kiln door\"}]}";
		Assert.True(FeedWire.TryRead(json, out var rows));
		Assert.Equal(2, rows.Count);
		Assert.Equal("mark", rows[0].Id);
		Assert.Equal("Harbor glaze", rows[0].Title);
		Assert.Equal("0001", rows[1].Id);
		Assert.Equal("Kiln door", rows[1].Title);
	}

	[Fact]
	public void An_http_error_does_not_become_a_row()
	{
		Assert.False(FeedWire.TryRead("http-error: cancelled", out var rows));
		Assert.Empty(rows);
		Assert.False(FeedWire.TryRead("", out rows));
		Assert.Empty(rows);
	}
}
