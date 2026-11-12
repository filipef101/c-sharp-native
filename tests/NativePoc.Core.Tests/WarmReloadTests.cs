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
	public void Mixed_keys_in_one_parent_are_still_refused()
	{
		var mixed = new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b"),
		]);
		var ex = Assert.Throws<InvalidOperationException>(() => TreeDiff.Diff(null, mixed));
		Assert.Equal("Keyed children cannot be mixed with unkeyed children.", ex.Message);

		var keyed = new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
		]);
		var again = Assert.Throws<InvalidOperationException>(() => TreeDiff.Diff(keyed, mixed));
		Assert.Equal("Keyed children cannot be mixed with unkeyed children.", again.Message);
	}
}
