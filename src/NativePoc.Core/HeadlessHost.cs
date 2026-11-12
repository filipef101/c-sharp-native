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
	string? _lastOpen;
	readonly NavFocusState _nav = new();
	Func<IPlatform, Element>? _render;
	bool _painting;

	public Element? Tree { get; private set; }

	public IReadOnlyList<string> Copied => _copied;

	public IReadOnlyList<string> Pushed => _pushed;

	public IReadOnlyList<string> Calls => _calls;

	public int Pops { get; private set; }

	public int NavDepth => _nav.Depth;

	internal string NavTop => _nav.Top;

	internal object? NavArgument { get; private set; }

	public string NavFocus => _nav.Focus;

	public void ResetNav() => _nav.Reset();

	public int Renders { get; private set; }

	internal Func<string, string, string[], string>? ModuleReply { get; set; }

	public string Accessibility => ScreenSnapshot.FormatTree(Tree);

	public string Layout => HeadlessLayout.Format(Tree);

	public void Show(Func<IPlatform, Element> render)
	{
		ArgumentNullException.ThrowIfNull(render);
		_render = render;
		Paint();
	}

	Element IPlatformSurface.Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag) => Apply(program, bag);

	internal Element Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag)
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

	public void Push(string screen, object row)
	{
		NavArgument = row;
		Push(screen);
	}

	public void Pop()
	{
		Pops++;
		_nav.Pop();
		Paint();
		_nav.FocusFrom(Tree);
	}

	public void OnOpen(string screen, Action open)
	{
		ArgumentNullException.ThrowIfNull(open);
		if (screen.Length == 0 || string.Equals(screen, _lastOpen, StringComparison.Ordinal))
			return;
		_lastOpen = screen;
		_calls.Add("open " + screen);
		open();
	}

	public Task<string> ModuleAsync(string module, string method, string[] args)
	{
		_calls.Add(module + "." + method + " " + string.Join(' ', args));
		var text = ModuleReply?.Invoke(module, method, args) ?? "ok";
		return Task.FromResult(text);
	}

	internal bool SwipeBack(float fraction, float velocity)
	{
		var commit = MotionContract.Commits(fraction, velocity);
		_calls.Add(commit ? "nav-swipe commit" : "nav-swipe cancel");
		if (commit)
			Pop();
		return commit;
	}

	internal int PlayAnimated(string key, int from, int to, int basis, float animatorScale, float transitionScale, bool spring)
	{
		var ms = MotionContract.AnimatedMs(basis, animatorScale, transitionScale);
		var value = ms == 0 ? to : MotionContract.AnimatedValue(from, to, 0, ms, spring);
		_calls.Add(
			"animated key=" + key
			+ " ms=" + ms.ToString(CultureInfo.InvariantCulture)
			+ " value=" + value.ToString(CultureInfo.InvariantCulture));
		return value;
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

	public static List<Box> Measure(Element root) => Measure(root, PageWidth, 1f);

	public static List<Box> Measure(Element root, float fontScale) => Measure(root, PageWidth, fontScale);

	internal const int SystemBarDp = 24;

	internal static List<Box> MeasureBelowBars(Element root, float fontScale) =>
		Measure(root, PageWidth, fontScale, SystemBarDp);

	public static List<Box> Measure(Element root, int width, float fontScale) =>
		Measure(root, width, fontScale, 0);

	static List<Box> Measure(Element root, int width, float fontScale, int topInset)
	{
		ArgumentNullException.ThrowIfNull(root);
		if (fontScale <= 0)
			fontScale = 1f;
		if (width < 0)
			width = 0;
		if (topInset < 0)
			topInset = 0;
		var boxes = new List<Box>();
		var id = root.Key is { Length: > 0 } key ? key : "root";
		MeasureNode(root, id, 0, topInset, width, boxes, fontScale);
		return boxes;
	}

	static int MeasureNode(Element element, string id, int left, int top, int width, List<Box> boxes, float fontScale, bool widthShare = false)
	{
		var allotted = width;
		var pad = element.Style?.Padding ?? 0;
		var innerLeft = left + pad;
		var innerWidth = Math.Max(0, width - (pad * 2));
		var children = Children(element);
		if (children.Count == 0)
		{
			int height;
			if (element is Button button)
			{
				var scale = fontScale;
				if (!widthShare && button.Style is { Grow: > 0 })
					scale = 1f;
				height = HostPolicy.ButtonDp(button.Style?.FontSize ?? 0, pad, scale);
			}
			else
				height = LeafHeight(element, fontScale, widthShare) + (pad * 2);
			Floor(element, allotted, ref width, ref height);
			boxes.Add(new Box(id, left, top, width, height));
			return height;
		}

		var gap = element.Style?.Gap ?? 0;
		var at = boxes.Count;
		var prefix = id == "root" ? "" : id;
		if (element is ListView && element.Style is { Across: >= 2 } grid)
			return MeasureGrid(element, id, left, top, width, pad, innerLeft, innerWidth, children, grid.Across, gap, boxes, fontScale);
		if (element.Style is { Across: > 0 })
		{
			var count = children.Count;
			var gaps = count > 1 ? gap * (count - 1) : 0;
			var available = Math.Max(0, innerWidth - gaps);
			var slot = count == 0 ? 0 : available / count;
			var extra = count == 0 ? 0 : available % count;
			var rowTop = top + pad;
			var max = 0;
			var x = innerLeft;
			for (var index = 0; index < count; index++)
			{
				var child = children[index];
				var childId = ChildId(child, prefix, index);
				var childWidth = slot + (index < extra ? 1 : 0);
				var childHeight = MeasureNode(child, childId, x, rowTop, childWidth, boxes, fontScale, widthShare: true);
				if (childHeight > max)
					max = childHeight;
				x += childWidth;
				if (index + 1 < count)
					x += gap;
			}

			var across = max + (pad * 2);
			Floor(element, allotted, ref width, ref across);
			boxes.Insert(at, new Box(id, left, top, width, across));
			return across;
		}

		var y = top + pad;
		var content = 0;
		for (var index = 0; index < children.Count; index++)
		{
			if (index > 0)
			{
				y += gap;
				content += gap;
			}

			var child = children[index];
			var childId = ChildId(child, prefix, index);
			var childHeight = MeasureNode(child, childId, innerLeft, y, innerWidth, boxes, fontScale);
			y += childHeight;
			content += childHeight;
		}

		var total = content + (pad * 2);
		Floor(element, allotted, ref width, ref total);
		boxes.Insert(at, new Box(id, left, top, width, total));
		return total;
	}

	static int MeasureGrid(Element element, string id, int left, int top, int allotted, int pad, int innerLeft, int innerWidth, IReadOnlyList<Element> children, int columns, int gap, List<Box> boxes, float fontScale)
	{
		var at = boxes.Count;
		var prefix = id == "root" ? "" : id;
		if (columns < 2)
			columns = 2;
		var count = children.Count;
		var cols = count < columns ? count : columns;
		if (cols < 1)
			cols = 1;
		var gaps = cols > 1 ? gap * (cols - 1) : 0;
		var available = Math.Max(0, innerWidth - gaps);
		var slot = count == 0 ? 0 : available / cols;
		var y = top + pad;
		var content = 0;
		var col = 0;
		var x = innerLeft;
		var rowHeight = 0;
		for (var index = 0; index < count; index++)
		{
			if (col == 0)
			{
				rowHeight = 0;
				x = innerLeft;
			}

			var child = children[index];
			var childId = ChildId(child, prefix, index);
			var childHeight = MeasureNode(child, childId, x, y, slot, boxes, fontScale, widthShare: true);
			if (childHeight > rowHeight)
				rowHeight = childHeight;
			col++;
			x += slot;
			if (col < cols)
				x += gap;
			if (col == columns || index + 1 == count)
			{
				y += rowHeight;
				content += rowHeight;
				col = 0;
				if (index + 1 < count && gap > 0)
				{
					y += gap;
					content += gap;
				}
			}
		}

		var width = allotted;
		var total = content + (pad * 2);
		Floor(element, allotted, ref width, ref total);
		boxes.Insert(at, new Box(id, left, top, width, total));
		return total;
	}

	static void Floor(Element element, int allotted, ref int width, ref int height)
	{
		if (element.Gesture == 0 && element.OnTap is null && element.OnLongPress is null && element.OnSwipe is null && element.OnRefresh is null && element.OnUndo is null)
			return;
		if (width < HostPolicy.TouchFloorDp)
			width = HostPolicy.TouchFloorDp;
		if (height < HostPolicy.TouchFloorDp)
			height = HostPolicy.TouchFloorDp;
		if (width > allotted)
			width = allotted;
	}

	static int LeafHeight(Element element, float fontScale, bool widthShare)
	{
		// A vertical weighted slot is Height 0 on Android. A horizontal weight is a width share, and the line still grows.
		if (!widthShare && element.Style is { Grow: > 0 } && HoldsText(element))
			fontScale = 1f;
		switch (element)
		{
			case Text text:
				var size = text.Style?.FontSize ?? 0;
				var line = Scale((size > 0 ? size : 16) + 8, fontScale);
				// AndroidHost.SetGesture calls TouchTarget, HostPolicy.TouchFloorDp,
				// when the gesture mask is nonzero. A long-press name is that view.
				if (text.Gesture != 0 || text.OnTap is not null || text.OnLongPress is not null || text.OnSwipe is not null || text.OnRefresh is not null || text.OnUndo is not null)
				{
					var floor = Scale(HostPolicy.TouchFloorDp, fontScale);
					return line > floor ? line : floor;
				}

				return line;
			case TextField:
			case TextInput:
			case Switch:
			case HostView:
				return Scale(HostPolicy.TouchFloorDp, fontScale);
			case Image image:
				return image.Side > 0 ? image.Side : 96;
			case ListView:
				return 240;
			case Column:
			case ScrollView:
				return 0;
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static bool HoldsText(Element element) => element is Text or Button or TextField or TextInput or Switch;

	static int Scale(int unit, float fontScale) =>
		fontScale == 1f ? unit : (int)Math.Ceiling(unit * fontScale);

	static string ChildId(Element child, string prefix, int index)
	{
		if (child.Key is { Length: > 0 } key)
			return prefix.Length == 0 ? key : prefix + "/" + key;
		var ordinal = index.ToString(CultureInfo.InvariantCulture);
		return prefix.Length == 0 ? ordinal : prefix + "/" + ordinal;
	}

	static IReadOnlyList<Element> Children(Element element)
	{
		switch (element)
		{
			case Column column:
				return column.Children;
			case ScrollView scroll:
				return [scroll.Child];
			case ListView list:
				return ListRows.Visible(list);
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
