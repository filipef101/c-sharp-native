namespace NativePoc.Tests;

public sealed class WarmReloadTests
{
	[Fact]
	public void Keyed_counter_replaces_the_unkeyed_warm_tree()
	{
		var platform = new RecordingPlatform();
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(RecordProgram.Warm(platform));
		Assert.Equal(".", reconciler.Root!.Children[0].Text);

		reconciler.Render(Screen.Render(new AppState(), platform));

		Assert.Equal(5, reconciler.Root.Children.Count);
		Assert.Equal("Counter 0", reconciler.Root.Children[0].Text);
		Assert.Equal("Enter a name", reconciler.Root.Children[3].Text);
		Assert.Equal("Packing", reconciler.Root.Children[4].Title);
	}

	[Fact]
	public void Unkeyed_warm_tree_replaces_the_keyed_counter()
	{
		var platform = new RecordingPlatform();
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(Screen.Render(new AppState(), platform));
		reconciler.Render(RecordProgram.Warm(platform));

		Assert.Single(reconciler.Root!.Children);
		Assert.Equal(".", reconciler.Root.Children[0].Text);
	}

	[Fact]
	public void An_unkeyed_edit_updates_a_child_in_place()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column([new Text("a"), new Text("b")]));
		var identity = reconciler.Root!.Children[0].Identity;
		reconciler.Render(new Column([new Text("a"), new Text("c")]));

		Assert.Equal(identity, reconciler.Root.Children[0].Identity);
		Assert.Equal("a", reconciler.Root.Children[0].Text);
		Assert.Equal("c", reconciler.Root.Children[1].Text);
	}

	[Fact]
	public void Keyed_shelf_insert_delete_and_rename_keep_the_scroll()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(Shelf("harbor", "kiln", "cedar", "night", "field", "rain", "linen", "moss"));
		var scroll = reconciler.Root!;
		scroll.ScrollY = 640;
		var list = scroll.Children[0];
		var harbor = list.Children[0];
		var kiln = list.Children[1];
		var moss = list.Children[7];

		reconciler.Render(Shelf("harbor", "kiln", "cedar", "night", "field", "rain", "linen"));
		Assert.Same(scroll, reconciler.Root);
		Assert.Same(list, scroll.Children[0]);
		Assert.Equal(640, scroll.ScrollY);
		Assert.Equal(harbor.Identity, list.Children[0].Identity);
		Assert.Equal(kiln.Identity, list.Children[1].Identity);
		Assert.Equal(7, list.Children.Count);
		Assert.True(moss.Disposed || moss.Removed > 0);

		reconciler.Render(Shelf("extra", "harbor", "kiln", "cedar", "night", "field", "rain", "linen"));
		Assert.Same(scroll, reconciler.Root);
		Assert.Equal(640, scroll.ScrollY);
		Assert.Equal("extra", list.Children[0].Text);
		Assert.Equal(harbor.Identity, list.Children[1].Identity);
		Assert.Equal(kiln.Identity, list.Children[2].Identity);

		reconciler.Render(Shelf("extra", "harbour", "kiln", "cedar", "night", "field", "rain", "linen"));
		Assert.Same(scroll, reconciler.Root);
		Assert.Equal(640, scroll.ScrollY);
		Assert.Equal(kiln.Identity, list.Children[2].Identity);
		Assert.NotEqual(harbor.Identity, list.Children[1].Identity);
		Assert.Equal("harbour", list.Children[1].Text);
		Assert.True(harbor.Disposed || harbor.Removed > 0);
	}

	static ScrollView Shelf(params string[] keys)
	{
		var tiles = new Element[keys.Length];
		for (var index = 0; index < keys.Length; index++)
			tiles[index] = new Text(keys[index]) { Key = keys[index] };
		return new ScrollView(new Column(tiles) { Key = "list" }) { Key = "scroll" };
	}

	[Fact]
	public void Mixed_keys_match_by_key_and_unkeyed_match_by_position()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("Heading") { Key = "heading" },
			new Text("one"),
			new Text("two"),
		]));
		var heading = reconciler.Root!.Children[0];
		var first = reconciler.Root.Children[1];
		var second = reconciler.Root.Children[2];
		reconciler.Render(new Column(
		[
			new Text("one-b"),
			new Text("Heading") { Key = "heading" },
			new Text("two-b"),
			new Text("extra"),
		]));
		Assert.Equal(heading.Identity, reconciler.Root.Children[1].Identity);
		Assert.Equal(first.Identity, reconciler.Root.Children[0].Identity);
		Assert.Equal("one-b", reconciler.Root.Children[0].Text);
		Assert.Equal(second.Identity, reconciler.Root.Children[2].Identity);
		Assert.Equal("two-b", reconciler.Root.Children[2].Text);
		Assert.Equal("extra", reconciler.Root.Children[3].Text);
		Assert.Equal(4, reconciler.Root.Children.Count);
	}
}
