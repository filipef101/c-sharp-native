namespace NativePoc.Packing.Tests;

sealed class PackNode
{
	static int _next = 1;

	public PackNode(ElementKind kind)
	{
		Kind = kind;
	}

	public int Identity { get; } = _next++;

	public ElementKind Kind { get; }

	public string? Key { get; set; }

	public string? Text { get; set; }

	public string? Title { get; set; }

	public string? AccessibilityLabel { get; set; }

	public string? Value { get; set; }

	public string? Placeholder { get; set; }

	public bool Checked { get; set; }

	public bool Disposed { get; set; }

	public Action? SwitchHandler { get; set; }

	public Action<string>? TextHandler { get; set; }

	public List<PackNode> Children { get; } = new();
}

sealed class PackHost : IElementHost<PackNode>
{
	readonly Func<Element?> _current;

	public PackHost(Func<Element?> current)
	{
		_current = current;
	}

	public PackNode Create(ElementKind kind) => new(kind);

	public void Insert(PackNode parent, int index, PackNode child) => parent.Children.Insert(index, child);

	public void Remove(PackNode parent, int index) => parent.Children.RemoveAt(index);

	public void SetText(PackNode view, string value) => view.Text = value;

	public void SetButton(PackNode view, string title, string accessibilityLabel)
	{
		view.Title = title;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void SetFieldMode(PackNode view, Keyboard keyboard, int lines, Submit submit)
	{
	}

	public void SetTextField(PackNode view, string value, string placeholder, string accessibilityLabel)
	{
		view.Value = value;
		view.Placeholder = placeholder;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void SetList(PackNode view, IReadOnlyList<Element> rows)
	{
	}

	public void SetStyle(PackNode view, Style style)
	{
	}

	public void SetMotion(PackNode view, Motion motion)
	{
	}

	public void SetGesture(PackNode view, byte mask)
	{
	}

	public void SetImage(PackNode view, string source, string description, bool decorative)
	{
		_ = source;
		_ = description;
		_ = decorative;
	}

	public void SetHost(PackNode view, string kind, string prop, string value, string eventName, Action<string, string>? onEvent)
	{
	}

	public void SetSwitch(PackNode view, bool value, string accessibilityLabel)
	{
		view.Checked = value;
		view.AccessibilityLabel = accessibilityLabel;
	}

	public void OnMounted(NodePath path, PackNode view)
	{
		Attach(path, view);
	}

	public void Retarget(NodePath path, PackNode view)
	{
		Attach(path, view);
	}

	public void OnUnmounted(NodePath path, PackNode view)
	{
		view.SwitchHandler = null;
		view.TextHandler = null;
		view.Disposed = true;
	}

	public int Listeners(PackNode view) =>
		(view.SwitchHandler is null ? 0 : 1) + (view.TextHandler is null ? 0 : 1);

	public void Fire(PackNode view)
	{
		if (view.SwitchHandler is null)
			throw new InvalidOperationException("The switch has no listener.");
		view.SwitchHandler();
	}

	public void FireText(PackNode view, string value)
	{
		if (view.TextHandler is null)
			throw new InvalidOperationException("The field has no listener.");
		view.TextHandler(value);
	}

	void Attach(NodePath path, PackNode view)
	{
		view.SwitchHandler = null;
		view.TextHandler = null;
		if (_current() is null)
			return;
		var element = TreeDiff.At(_current()!, path);
		if (element is Switch toggle)
		{
			view.SwitchHandler = () => toggle.OnChange(!view.Checked);
			return;
		}
		if (element is TextField field)
			view.TextHandler = field.OnChange;
	}
}

sealed class FakeServices : INativeServices
{
	public List<string> Pulses { get; } = new();

	public string? Label { get; private set; }

	public string? Text { get; private set; }

	public bool Disposed { get; private set; }

	public void CopyText(string label, string text)
	{
		ObjectDisposedException.ThrowIf(Disposed, this);
		Label = label;
		Text = text;
	}

	public void Pulse()
	{
		ObjectDisposedException.ThrowIf(Disposed, this);
		Pulses.Add("pulse");
	}

	public void Dispose() => Disposed = true;
}

sealed class PackingHarness : IPlatform
{
	public PackingHarness()
	{
		State = new PackingModel();
		ViewReconciler<PackNode>? reconciler = null;
		Host = new PackHost(() => reconciler?.Current);
		reconciler = new ViewReconciler<PackNode>(Host);
		Reconciler = reconciler;
		Render();
	}

	public PackingModel State { get; }

	public PackHost Host { get; }

	public FakeServices Services { get; } = new();

	public ViewReconciler<PackNode> Reconciler { get; }

	public void CopyNonce(string nonce)
	{
	}

	public void CopyText(string label, string text) => Services.CopyText(label, text);

	public void Pulse() => Services.Pulse();

	public void RequestRender() => Render();

	public void Render() => Reconciler.Render(PackingScreen.Render(State, this));

	public void Press(string key)
	{
		var button = (Button)FindElement(Reconciler.Current!, key);
		button.OnPress();
	}

	public void PressRow(string row, string key)
	{
		var button = (Button)FindElement(FindElement(Reconciler.Current!, row), key);
		button.OnPress();
	}

	public void SetPacked(string row, bool value)
	{
		var toggle = (Switch)FindElement(FindElement(Reconciler.Current!, row), "packed");
		toggle.OnChange(value);
	}

	public void SetEditorPacked(bool value)
	{
		var toggle = (Switch)FindElement(Reconciler.Current!, "packed");
		toggle.OnChange(value);
	}

	public void Type(string value)
	{
		var field = (TextField)FindElement(Reconciler.Current!, "name");
		field.OnChange(value);
	}

	public string Text(string key)
	{
		var text = (Text)FindElement(Reconciler.Current!, key);
		return text.Value;
	}

	public string TextAt(string row, string key)
	{
		var text = (Text)FindElement(FindElement(Reconciler.Current!, row), key);
		return text.Value;
	}

	public bool SwitchChecked(string row, string key) => ViewUnder(row, key).Checked;

	public PackNode RowSwitch(string row) => ViewUnder(row, "packed");

	public PackNode Field() => Reconciler.ViewAt(PathOf(Reconciler.Current!, "name"));

	PackNode ViewUnder(string row, string key)
	{
		var root = Reconciler.Current ?? throw new InvalidOperationException("Nothing is mounted.");
		var rowPath = PathOf(root, row);
		var rowElement = (Column)TreeDiff.At(root, rowPath);
		for (var i = 0; i < rowElement.Children.Count; i++)
		{
			if (rowElement.Children[i].Key == key)
				return Reconciler.ViewAt(rowPath.Child(i));
		}
		throw new InvalidOperationException("Missing " + key);
	}

	static Element FindElement(Element root, string key)
	{
		if (root.Key == key)
			return root;
		if (root is Column column)
		{
			foreach (var child in column.Children)
			{
				try
				{
					return FindElement(child, key);
				}
				catch (InvalidOperationException)
				{
				}
			}
		}

		if (root is ScrollView scroll)
			return FindElement(scroll.Child, key);
		throw new InvalidOperationException("Missing " + key);
	}

	static NodePath PathOf(Element root, string key) => PathOf(root, key, NodePath.Root);

	static NodePath PathOf(Element element, string key, NodePath path)
	{
		if (element.Key == key)
			return path;
		if (element is Column column)
		{
			for (var i = 0; i < column.Children.Count; i++)
			{
				try
				{
					return PathOf(column.Children[i], key, path.Child(i));
				}
				catch (InvalidOperationException)
				{
				}
			}
		}

		if (element is ScrollView scroll)
			return PathOf(scroll.Child, key, path.Child(0));
		throw new InvalidOperationException("Missing " + key);
	}
}
