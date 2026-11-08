using NativePoc;

namespace NativePoc.Tests;

public sealed class ReconcilerTests
{
	static readonly NodePath Counter = NodePath.Root.Child(0);
	static readonly NodePath Field = NodePath.Root.Child(1);
	static readonly NodePath Save = NodePath.Root.Child(2);
	static readonly NodePath Greeting = NodePath.Root.Child(3);

	[Fact]
	public void First_render_mounts_the_screen()
	{
		var harness = new Harness();
		harness.Render();

		var root = harness.Reconciler.Root;
		Assert.NotNull(root);
		Assert.Equal(ElementKind.Column, root.Kind);
		Assert.Equal(4, root.Children.Count);
		Assert.Equal(ElementKind.Text, harness.At(0).Kind);
		Assert.Equal(ElementKind.TextField, harness.At(1).Kind);
		Assert.Equal(ElementKind.Button, harness.At(2).Kind);
		Assert.Equal(ElementKind.Text, harness.At(3).Kind);
		Assert.Equal("Counter 0", harness.At(0).Text);
		Assert.Equal("", harness.At(1).Value);
		Assert.Equal("Name", harness.At(1).Placeholder);
		Assert.Equal("Save", harness.At(2).Title);
		Assert.Equal("Save name", harness.At(2).AccessibilityLabel);
		Assert.Equal("Enter a name", harness.At(3).Text);
	}

	[Fact]
	public void Typing_updates_the_field_in_place_and_the_greeting()
	{
		var harness = new Harness();
		harness.Render();
		var field = harness.Reconciler.ViewAt(Field);
		var greeting = harness.Reconciler.ViewAt(Greeting);

		harness.ChangeName("A");
		harness.ChangeName("Ad");
		harness.ChangeName("Ada");

		Assert.Equal(field.Identity, harness.Reconciler.ViewAt(Field).Identity);
		Assert.Equal(greeting.Identity, harness.Reconciler.ViewAt(Greeting).Identity);
		Assert.Equal("Ada", harness.Reconciler.ViewAt(Field).Value);
		Assert.Equal("Hello Ada", harness.Reconciler.ViewAt(Greeting).Text);
		Assert.Equal("Ada", harness.State.Name);
	}

	[Fact]
	public void Clearing_the_name_returns_the_empty_greeting()
	{
		var harness = new Harness();
		harness.Render();
		harness.ChangeName("Ada");
		harness.ChangeName("");

		Assert.Equal("Enter a name", harness.Reconciler.ViewAt(Greeting).Text);
		Assert.Equal("", harness.Reconciler.ViewAt(Field).Value);
	}

	[Fact]
	public void Save_increments_the_counter_copies_a_nonce_and_keeps_the_field()
	{
		var harness = new Harness();
		harness.Render();
		harness.ChangeName("Ada");
		var fieldId = harness.Reconciler.ViewAt(Field).Identity;
		var buttonId = harness.Reconciler.ViewAt(Save).Identity;

		harness.PressSave();

		Assert.Equal(1, harness.State.Count);
		Assert.Equal("Counter 1", harness.Reconciler.ViewAt(Counter).Text);
		Assert.Equal("Ada", harness.Reconciler.ViewAt(Field).Value);
		Assert.Equal(fieldId, harness.Reconciler.ViewAt(Field).Identity);
		Assert.Equal(buttonId, harness.Reconciler.ViewAt(Save).Identity);
		Assert.Equal("poc-1", Assert.Single(harness.Platform.Nonces));
	}

	[Fact]
	public void Save_still_counts_when_the_name_is_empty()
	{
		var harness = new Harness();
		harness.Render();

		harness.PressSave();

		Assert.Equal(1, harness.State.Count);
		Assert.Equal("Enter a name", harness.Reconciler.ViewAt(Greeting).Text);
		Assert.Equal("poc-1", Assert.Single(harness.Platform.Nonces));
	}

	[Fact]
	public void A_new_activity_keeps_the_same_state()
	{
		var first = new Harness();
		first.Render();
		first.ChangeName("Ada");
		first.PressSave();
		var firstField = first.Reconciler.ViewAt(Field).Identity;

		var second = Harness.Restore(first.Session);

		Assert.Same(first.State, second.State);
		Assert.Equal("Ada", second.State.Name);
		Assert.Equal(1, second.State.Count);
		Assert.Equal("Ada", second.Reconciler.ViewAt(Field).Value);
		Assert.Equal("Counter 1", second.Reconciler.ViewAt(Counter).Text);
		Assert.Equal("Hello Ada", second.Reconciler.ViewAt(Greeting).Text);
		Assert.NotEqual(firstField, second.Reconciler.ViewAt(Field).Identity);
	}

	[Fact]
	public void Rendering_again_without_a_state_change_emits_no_patches()
	{
		var harness = new Harness();
		harness.Render();

		var patches = harness.RenderAgain();

		Assert.Empty(patches);
	}

	[Fact]
	public void Replacing_a_child_keeps_the_sibling_identity()
	{
		var host = new MemoryHost();
		var reconciler = new ViewReconciler<MemoryNode>(host);
		reconciler.Render(new Column([new Text("a"), new Text("b")]));
		var sibling = reconciler.ViewAt(NodePath.Root.Child(1)).Identity;

		var patches = reconciler.Render(new Column(
		[
			new Button("x", static () => { }, "x"),
			new Text("b"),
		]));

		Assert.Contains(patches, patch => patch is Replace replace
			&& replace.Path == NodePath.Root.Child(0)
			&& replace.Kind == ElementKind.Button);
		Assert.NotEqual(sibling, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal(sibling, reconciler.ViewAt(NodePath.Root.Child(1)).Identity);
		Assert.Equal("b", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
	}

	[Fact]
	public void Removing_a_trailing_child_unmounts_only_that_child()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column([new Text("a"), new Text("b")]));
		var kept = reconciler.ViewAt(NodePath.Root.Child(0)).Identity;

		var patches = reconciler.Render(new Column([new Text("a")]));

		var unmount = Assert.Single(patches);
		var removed = Assert.IsType<Unmount>(unmount);
		Assert.Equal(NodePath.Root.Child(1), removed.Path);
		Assert.Equal(kept, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Single(reconciler.Root!.Children);
	}

	[Fact]
	public void Inserting_a_child_keeps_the_existing_child()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column([new Text("a")]));
		var kept = reconciler.ViewAt(NodePath.Root.Child(0)).Identity;

		reconciler.Render(new Column([new Text("a"), new Text("b")]));

		Assert.Equal(kept, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal("b", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
		Assert.Equal(2, reconciler.Root!.Children.Count);
	}
}
