namespace NativePoc;

public interface IElementHost<TView>
	where TView : class
{
	TView Create(ElementKind kind);

	void Insert(TView parent, int index, TView child);

	void Remove(TView parent, int index);

	void SetText(TView view, string value);

	void SetButton(TView view, string title, string accessibilityLabel);

	void SetTextField(TView view, string value, string placeholder);

	void OnMounted(NodePath path, TView view);

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
				_host.SetTextField(ViewAt(setField.Path), setField.Value, setField.Placeholder);
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
