using NativePoc;

namespace NativePoc.Tests;

sealed class MemoryNode
{
	static int _next = 1;

	public MemoryNode(ElementKind kind)
	{
		Kind = kind;
	}

	public int Identity { get; } = _next++;

	public int Removed { get; set; }

	public bool Attached { get; set; }

	public bool Disposed { get; set; }

	public bool DisposedWhileAttached { get; set; }

	public ElementKind Kind { get; }

	public string? Text { get; set; }

	public string? Title { get; set; }

	public string? AccessibilityLabel { get; set; }

	public string? Value { get; set; }

	public string? Placeholder { get; set; }

	public bool Checked { get; set; }

	public Style? Style { get; set; }

	public Motion? Motion { get; set; }

	public byte Gesture { get; set; }

	public bool Deferred { get; set; }

	public List<MemoryNode> Children { get; } = new();
}

sealed class MemoryHost : IElementHost<MemoryNode>
{
	public MemoryNode Create(ElementKind kind) => new(kind);

	public void Insert(MemoryNode parent, int index, MemoryNode child)
	{
		child.Attached = true;
		parent.Children.Insert(index, child);
	}

	public void Remove(MemoryNode parent, int index)
	{
		var child = parent.Children[index];
		child.Removed++;
		child.Attached = false;
		parent.Children.RemoveAt(index);
	}

	public void SetText(MemoryNode view, string value) => view.Text = value;

	public void SetButton(MemoryNode view, string title, string accessibilityLabel)
	{
		view.Title = title;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void SetFieldMode(MemoryNode view, Keyboard keyboard, int lines, Submit submit)
	{
	}

	public void SetTextField(MemoryNode view, string value, string placeholder, string accessibilityLabel)
	{
		view.Value = value;
		view.Placeholder = placeholder;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void SetList(MemoryNode view, IReadOnlyList<Element> rows)
	{
	}

	public void SetStyle(MemoryNode view, Style style) => view.Style = style;

	public void SetMotion(MemoryNode view, Motion motion) => view.Motion = motion;

	public void SetGesture(MemoryNode view, byte mask) => view.Gesture = mask;

	public void SetImage(MemoryNode view, string source) => view.Text = source;

	public void SetSwitch(MemoryNode view, bool value, string accessibilityLabel)
	{
		view.Checked = value;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void OnMounted(NodePath path, MemoryNode view)
	{
	}

	public void Retarget(NodePath path, MemoryNode view)
	{
	}

	public bool Hold { get; set; }

	readonly List<MemoryNode> _deferred = new();

	public void OnUnmounted(NodePath path, MemoryNode view)
	{
		view.DisposedWhileAttached = view.Attached;
		view.Attached = false;
		if (Hold)
		{
			view.Deferred = true;
			_deferred.Add(view);
			return;
		}

		view.Disposed = true;
	}

	public void Flush()
	{
		foreach (var view in _deferred)
			view.Disposed = true;
		_deferred.Clear();
	}
}

sealed class RecordingPlatform : IPlatform
{
	public List<string> Nonces { get; } = new();

	public Action? OnRender { get; set; }

	public void CopyNonce(string nonce) => Nonces.Add(nonce);

	public void RequestRender() => OnRender?.Invoke();
}

sealed class Harness
{
	public Harness(AppSession? session = null)
	{
		Session = session ?? new AppSession();
		Reconciler = new ViewReconciler<MemoryNode>(new MemoryHost());
		Platform.OnRender = Render;
	}

	public AppSession Session { get; }

	public AppState State => Session.State;

	public RecordingPlatform Platform { get; } = new();

	public ViewReconciler<MemoryNode> Reconciler { get; }

	public static Harness Restore(AppSession session)
	{
		var harness = new Harness(session);
		harness.Render();
		return harness;
	}

	public void Render() =>
		Reconciler.Render(Screen.Render(State, Platform));

	public IReadOnlyList<Patch> RenderAgain() =>
		Reconciler.Render(Screen.Render(State, Platform));

	public MemoryNode At(int index) =>
		Reconciler.ViewAt(NodePath.Root.Child(index));

	public void ChangeName(string value)
	{
		var field = (TextField)TreeDiff.At(Reconciler.Current!, NodePath.Root.Child(1));
		field.OnChange(value);
	}

	public void PressSave()
	{
		var button = (Button)TreeDiff.At(Reconciler.Current!, NodePath.Root.Child(2));
		button.OnPress();
	}
}
