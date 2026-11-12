using System.Globalization;
using System.Text;

namespace NativePoc;

/* A platform with no Android and no iOS view. An agent builds a screen,
   renders the tree, and reads the accessibility and layout dumps. Applying
   a program is the hot-reload edit: the same bytes the listener would
   bind, painted on the same bag. */
public sealed class HeadlessHost : IPlatform, IPlatformSurface
{
	readonly List<string> _copied = new();
	readonly List<string> _pushed = new();
	readonly List<string> _calls = new();
	readonly NavFocusState _nav = new();
	Func<IPlatform, Element>? _render;
	bool _painting;

	public Element? Tree { get; private set; }

	public IReadOnlyList<string> Copied => _copied;

	public IReadOnlyList<string> Pushed => _pushed;

	public IReadOnlyList<string> Calls => _calls;

	public int Pops { get; private set; }

	public int NavDepth => _nav.Depth;

	public string NavFocus => _nav.Focus;

	public void ResetNav() => _nav.Reset();

	public int Renders { get; private set; }

	public string Accessibility => ScreenSnapshot.FormatTree(Tree);

	public string Layout => HeadlessLayout.Format(Tree);

	public void Show(Func<IPlatform, Element> render)
	{
		ArgumentNullException.ThrowIfNull(render);
		_render = render;
		Paint();
	}

	public Element Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag)
	{
		ArgumentNullException.ThrowIfNull(bag);
		var bound = RecordProgram.Bind(program, bag);
		Show(bound.Render);
		_nav.FocusFrom(Tree);
		return Tree ?? throw new InvalidOperationException("The program rendered no tree.");
	}

	public void CopyNonce(string nonce) => _copied.Add(nonce);

	public void RequestRender()
	{
		Renders++;
		Paint();
	}

	public void Push(string screen)
	{
		_pushed.Add(screen);
		_nav.Push(screen);
		Paint();
		_nav.FocusFrom(Tree);
	}

	public void Push(string screen, object row) => Push(screen);

	public void Pop()
	{
		Pops++;
		_nav.Pop();
		Paint();
		_nav.FocusFrom(Tree);
	}

	public void Module(string module, string method, string[] args)
	{
		_calls.Add(module + "." + method);
		if (module == "nav" && method == "Push" && args.Length >= 1)
		{
			Push(args[0]);
			return;
		}

		if (module == "nav" && method == "Pop")
			Pop();
	}

	void Paint()
	{
		if (_render is null || _painting)
			return;
		_painting = true;
		try
		{
			Tree = _render(this);
		}
		finally
		{
			_painting = false;
		}
	}
}

public static class HeadlessLayout
{
	public const int PageWidth = 360;

	public readonly record struct Box(string Id, int Left, int Top, int Width, int Height);

	public static string Format(Element? root)
	{
		if (root is null)
			return "";
		var builder = new StringBuilder();
		foreach (var box in Measure(root))
		{
			builder.Append("id=").Append(box.Id);
			builder.Append(" left=").Append(box.Left.ToString(CultureInfo.InvariantCulture));
			builder.Append(" top=").Append(box.Top.ToString(CultureInfo.InvariantCulture));
			builder.Append(" width=").Append(box.Width.ToString(CultureInfo.InvariantCulture));
			builder.Append(" height=").Append(box.Height.ToString(CultureInfo.InvariantCulture));
			builder.Append('\n');
		}

		return builder.ToString();
	}

	public static List<Box> Measure(Element root)
	{
		ArgumentNullException.ThrowIfNull(root);
		var boxes = new List<Box>();
		var id = root.Key is { Length: > 0 } key ? key : "root";
		MeasureNode(root, id, 0, 0, PageWidth, boxes);
		return boxes;
	}

	static int MeasureNode(Element element, string id, int left, int top, int width, List<Box> boxes)
	{
		var pad = element.Style?.Padding ?? 0;
		var innerLeft = left + pad;
		var innerWidth = Math.Max(0, width - (pad * 2));
		var children = Children(element);
		if (children.Count == 0)
		{
			var height = LeafHeight(element) + (pad * 2);
			Floor(element, ref width, ref height);
			boxes.Add(new Box(id, left, top, width, height));
			return height;
		}

		var gap = element.Style?.Gap ?? 0;
		var at = boxes.Count;
		var y = top + pad;
		var content = 0;
		var prefix = id == "root" ? "" : id;
		for (var index = 0; index < children.Count; index++)
		{
			if (index > 0)
			{
				y += gap;
				content += gap;
			}

			var child = children[index];
			var childId = child.Key is { Length: > 0 } key
				? (prefix.Length == 0 ? key : prefix + "/" + key)
				: prefix.Length == 0
					? index.ToString(CultureInfo.InvariantCulture)
					: prefix + "/" + index.ToString(CultureInfo.InvariantCulture);
			var childHeight = MeasureNode(child, childId, innerLeft, y, innerWidth, boxes);
			y += childHeight;
			content += childHeight;
		}

		var total = content + (pad * 2);
		Floor(element, ref width, ref total);
		boxes.Insert(at, new Box(id, left, top, width, total));
		return total;
	}

	static void Floor(Element element, ref int width, ref int height)
	{
		if (element.Gesture == 0 && element.OnTap is null && element.OnLongPress is null && element.OnSwipe is null && element.OnRefresh is null && element.OnUndo is null)
			return;
		if (width < HostPolicy.TouchFloorDp)
			width = HostPolicy.TouchFloorDp;
		if (height < HostPolicy.TouchFloorDp)
			height = HostPolicy.TouchFloorDp;
	}

	static int LeafHeight(Element element)
	{
		switch (element)
		{
			case Text text:
				var size = text.Style?.FontSize ?? 0;
				var line = (size > 0 ? size : 16) + 8;
				// AndroidHost.SetGesture calls TouchTarget, HostPolicy.TouchFloorDp,
				// when the gesture mask is nonzero. A long-press name is that view.
				if (text.Gesture != 0 || text.OnTap is not null || text.OnLongPress is not null || text.OnSwipe is not null || text.OnRefresh is not null || text.OnUndo is not null)
					return line > HostPolicy.TouchFloorDp ? line : HostPolicy.TouchFloorDp;
				return line;
			case Button:
			case TextField:
			case TextInput:
			case Switch:
			case HostView:
				return HostPolicy.TouchFloorDp;
			case Image:
				return 96;
			case ListView:
				return 240;
			case Column:
			case ScrollView:
				return 0;
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static IReadOnlyList<Element> Children(Element element)
	{
		switch (element)
		{
			case Column column:
				return column.Children;
			case ScrollView scroll:
				return [scroll.Child];
			case ListView:
			case Text:
			case Button:
			case TextField:
			case TextInput:
			case Switch:
			case Image:
			case HostView:
				return Array.Empty<Element>();
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}
}
