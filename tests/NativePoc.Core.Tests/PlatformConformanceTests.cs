namespace NativePoc.Tests;

public sealed class PlatformConformanceTests
{
	[Fact]
	public void HeadlessHost_passes_the_platform_suite()
	{
		PlatformConformance.Run(new HeadlessHost());
	}

	[Fact]
	public void A_long_press_name_is_at_least_48_dp_and_plain_text_stays_a_line()
	{
		var pressed = Assert.Single(HeadlessLayout.Measure(new Text("Canvas tent") { Key = "name", OnLongPress = () => { } }));
		Assert.Equal(48, pressed.Height);
		Assert.True(pressed.Width >= 48);
		var plain = Assert.Single(HeadlessLayout.Measure(new Text("Canvas tent") { Key = "name" }));
		Assert.Equal(24, plain.Height);
		var row = Assert.Single(HeadlessLayout.Measure(new Column([new Text("Coast road") { Key = "name" }]) { Key = "coast", OnTap = () => { } }), box => box.Id == "coast");
		Assert.Equal(48, row.Height);
	}

	[Fact]
	public void The_managed_reconciler_passes_the_platform_suite()
	{
		var surface = new ManagedSurface();
		PlatformConformance.Run(surface);
		Assert.Equal("Kept", surface.Reconciler.Root!.Text);
	}

	[Fact]
	public void The_managed_reconciler_updates_text_in_place_and_removes_a_child()
	{
		var surface = new ManagedSurface();
		var title = "Hello";
		var showNote = true;
		IPlatform? live = null;
		surface.Show(platform =>
		{
			live = platform;
			var children = new List<Element> { new Text(title) { Key = "title" } };
			if (showNote)
				children.Add(new Text("Keep me") { Key = "note" });
			return new Column(children);
		});
		Assert.NotNull(live);
		var kept = surface.Reconciler.ViewAt(NodePath.Root.Child(0));
		var note = surface.Reconciler.ViewAt(NodePath.Root.Child(1));
		var identity = kept.Identity;
		title = "Saved";
		live.RequestRender();
		Assert.Equal(identity, surface.Reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
		Assert.Equal("Saved", surface.Reconciler.ViewAt(NodePath.Root.Child(0)).Text);
		showNote = false;
		live.RequestRender();
		Assert.Equal(1, note.Removed);
		Assert.False(note.Attached);
		Assert.Equal(identity, surface.Reconciler.ViewAt(NodePath.Root.Child(0)).Identity);
	}

	/* AndroidHost is the IElementHost<View> plugged into this reconciler.
	   These nodes stand in for the views, which this machine cannot construct. */
	sealed class ManagedSurface : IPlatform, IPlatformSurface
	{
		readonly ViewReconciler<MemoryNode> _reconciler;
		readonly NavFocusState _nav = new();
		Func<IPlatform, Element>? _render;
		bool _painting;

		public ManagedSurface() => _reconciler = new(new MemoryHost());

		public ViewReconciler<MemoryNode> Reconciler => _reconciler;

		public Element? Tree { get; private set; }

		public string Accessibility => ScreenSnapshot.FormatTree(Tree);

		public int NavDepth => _nav.Depth;

		public string NavFocus => _nav.Focus;

		public void ResetNav() => _nav.Reset();

		public void Show(Func<IPlatform, Element> render)
		{
			_render = render;
			Paint();
		}

		public Element Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag)
		{
			var bound = RecordProgram.Bind(program, bag);
			Show(bound.Render);
			_nav.FocusFrom(Tree);
			return Tree ?? throw new InvalidOperationException("The program rendered no tree.");
		}

		public void CopyNonce(string nonce)
		{
		}

		public void Push(string screen)
		{
			_nav.Push(screen);
			Paint();
			_nav.FocusFrom(Tree);
		}

		public void Push(string screen, object row) => Push(screen);

		public void Pop()
		{
			_nav.Pop();
			Paint();
			_nav.FocusFrom(Tree);
		}

		public void RequestRender() => Paint();

		void Paint()
		{
			if (_render is null || _painting)
				return;
			_painting = true;
			try
			{
				Tree = _render(this);
				_reconciler.Render(Tree);
			}
			finally
			{
				_painting = false;
			}
		}
	}
}
