namespace NativePoc.Tests;

public sealed class GestureTests
{
	[Fact]
	public void A_gesture_mask_reaches_the_view_and_a_reload_keeps_that_view()
	{
		var host = new MemoryHost();
		var reconciler = new ViewReconciler<MemoryNode>(host);
		var row = new Column([new Text("Tent") { Key = "name" }])
		{
			Key = "tent",
			Gesture = Gestures.Swipe,
			OnSwipe = () => { },
		};
		reconciler.Render(new Column([row]) { Key = "list" });
		var mounted = reconciler.Root!.Children[0];
		Assert.Equal(Gestures.Swipe, mounted.Gesture);
		var identity = mounted.Identity;
		reconciler.Render(new Column(
		[
			new Column([new Text("Tent") { Key = "name" }])
			{
				Key = "tent",
				Gesture = Gestures.Swipe,
				OnSwipe = () => { },
			},
		])
		{ Key = "list" });
		Assert.Equal(identity, reconciler.Root.Children[0].Identity);
		Assert.False(reconciler.Root.Children[0].Disposed);
	}

	[Fact]
	public void Unmount_during_a_gesture_defers_dispose_until_the_gesture_ends()
	{
		var host = new MemoryHost();
		var reconciler = new ViewReconciler<MemoryNode>(host);
		reconciler.Render(new Column(
		[
			new Text("Tent") { Key = "tent", Gesture = Gestures.Swipe, OnSwipe = () => { } },
		]));
		var mounted = reconciler.Root!.Children[0];
		host.Hold = true;
		reconciler.Render(new Column(Array.Empty<Element>()));
		Assert.True(mounted.Deferred);
		Assert.False(mounted.Disposed);
		host.Hold = false;
		host.Flush();
		Assert.True(mounted.Disposed);
	}

	[Fact]
	public void Swipe_long_press_tap_and_refresh_run_the_action_for_that_id()
	{
		var taps = 0;
		var longs = 0;
		var swipes = 0;
		var refreshes = 0;
		var tree = new Column(
		[
			new Text("Row") { Key = "row", Gesture = Gestures.Tap | Gestures.LongPress, OnTap = () => taps++, OnLongPress = () => longs++ },
			new Column([new Text("Item") { Key = "name" }]) { Key = "item", Gesture = Gestures.Swipe, OnSwipe = () => swipes++ },
		])
		{
			Key = "page",
			Gesture = Gestures.Refresh,
			OnRefresh = () => refreshes++,
		};
		Assert.Equal("tap page/row", ElementIds.Tap(tree, "page/row"));
		Assert.Equal("long-press page/row", ElementIds.LongPress(tree, "page/row"));
		Assert.Equal("swipe page/item", ElementIds.Swipe(tree, "page/item"));
		Assert.Equal("refresh page", ElementIds.Refresh(tree, "page"));
		Assert.Equal(1, taps);
		Assert.Equal(1, longs);
		Assert.Equal(1, swipes);
		Assert.Equal(1, refreshes);
		var payload = ScreenQuery.SwipeId("page/item");
		Assert.Equal(ScreenQuery.Swipe, ScreenQuery.Op(payload));
		Assert.Equal("page/item", System.Text.Encoding.UTF8.GetString(payload.AsSpan(6)));
	}

	[Fact]
	public void Remove_row_drops_only_the_matching_id()
	{
		var bag = new RecordProgram.Bag();
		bag.ReplaceRows("Items", [["tent", "Canvas tent", "false"], ["map", "Ridge map", "false"]]);
		bag.RemoveRow("Items", "tent", 0);
		Assert.Equal(1, bag.CountRows("Items"));
		Assert.Equal("map", bag.Cell("Items", "map", 0, 0));
		Assert.Equal("", bag.Cell("Items", "tent", 0, 0));
	}
}
