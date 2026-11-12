namespace NativePoc;

/* The UIKit host's managed decisions, without a UIView. Linux tests run
   PlatformConformance on this surface. The net10.0-ios project calls the
   same HostPolicy and IosBridge methods and has not been compiled here. */
public sealed class IosView
{
	public IosView(ElementKind kind) => Kind = kind;

	public ElementKind Kind { get; }

	public string Text { get; set; } = "";

	public string AccessibilityValue { get; set; } = "";

	public bool Hidden { get; set; }

	public int TouchFloor { get; set; }

	public bool Heading { get; set; }

	public string FieldClass { get; set; } = "";

	public string ListSummary { get; set; } = "";

	public List<string> Rows { get; } = new();

	public List<IosView> Children { get; } = new();
}

public sealed class IosLogicHost : IElementHost<IosView>
{
	public IosView Create(ElementKind kind) => new(kind);

	public void Insert(IosView parent, int index, IosView child)
	{
		if (!HostPolicy.InsertHonorsIndex(parent.Kind))
			throw new InvalidOperationException("Parent does not take a child.");
		parent.Children.Insert(index, child);
	}

	public void Remove(IosView parent, int index)
	{
		if (!HostPolicy.RemoveAllowed(parent.Kind))
			throw new InvalidOperationException("Parent does not take a child.");
		parent.Children.RemoveAt(index);
	}

	public void SetText(IosView view, string value)
	{
		view.Text = value;
		view.AccessibilityValue = HostPolicy.TextAccessibility(value);
		view.Hidden = HostPolicy.TextIsHidden(value);
	}

	public void SetButton(IosView view, string title, string accessibilityLabel)
	{
		view.Text = title;
		view.AccessibilityValue = accessibilityLabel;
		view.TouchFloor = HostPolicy.TouchFloorDp;
	}

	public void SetTextField(IosView view, string value, string placeholder, string accessibilityLabel)
	{
		view.Text = value;
		view.AccessibilityValue = accessibilityLabel;
		view.ListSummary = placeholder;
		view.TouchFloor = HostPolicy.TouchFloorDp;
	}

	public void SetFieldMode(IosView view, Keyboard keyboard, int lines, Submit submit)
	{
		_ = submit;
		view.FieldClass = HostPolicy.FieldView(lines, keyboard);
	}

	public void SetSwitch(IosView view, bool value, string accessibilityLabel)
	{
		view.Text = value ? "on" : "off";
		view.AccessibilityValue = accessibilityLabel;
		view.TouchFloor = HostPolicy.TouchFloorDp;
	}

	public void SetList(IosView view, IReadOnlyList<Element> rows)
	{
		view.Rows.Clear();
		foreach (var row in rows)
			view.Rows.Add(HostPolicy.RowText(row));
		view.ListSummary = HostPolicy.ListSummary(rows);
		view.AccessibilityValue = view.ListSummary;
	}

	public void SetStyle(IosView view, Style style)
	{
		view.Heading = HostPolicy.IsHeading(style.FontSize);
	}

	public void SetMotion(IosView view, Motion motion)
	{
		_ = view;
		_ = motion;
	}

	public void SetGesture(IosView view, byte mask) => view.TouchFloor = HostPolicy.TouchFloor(mask);

	public void SetImage(IosView view, string source)
	{
		view.Text = source;
		view.Hidden = source.Length == 0;
		view.AccessibilityValue = source;
	}

	public void SetHost(IosView view, string kind, string prop, string value, string eventName, Action<string, string>? onEvent)
	{
		view.Text = kind + " " + prop + "=" + value;
		view.AccessibilityValue = eventName;
		_ = onEvent;
	}

	public void OnMounted(NodePath path, IosView view)
	{
		_ = path;
		_ = view;
	}

	public void Retarget(NodePath path, IosView view)
	{
		_ = path;
		_ = view;
	}

	public void OnUnmounted(NodePath path, IosView view)
	{
		_ = path;
		view.Hidden = true;
	}
}

public sealed class IosLogicSurface : IPlatform, IPlatformSurface
{
	readonly ViewReconciler<IosView> _reconciler;
	readonly IosLogicHost _host = new();
	Func<IPlatform, Element>? _render;
	bool _painting;

	public IosLogicSurface() => _reconciler = new(_host);

	public IosLogicHost Host => _host;

	public ViewReconciler<IosView> Reconciler => _reconciler;

	public Element? Tree { get; private set; }

	public string Accessibility => ScreenSnapshot.FormatTree(Tree);

	public void Show(Func<IPlatform, Element> render)
	{
		_render = render;
		Paint();
	}

	public Element Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag)
	{
		var bound = RecordProgram.Bind(program, bag);
		Show(bound.Render);
		return Tree ?? throw new InvalidOperationException("The program rendered no tree.");
	}

	public void CopyNonce(string nonce)
	{
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
