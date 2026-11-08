namespace NativePoc;

public interface IElementHost<TView>
	where TView : class
{
	TView Create(ElementKind kind);

	void Insert(TView parent, int index, TView child);

	void Remove(TView parent, int index);

	void SetText(TView view, string value);

	void SetButton(TView view, string title, string accessibilityLabel);

	void SetTextField(TView view, string value, string placeholder, string accessibilityLabel);

	void OnMounted(NodePath path, TView view);

	void Retarget(NodePath path, TView view);

	void OnUnmounted(NodePath path, TView view);
}

public sealed class ViewReconciler<TView>
	where TView : class
{
	readonly IElementHost<TView> _host;
	readonly Dictionary<NodePath, TView> _views = new();
	Element? _previous;
	Element? _current;

	public ViewReconciler(IElementHost<TView> host)
	{
		ArgumentNullException.ThrowIfNull(host);
		_host = host;
	}

	public TView? Root { get; private set; }

	public Element? Current => _current;

	public TView ViewAt(NodePath path)
	{
		if (!_views.TryGetValue(path, out var view))
			throw new InvalidOperationException("No view is mounted at that path.");
		return view;
	}

	public IReadOnlyList<Patch> Render(Element next)
	{
		ArgumentNullException.ThrowIfNull(next);
		var patches = TreeDiff.Diff(_previous, next);
		_current = next;
		foreach (var patch in patches)
			Apply(patch);
		_previous = next;
		return patches;
	}

	void Apply(Patch patch)
	{
		switch (patch)
		{
			case Mount mount:
				MountView(mount.Path, mount.Kind);
				return;
			case Replace replace:
				UnmountDescendants(replace.Path);
				Detach(replace.Path);
				MountView(replace.Path, replace.Kind);
				return;
			case Unmount unmount:
				UnmountDescendants(unmount.Path);
				Detach(unmount.Path);
				return;
			case SetText setText:
				_host.SetText(ViewAt(setText.Path), setText.Value);
				return;
			case SetButton setButton:
				_host.SetButton(ViewAt(setButton.Path), setButton.Title, setButton.AccessibilityLabel);
				return;
			case SetTextField setField:
				_host.SetTextField(ViewAt(setField.Path), setField.Value, setField.Placeholder, setField.AccessibilityLabel);
				return;
			case Reorder reorder:
				ApplyReorder(reorder.Parent, reorder.Sources);
				return;
			default:
				throw Unknown(patch);
		}
	}

	void MountView(NodePath path, ElementKind kind)
	{
		var view = _host.Create(kind);
		if (path.Length == 0)
			Root = view;
		else
			_host.Insert(ViewAt(path.Parent()), path.LastIndex(), view);
		_views[path] = view;
		_host.OnMounted(path, view);
	}

	void Detach(NodePath path)
	{
		if (!_views.TryGetValue(path, out var view))
			throw new InvalidOperationException("Cannot detach a view that is not mounted.");
		if (path.Length == 0)
			Root = null;
		else
			_host.Remove(ViewAt(path.Parent()), path.LastIndex());
		_views.Remove(path);
		_host.OnUnmounted(path, view);
	}

	void ApplyReorder(NodePath parent, IReadOnlyList<int> sources)
	{
		var oldCount = CountDirectChildren(parent);
		var extracted = new (TView View, List<(NodePath Path, TView View)> Nodes)[oldCount];
		for (var index = oldCount - 1; index >= 0; index--)
		{
			var path = parent.Child(index);
			var view = ViewAt(path);
			var nodes = new List<(NodePath Path, TView View)>();
			foreach (var pair in _views)
			{
				if (pair.Key.Equals(path) || pair.Key.IsDescendantOf(path))
					nodes.Add((pair.Key, pair.Value));
			}
			_host.Remove(ViewAt(parent), index);
			foreach (var node in nodes)
				_views.Remove(node.Path);
			extracted[index] = (view, nodes);
		}

		var kept = new bool[oldCount];
		var packed = 0;
		for (var newIndex = 0; newIndex < sources.Count; newIndex++)
		{
			var oldIndex = sources[newIndex];
			if (oldIndex < 0)
				continue;
			if ((uint)oldIndex >= (uint)oldCount)
				throw new InvalidOperationException("Reorder names a child that is not mounted.");
			kept[oldIndex] = true;
			var (view, nodes) = extracted[oldIndex];
			_host.Insert(ViewAt(parent), packed, view);
			packed++;
			foreach (var node in nodes)
			{
				var updated = ReplaceSegment(node.Path, parent.Length, newIndex);
				_views[updated] = node.View;
				if (!updated.Equals(node.Path))
					_host.Retarget(updated, node.View);
			}
		}

		for (var oldIndex = 0; oldIndex < oldCount; oldIndex++)
		{
			if (kept[oldIndex])
				continue;
			var nodes = extracted[oldIndex].Nodes;
			nodes.Sort(static (left, right) => right.Path.Length.CompareTo(left.Path.Length));
			foreach (var node in nodes)
				_host.OnUnmounted(node.Path, node.View);
		}
	}

	int CountDirectChildren(NodePath parent)
	{
		var count = 0;
		foreach (var path in _views.Keys)
		{
			if (path.Length == parent.Length + 1 && path.Parent().Equals(parent))
				count++;
		}
		return count;
	}

	static NodePath ReplaceSegment(NodePath path, int depth, int index)
	{
		var rewritten = NodePath.Root;
		for (var i = 0; i < path.Length; i++)
			rewritten = rewritten.Child(i == depth ? index : path.Segment(i));
		return rewritten;
	}

	void UnmountDescendants(NodePath path)
	{
		var descendants = new List<NodePath>();
		foreach (var pair in _views)
		{
			if (pair.Key.IsDescendantOf(path))
				descendants.Add(pair.Key);
		}
		descendants.Sort(static (left, right) => right.Length.CompareTo(left.Length));
		foreach (var descendant in descendants)
		{
			var view = _views[descendant];
			_views.Remove(descendant);
			_host.OnUnmounted(descendant, view);
		}
	}

	static InvalidOperationException Unknown(Patch patch) =>
		new("Unknown patch.");
}
