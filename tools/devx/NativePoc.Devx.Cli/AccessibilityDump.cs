using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class AccessibilityDump
{
	public readonly record struct Report(string Text, int Violations);

	public static Report Check(string xml, int dpi, float fontScale)
	{
		if (dpi <= 0)
			dpi = 160;
		if (fontScale <= 0)
			fontScale = 1;
		var minPx = 48.0 * dpi / 160.0;
		var linePx = 16.0 * fontScale * dpi / 160.0 * 0.85;
		var nodes = Nodes(xml);
		var clipBottoms = new HashSet<int>();
		foreach (var node in nodes)
		{
			if (node.Class.EndsWith("ScrollView", StringComparison.Ordinal))
				clipBottoms.Add(node.Bottom);
		}

		var builder = new StringBuilder();
		builder.Append("density=").Append(dpi.ToString(CultureInfo.InvariantCulture)).Append('\n');
		builder.Append("font-scale=").Append(fontScale.ToString(CultureInfo.InvariantCulture)).Append('\n');
		var violations = 0;
		var headings = 0;
		foreach (var node in nodes)
		{
			if (node.Heading)
				headings++;
			if (node.Width <= 0 || node.Height <= 0)
				continue;
			if (CutByScroll(node, clipBottoms, minPx))
				continue;
			if (node.Touchable && (node.Width < minPx || node.Height < minPx))
			{
				violations++;
				builder.Append("violation touch-target class=").Append(Quote(node.Class));
				builder.Append(" bounds=").Append(Quote(node.Bounds)).Append('\n');
			}

			if (node.Touchable && node.Label.Length == 0 && node.Text.Length == 0 && IsControl(node.Class))
			{
				violations++;
				builder.Append("violation unlabeled class=").Append(Quote(node.Class));
				builder.Append(" bounds=").Append(Quote(node.Bounds)).Append('\n');
			}

			if (node.Class.EndsWith("TextView", StringComparison.Ordinal) && node.Text.Length > 0 && node.Height < linePx)
			{
				violations++;
				builder.Append("violation clipped class=").Append(Quote(node.Class));
				builder.Append(" text=").Append(Quote(node.Text));
				builder.Append(" bounds=").Append(Quote(node.Bounds)).Append('\n');
			}
		}

		A11yNode? previous = null;
		foreach (var node in nodes)
		{
			if (!node.Leaf || node.Height <= 0 || Decor(node))
				continue;
			if (previous is { } prior
				&& Overlaps(prior, node)
				&& node.Top + 8 < prior.Top)
			{
				violations++;
				builder.Append("violation order class=").Append(Quote(node.Class));
				builder.Append(" text=").Append(Quote(node.Text));
				builder.Append(" bounds=").Append(Quote(node.Bounds)).Append('\n');
			}

			previous = node;
		}

		builder.Append("headings=").Append(headings.ToString(CultureInfo.InvariantCulture)).Append('\n');
		if (xml.IndexOf("heading=", StringComparison.Ordinal) < 0)
			builder.Append("heading-attribute=absent\n");
		builder.Append("violations=").Append(violations.ToString(CultureInfo.InvariantCulture)).Append('\n');
		return new Report(builder.ToString(), violations);
	}

	static bool CutByScroll(A11yNode node, HashSet<int> clipBottoms, double minPx) =>
		clipBottoms.Contains(node.Bottom) && node.Width >= minPx && node.Height < minPx;

	static bool Decor(A11yNode node) =>
		node.Class == "android.view.View" && node.Text.Length == 0 && node.Label.Length == 0;

	static bool IsControl(string className) =>
		className.EndsWith("Button", StringComparison.Ordinal)
		|| className.EndsWith("Switch", StringComparison.Ordinal)
		|| className.EndsWith("ImageView", StringComparison.Ordinal)
		|| className.EndsWith("EditText", StringComparison.Ordinal)
		|| className.EndsWith("CheckBox", StringComparison.Ordinal)
		|| className.EndsWith("TextView", StringComparison.Ordinal);

	static bool Overlaps(A11yNode left, A11yNode right) => left.Left < right.Right && right.Left < left.Right;
	public static string Read(string serial)
	{
		var dumped = AndroidLoop.Adb(serial, "shell", "uiautomator", "dump", "/sdcard/window_dump.xml");
		if (dumped.Code != 0)
			throw new InvalidOperationException("uiautomator dump failed: " + dumped.Stderr + dumped.Stdout);
		var read = AndroidLoop.Adb(serial, "shell", "cat", "/sdcard/window_dump.xml");
		if (read.Code != 0 || read.Stdout.Length == 0)
			throw new InvalidOperationException("uiautomator dump was empty: " + read.Stderr + read.Stdout);
		return read.Stdout;
	}

	public static string Summarize(string xml)
	{
		var builder = new StringBuilder();
		var cursor = 0;
		while (cursor < xml.Length)
		{
			var node = xml.IndexOf("<node ", cursor, StringComparison.Ordinal);
			if (node < 0)
				break;
			var end = xml.IndexOf('>', node);
			if (end < 0)
				break;
			var piece = xml[node..end];
			builder.Append("class=").Append(Attr(piece, "class"));
			builder.Append(" desc=").Append(Quote(Attr(piece, "content-desc")));
			builder.Append(" text=").Append(Quote(Attr(piece, "text")));
			builder.Append(" bounds=").Append(Attr(piece, "bounds"));
			builder.Append('\n');
			cursor = end + 1;
		}

		return builder.ToString();
	}

	static string Attr(string node, string name)
	{
		var needle = name + "=\"";
		var start = node.IndexOf(needle, StringComparison.Ordinal);
		if (start < 0)
			return "";
		start += needle.Length;
		var end = node.IndexOf('"', start);
		if (end < 0)
			return "";
		return node[start..end];
	}

	static string Quote(string value) =>
		"\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

	static List<A11yNode> Nodes(string xml)
	{
		var nodes = new List<A11yNode>();
		var cursor = 0;
		while (cursor < xml.Length)
		{
			var node = xml.IndexOf("<node ", cursor, StringComparison.Ordinal);
			if (node < 0)
				break;
			var end = xml.IndexOf('>', node);
			if (end < 0)
				break;
			var self = end > node && xml[end - 1] == '/';
			var leaf = self || ClosesImmediately(xml, end);
			var piece = xml[node..end];
			if (TryNode(piece, leaf, out var parsed))
				nodes.Add(parsed);
			cursor = end + 1;
		}

		return nodes;
	}

	static bool ClosesImmediately(string xml, int end)
	{
		var next = end + 1;
		while (next < xml.Length && char.IsWhiteSpace(xml[next]))
			next++;
		return next < xml.Length && xml.AsSpan(next).StartsWith("</node", StringComparison.Ordinal);
	}

	static bool TryNode(string piece, bool self, out A11yNode node)
	{
		node = default;
		var bounds = Attr(piece, "bounds");
		if (!TryBounds(bounds, out var left, out var top, out var right, out var bottom))
			return false;
		var clickable = Attr(piece, "clickable") == "true";
		var longClickable = Attr(piece, "long-clickable") == "true";
		node = new A11yNode(
			Attr(piece, "class"),
			Attr(piece, "text"),
			Attr(piece, "content-desc"),
			bounds,
			left,
			top,
			right,
			bottom,
			clickable || longClickable,
			Attr(piece, "heading") == "true",
			self);
		return true;
	}

	static bool TryBounds(string text, out int left, out int top, out int right, out int bottom)
	{
		left = top = right = bottom = 0;
		var split = text.Split('[', ']', ',');
		var numbers = new List<int>(4);
		foreach (var piece in split)
		{
			if (piece.Length == 0)
				continue;
			if (!int.TryParse(piece, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
				return false;
			numbers.Add(value);
		}

		if (numbers.Count != 4)
			return false;
		left = numbers[0];
		top = numbers[1];
		right = numbers[2];
		bottom = numbers[3];
		return true;
	}

	readonly record struct A11yNode(
		string Class,
		string Text,
		string Label,
		string Bounds,
		int Left,
		int Top,
		int Right,
		int Bottom,
		bool Touchable,
		bool Heading,
		bool Leaf)
	{
		public int Width => Right - Left;

		public int Height => Bottom - Top;
	}
}
