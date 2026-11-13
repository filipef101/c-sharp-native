namespace NativePoc.Tests;

public sealed class ImageCacheTests
{
	[Fact]
	public void Downsample_keeps_the_view_size_and_halves_until_it_fits()
	{
		Assert.Equal(1, ImageSample.InSampleSize(48, 48, 48));
		Assert.Equal(1, ImageSample.InSampleSize(80, 80, 48));
		Assert.Equal(2, ImageSample.InSampleSize(96, 96, 48));
		Assert.Equal(4, ImageSample.InSampleSize(400, 300, 48));
		Assert.Equal(1, ImageSample.InSampleSize(0, 100, 48));
	}

	[Fact]
	public void Remote_sources_are_http_urls()
	{
		Assert.True(ImageSource.IsRemote("http://127.0.0.1:8094/img/0001"));
		Assert.True(ImageSource.IsRemote("https://example.test/a.png"));
		Assert.False(ImageSource.IsRemote("feed_mark"));
		Assert.False(ImageSource.IsRemote(""));
		Assert.False(ImageSource.IsRemote("http"));
	}

	[Fact]
	public void A_new_bind_token_retires_the_previous_one()
	{
		var slot = new ImageSlot();
		var first = slot.Begin();
		var second = slot.Begin();
		Assert.False(slot.Current(first));
		Assert.True(slot.Current(second));
		Assert.False(slot.Current(0));
	}

	[Fact]
	public void The_cache_drops_the_least_recently_used_entry()
	{
		var cache = new LruCache<string>(2);
		cache.Add("a", "one");
		cache.Add("b", "two");
		Assert.True(cache.TryGet("a", out var kept));
		Assert.Equal("one", kept);
		cache.Add("c", "three");
		Assert.False(cache.TryGet("b", out _));
		Assert.True(cache.TryGet("a", out _));
		Assert.True(cache.TryGet("c", out var newest));
		Assert.Equal("three", newest);
		Assert.Equal(2, cache.Count);
	}
}

public sealed class FeedRowsTests
{
	[Fact]
	public void A_long_feed_keeps_the_header_and_recycles_the_rows()
	{
		var rows = new List<Element>();
		for (var index = 0; index < 6; index++)
			rows.Add(new Text("h" + index) { Key = "h" + index });
		for (var index = 0; index < 30; index++)
			rows.Add(new Text("r") { Key = index.ToString("0000") });
		var column = Assert.IsType<Column>(FeedRows.Window(rows, 6));
		Assert.Equal(7, column.Children.Count);
		var list = Assert.IsType<ListView>(column.Children[6]);
		Assert.Equal("window", list.Key);
		Assert.Null(list.OnRefresh);
		Assert.Equal(30, list.Rows.Count);
		Assert.Equal("0000", list.Rows[0].Key);
	}

	[Fact]
	public void A_short_feed_stays_one_column()
	{
		var rows = new List<Element> { new Text("h") { Key = "heading" }, new Text("r") { Key = "0000" } };
		var column = Assert.IsType<Column>(FeedRows.Window(rows, 6));
		Assert.Equal(2, column.Children.Count);
		Assert.Equal("feed", column.Key);
	}

	[Fact]
	public void A_deferred_feed_builds_a_row_when_it_is_indexed()
	{
		var head = new List<Element> { new Text("Feed") { Key = "heading" } };
		var column = Assert.IsType<Column>(FeedRows.Lazy(head, 1000, index => new Text("r") { Key = index.ToString("0000") }, index => index.ToString("0000"), () => { }));
		var list = Assert.IsType<ListView>(column.Children[1]);
		Assert.NotNull(list.OnRefresh);
		var deferred = Assert.IsType<DeferredList>(list.Rows);
		Assert.Equal(1000, deferred.Count);
		Assert.Equal(0, deferred.Built);
		Assert.Equal("0000", deferred.KeyAt(0));
		Assert.Equal(0, deferred.Built);
		Assert.Equal("0004", deferred[4].Key);
		Assert.Equal(1, deferred.Built);
		Assert.Equal("0004", deferred[4].Key);
		Assert.Equal(1, deferred.Built);
	}
}
