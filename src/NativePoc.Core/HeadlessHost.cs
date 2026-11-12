using System.Globalization;
using System.Text;

namespace NativePoc;

/* A platform with no Android and no iOS view. An agent builds a screen,
   renders the tree, and reads the accessibility and layout dumps. Applying
   a program is the hot-reload edit: the same bytes the listener would
   bind, painted on the same bag. */
public sealed class HeadlessHost : IPlatform
{
	readonly List<string> _copied = new();
	readonly List<string> _pushed = new();
	readonly List<string> _calls = new();
	Func<IPlatform, Element>? _render;
	bool _painting;

	public Element? Tree { get; private set; }

	public IReadOnlyList<string> Copied => _copied;

	public IReadOnlyList<string> Pushed => _pushed;

	public IReadOnlyList<string> Calls => _calls;

	public int Pops { get; private set; }

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
		return Tree ?? throw new InvalidOperationException("The program rendered no tree.");
	}

	public void CopyNonce(string nonce) => _copied.Add(nonce);

	public void RequestRender()
	{
		Renders++;
		Paint();
	}

	public void Push(string screen) => _pushed.Add(screen);

	public void Push(string screen, object row) => _pushed.Add(screen);

	public void Pop() => Pops++;

	public void Module(string module, string method, string[] args)
	{
		_ = args;
		_calls.Add(module + "." + method);
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
		boxes.Insert(at, new Box(id, left, top, width, total));
		return total;
	}

	static int LeafHeight(Element element)
	{
		switch (element)
		{
			case Text text:
				var size = text.Style?.FontSize ?? 0;
				return (size > 0 ? size : 16) + 8;
			case Button:
			case TextField:
			case TextInput:
			case Switch:
			case HostView:
				return 48;
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
