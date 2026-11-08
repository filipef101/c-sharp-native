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

	void SetSwitch(TView view, bool value, string accessibilityLabel);

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

	public void Release()
	{
		if (_current is null)
			return;
		RemoveSubtree(NodePath.Root);
		_previous = null;
		_current = null;
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
				RemoveSubtree(replace.Path);
				MountView(replace.Path, replace.Kind);
				return;
			case Unmount unmount:
				RemoveSubtree(unmount.Path);
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
			case SetSwitch setSwitch:
				_host.SetSwitch(ViewAt(setSwitch.Path), setSwitch.Value, setSwitch.AccessibilityLabel);
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

	void RemoveSubtree(NodePath path)
	{
		if (!_views.ContainsKey(path))
			throw new InvalidOperationException("Cannot detach a view that is not mounted.");
		if (path.Length == 0)
			Root = null;
		else
			_host.Remove(ViewAt(path.Parent()), path.LastIndex());

		var nodes = new List<(NodePath Path, TView View)>();
		foreach (var pair in _views)
		{
			if (pair.Key.Equals(path) || pair.Key.IsDescendantOf(path))
				nodes.Add((pair.Key, pair.Value));
		}
		nodes.Sort(static (left, right) =>
		{
			var byLength = right.Path.Length.CompareTo(left.Path.Length);
			if (byLength != 0)
				return byLength;
			return right.Path.LastIndex().CompareTo(left.Path.LastIndex());
		});
		foreach (var node in nodes)
		{
			if (!node.Path.Equals(path))
				_host.Remove(ViewIn(nodes, node.Path.Parent()), node.Path.LastIndex());
			_views.Remove(node.Path);
			_host.OnUnmounted(node.Path, node.View);
		}
	}

	void ApplyReorder(NodePath parent, IReadOnlyList<int> sources)
	{
		var oldCount = CountDirectChildren(parent);
		var extracted = new (TView View, List<(NodePath Path, TView View)> Nodes)[oldCount];
		for (var index = 0; index < oldCount; index++)
		{
			var path = parent.Child(index);
			var nodes = new List<(NodePath Path, TView View)>();
			foreach (var pair in _views)
			{
				if (pair.Key.Equals(path) || pair.Key.IsDescendantOf(path))
					nodes.Add((pair.Key, pair.Value));
			}
			extracted[index] = (ViewAt(path), nodes);
		}

		var stationary = StationaryNewIndices(sources);
		var kept = new bool[oldCount];
		var stationaryNew = new int[oldCount];
		Array.Fill(stationaryNew, -1);
		for (var newIndex = 0; newIndex < sources.Count; newIndex++)
		{
			var oldIndex = sources[newIndex];
			if (oldIndex < 0)
				continue;
			if ((uint)oldIndex >= (uint)oldCount)
				throw new InvalidOperationException("Reorder names a child that is not mounted.");
			if (kept[oldIndex])
				throw new InvalidOperationException("Reorder names the same child twice.");
			kept[oldIndex] = true;
			if (stationary.Contains(newIndex))
				stationaryNew[oldIndex] = newIndex;
		}

		for (var index = 0; index < oldCount; index++)
		{
			foreach (var node in extracted[index].Nodes)
				_views.Remove(node.Path);
		}

		var parentView = ViewAt(parent);
		for (var oldIndex = oldCount - 1; oldIndex >= 0; oldIndex--)
		{
			if (stationaryNew[oldIndex] >= 0)
				continue;
			_host.Remove(parentView, oldIndex);
			if (kept[oldIndex])
				continue;
			DisposeExtracted(extracted[oldIndex].Nodes, parent.Child(oldIndex));
		}

		var physical = 0;
		for (var newIndex = 0; newIndex < sources.Count; newIndex++)
		{
			var oldIndex = sources[newIndex];
			if (oldIndex < 0)
				continue;
			var (view, nodes) = extracted[oldIndex];
			if (stationaryNew[oldIndex] < 0)
				_host.Insert(parentView, physical, view);
			foreach (var node in nodes)
			{
				var updated = ReplaceSegment(node.Path, parent.Length, newIndex);
				_views[updated] = node.View;
				if (!updated.Equals(node.Path))
					_host.Retarget(updated, node.View);
			}
			physical++;
		}
	}

	void DisposeExtracted(List<(NodePath Path, TView View)> nodes, NodePath root)
	{
		nodes.Sort(static (left, right) =>
		{
			var byLength = right.Path.Length.CompareTo(left.Path.Length);
			if (byLength != 0)
				return byLength;
			return right.Path.LastIndex().CompareTo(left.Path.LastIndex());
		});
		foreach (var node in nodes)
		{
			if (!node.Path.Equals(root))
				_host.Remove(ViewIn(nodes, node.Path.Parent()), node.Path.LastIndex());
			_host.OnUnmounted(node.Path, node.View);
		}
	}

	static TView ViewIn(List<(NodePath Path, TView View)> nodes, NodePath path)
	{
		foreach (var node in nodes)
		{
			if (node.Path.Equals(path))
				return node.View;
		}
		throw new InvalidOperationException("No view is mounted at that path.");
	}

	static HashSet<int> StationaryNewIndices(IReadOnlyList<int> sources)
	{
		var sequence = new List<(int NewIndex, int OldIndex)>();
		for (var i = 0; i < sources.Count; i++)
		{
			if (sources[i] >= 0)
				sequence.Add((i, sources[i]));
		}
		var count = sequence.Count;
		var stationary = new HashSet<int>();
		if (count == 0)
			return stationary;
		var tails = new int[count];
		var tailLength = 0;
		var previous = new int[count];
		Array.Fill(previous, -1);
		for (var i = 0; i < count; i++)
		{
			var old = sequence[i].OldIndex;
			var lo = 0;
			var hi = tailLength;
			while (lo < hi)
			{
				var mid = (lo + hi) / 2;
				if (sequence[tails[mid]].OldIndex < old)
					lo = mid + 1;
				else
					hi = mid;
			}
			if (lo > 0)
				previous[i] = tails[lo - 1];
			tails[lo] = i;
			if (lo == tailLength)
				tailLength++;
		}
		for (var cursor = tails[tailLength - 1]; cursor >= 0; cursor = previous[cursor])
			stationary.Add(sequence[cursor].NewIndex);
		return stationary;
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

	static InvalidOperationException Unknown(Patch patch) =>
		new("Unknown patch.");
}
