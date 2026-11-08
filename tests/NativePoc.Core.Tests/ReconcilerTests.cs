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
	public void A_title_only_render_keeps_the_field_the_button_and_the_state()
	{
		var harness = new Harness();
		harness.Render();
		harness.ChangeName("Ada");
		harness.PressSave();
		var field = harness.Reconciler.ViewAt(Field).Identity;
		var button = harness.Reconciler.ViewAt(Save).Identity;
		var greeting = harness.Reconciler.ViewAt(Greeting).Identity;

		harness.Reconciler.Render(Reloaded(harness.State, harness.Platform, "Tally "));

		Assert.Equal("Tally 1", harness.Reconciler.ViewAt(Counter).Text);
		Assert.Equal("Ada", harness.State.Name);
		Assert.Equal(1, harness.State.Count);
		Assert.Equal("Ada", harness.Reconciler.ViewAt(Field).Value);
		Assert.Equal("Hello Ada", harness.Reconciler.ViewAt(Greeting).Text);
		Assert.Equal(field, harness.Reconciler.ViewAt(Field).Identity);
		Assert.Equal(button, harness.Reconciler.ViewAt(Save).Identity);
		Assert.Equal(greeting, harness.Reconciler.ViewAt(Greeting).Identity);
	}

	[Fact]
	public void Replacing_a_nested_column_drops_its_descendants_and_keeps_the_sibling()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		var nested = NodePath.Root.Child(0);
		var leaf = nested.Child(0);
		reconciler.Render(new Column(
		[
			new Column([new Text("inner")]),
			new Text("sib"),
		]));
		var sibling = reconciler.ViewAt(NodePath.Root.Child(1)).Identity;

		reconciler.Render(new Column(
		[
			new Text("flat"),
			new Text("sib"),
		]));

		Assert.Equal("flat", reconciler.ViewAt(nested).Text);
		Assert.Equal(sibling, reconciler.ViewAt(NodePath.Root.Child(1)).Identity);
		Assert.Equal(2, reconciler.Root!.Children.Count);
		Assert.Empty(reconciler.ViewAt(nested).Children);
		Assert.Throws<InvalidOperationException>(() => reconciler.ViewAt(leaf));
	}

	static Element Reloaded(AppState state, IPlatform platform, string prefix)
	{
		return new Column(
		[
			new Text(prefix + state.Count) { Key = "title" },
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					platform.RequestRender();
				},
				"Name",
				"Name")
			{ Key = "name" },
			new Button(
				"Save",
				() =>
				{
					state.Count++;
					platform.CopyNonce("poc-" + state.Count);
					platform.RequestRender();
				},
				"Save name")
			{ Key = "save" },
			new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name) { Key = "greeting" },
		]);
	}

	[Fact]
	public void Deleting_a_middle_keyed_child_keeps_the_other_views()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
			new Text("c") { Key = "c" },
		]));
		var first = reconciler.ViewAt(NodePath.Root.Child(0)).Identity;
		var third = reconciler.ViewAt(NodePath.Root.Child(2)).Identity;

		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("c") { Key = "c" },
		]));

		Assert.Equal(first, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal("a", reconciler.ViewAt(NodePath.Root.Child(0)).Text);
		Assert.Equal(third, reconciler.ViewAt(NodePath.Root.Child(1)).Identity);
		Assert.Equal("c", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
		Assert.Equal(2, reconciler.Root!.Children.Count);
	}

	[Fact]
	public void Reordering_keyed_children_keeps_each_view()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
		]));
		var first = reconciler.ViewAt(NodePath.Root.Child(0)).Identity;
		var second = reconciler.ViewAt(NodePath.Root.Child(1)).Identity;

		reconciler.Render(new Column(
		[
			new Text("b") { Key = "b" },
			new Text("a") { Key = "a" },
		]));

		Assert.Equal(second, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal("b", reconciler.ViewAt(NodePath.Root.Child(0)).Text);
		Assert.Equal(first, reconciler.ViewAt(NodePath.Root.Child(1)).Identity);
		Assert.Equal("a", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
	}

	[Fact]
	public void Deleting_a_leading_keyed_field_keeps_the_shifted_field()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new TextField("keep", _ => { }, "Keep", "Keep field") { Key = "keep" },
			new TextField("shifted", _ => { }, "Shifted", "Shifted field") { Key = "shifted" },
		]));
		var shifted = reconciler.ViewAt(NodePath.Root.Child(1)).Identity;

		reconciler.Render(new Column(
		[
			new TextField("shifted", _ => { }, "Shifted", "Shifted field") { Key = "shifted" },
		]));

		Assert.Equal(shifted, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal("shifted", reconciler.ViewAt(NodePath.Root.Child(0)).Value);
		Assert.Equal(0, reconciler.ViewAt(NodePath.Root.Child(0)).Removed);
		Assert.Single(reconciler.Root!.Children);
	}

	[Fact]
	public void Screen_render_assigns_a_key_to_every_child()
	{
		var column = Assert.IsType<Column>(Screen.Render(new AppState(), new RecordingPlatform()));
		Assert.Equal("title,name,save,greeting", string.Join(",", column.Children.Select(child => child.Key)));
	}

	[Fact]
	public void Screen_render_keeps_the_field_and_button_when_the_greeting_moves_first()
	{
		var platform = new RecordingPlatform();
		var state = new AppState();
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(Screen.Render(state, platform));
		var title = reconciler.ViewAt(NodePath.Root.Child(0));
		var field = reconciler.ViewAt(NodePath.Root.Child(1));
		var button = reconciler.ViewAt(NodePath.Root.Child(2));
		var greeting = reconciler.ViewAt(NodePath.Root.Child(3));

		state.GreetingFirst = true;
		reconciler.Render(Screen.Render(state, platform));

		Assert.Equal("greeting,name,save,title", string.Join(",", ((Column)Screen.Render(state, platform)).Children.Select(child => child.Key)));
		Assert.Same(greeting, reconciler.ViewAt(NodePath.Root.Child(0)));
		Assert.Same(field, reconciler.ViewAt(NodePath.Root.Child(1)));
		Assert.Same(button, reconciler.ViewAt(NodePath.Root.Child(2)));
		Assert.Same(title, reconciler.ViewAt(NodePath.Root.Child(3)));
		Assert.Equal(0, field.Removed);
		Assert.Equal(0, button.Removed);
		Assert.False(field.Disposed);
		Assert.False(button.Disposed);
		Assert.False(title.Disposed);
		Assert.False(greeting.Disposed);
	}

	[Fact]
	public void Deleting_a_middle_keyed_child_leaves_the_other_views_attached()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
			new Text("c") { Key = "c" },
			new Text("d") { Key = "d" },
		]));
		var first = reconciler.ViewAt(NodePath.Root.Child(0));
		var second = reconciler.ViewAt(NodePath.Root.Child(1));
		var third = reconciler.ViewAt(NodePath.Root.Child(2));
		var fourth = reconciler.ViewAt(NodePath.Root.Child(3));

		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("c") { Key = "c" },
			new Text("d") { Key = "d" },
		]));

		Assert.Equal(0, first.Removed);
		Assert.Equal(0, third.Removed);
		Assert.Equal(0, fourth.Removed);
		Assert.False(first.Disposed);
		Assert.False(third.Disposed);
		Assert.False(fourth.Disposed);
		Assert.Equal(1, second.Removed);
		Assert.True(second.Disposed);
		Assert.False(second.DisposedWhileAttached);
		Assert.Equal("a,c,d", string.Join(",", reconciler.Root!.Children.Select(child => child.Text)));
	}

	[Fact]
	public void Reordering_keyed_children_leaves_a_view_attached()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
			new Text("c") { Key = "c" },
			new Text("d") { Key = "d" },
		]));
		var nodes = Enumerable.Range(0, 4).Select(index => reconciler.ViewAt(NodePath.Root.Child(index))).ToArray();

		reconciler.Render(new Column(
		[
			new Text("d") { Key = "d" },
			new Text("c") { Key = "c" },
			new Text("b") { Key = "b" },
			new Text("a") { Key = "a" },
		]));

		Assert.Contains(nodes, node => node.Removed == 0);
		Assert.All(nodes, node => Assert.False(node.Disposed));
		Assert.Equal("d,c,b,a", string.Join(",", reconciler.Root!.Children.Select(child => child.Text)));
		Assert.Equal(nodes[3].Identity, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal(nodes[0].Identity, reconciler.ViewAt(NodePath.Root.Child(3)).Identity);
	}

	[Fact]
	public void Inserting_a_keyed_child_in_the_middle_leaves_the_others_attached()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
			new Text("c") { Key = "c" },
		]));
		var nodes = Enumerable.Range(0, 3).Select(index => reconciler.ViewAt(NodePath.Root.Child(index))).ToArray();

		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("x") { Key = "x" },
			new Text("b") { Key = "b" },
			new Text("c") { Key = "c" },
		]));

		Assert.All(nodes, node => Assert.Equal(0, node.Removed));
		Assert.Equal(nodes[0].Identity, reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal(nodes[1].Identity, reconciler.ViewAt(NodePath.Root.Child(2)).Identity);
		Assert.Equal(nodes[2].Identity, reconciler.ViewAt(NodePath.Root.Child(3)).Identity);
		Assert.Equal("x", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
	}

	[Fact]
	public void Replacing_a_keyed_column_disposes_its_children_after_they_leave_the_parent()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Column(
			[
				new Text("inner") { Key = "inner" },
				new Button("Go", static () => { }, "Go") { Key = "go" },
			])
			{ Key = "nest" },
			new Text("sib") { Key = "sib" },
		]));
		var nest = reconciler.ViewAt(NodePath.Root.Child(0));
		var inner = nest.Children[0];
		var button = nest.Children[1];
		var sibling = reconciler.ViewAt(NodePath.Root.Child(1));

		reconciler.Render(new Column(
		[
			new Text("flat") { Key = "nest" },
			new Text("sib") { Key = "sib" },
		]));

		Assert.True(nest.Disposed);
		Assert.True(inner.Disposed);
		Assert.True(button.Disposed);
		Assert.False(nest.DisposedWhileAttached);
		Assert.False(inner.DisposedWhileAttached);
		Assert.False(button.DisposedWhileAttached);
		Assert.False(sibling.Disposed);
		Assert.Equal(0, sibling.Removed);
		Assert.Equal("flat", reconciler.ViewAt(NodePath.Root.Child(0)).Text);
		Assert.Equal(sibling.Identity, reconciler.ViewAt(NodePath.Root.Child(1)).Identity);
	}

	[Fact]
	public void Replacing_the_root_mounts_a_new_view_and_disposes_the_old_tree()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column([new Text("a") { Key = "a" }]));
		var oldRoot = reconciler.Root!;
		var child = oldRoot.Children[0];

		reconciler.Render(new Text("flat"));

		Assert.NotSame(oldRoot, reconciler.Root);
		Assert.True(oldRoot.Disposed);
		Assert.True(child.Disposed);
		Assert.False(oldRoot.DisposedWhileAttached);
		Assert.False(child.DisposedWhileAttached);
		Assert.Equal("flat", reconciler.Root!.Text);
		Assert.Empty(reconciler.Root.Children);
	}

	[Fact]
	public void Keyed_structure_keeps_identity_across_random_edits()
	{
		var random = new Random(20261108);
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		var order = new List<string> { "a", "b", "c", "d", "e" };
		var live = new Dictionary<string, MemoryNode>();
		RenderOrder();
		Remember();
		for (var step = 0; step < 200; step++)
		{
			var before = order.ToArray();
			var roll = random.Next(4);
			if (roll == 0 && order.Count > 1)
				order.RemoveAt(random.Next(order.Count));
			else if (roll == 1)
				order.Insert(random.Next(order.Count + 1), "n" + step);
			else if (roll == 2 && order.Count > 1)
			{
				var left = random.Next(order.Count);
				var right = random.Next(order.Count);
				(order[left], order[right]) = (order[right], order[left]);
			}
			else if (order.Count > 1)
				order.Reverse();

			var removed = before.Where(key => !order.Contains(key)).ToArray();
			RenderOrder();
			Assert.Equal(order.ToArray(), reconciler.Root!.Children.Select(child => child.Text).ToArray());
			for (var index = 0; index < order.Count; index++)
			{
				var key = order[index];
				var child = reconciler.ViewAt(NodePath.Root.Child(index));
				if (live.TryGetValue(key, out var previous))
				{
					Assert.Equal(previous.Identity, child.Identity);
					Assert.False(previous.Disposed);
				}
				live[key] = child;
			}
			foreach (var key in removed)
			{
				Assert.True(live[key].Disposed);
				Assert.False(live[key].DisposedWhileAttached);
				live.Remove(key);
			}
		}

		void RenderOrder()
		{
			reconciler.Render(new Column(order.Select(key => new Text(key) { Key = key }).ToArray()));
		}

		void Remember()
		{
			live.Clear();
			for (var index = 0; index < order.Count; index++)
				live[order[index]] = reconciler.ViewAt(NodePath.Root.Child(index));
		}
	}

	[Fact]
	public void Mixed_and_duplicate_keys_are_rejected()
	{
		var reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "b" },
		]));

		Assert.Throws<InvalidOperationException>(() => reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b"),
		])));
		Assert.Throws<InvalidOperationException>(() => reconciler.Render(new Column(
		[
			new Text("a") { Key = "a" },
			new Text("b") { Key = "a" },
		])));
		Assert.Equal("a", reconciler.ViewAt(NodePath.Root.Child(0)).Text);
		Assert.Equal("b", reconciler.ViewAt(NodePath.Root.Child(1)).Text);
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
