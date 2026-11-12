using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class ScreenInspect
{
	public static string FormatFile(string path)
	{
		var full = Path.GetFullPath(path);
		var dir = Path.GetDirectoryName(full) ?? "";
		var companions = Directory.Exists(dir)
			? Directory.GetFiles(dir, "*.cs").Where(file => !string.Equals(file, full, StringComparison.Ordinal)).Select(File.ReadAllText).ToArray()
			: [];
		var source = File.ReadAllText(full);
		var lowered = SubsetLowerer.TryLower(source, companions, originFile: full);
		if (!lowered.Ok)
			lowered = SubsetLowerer.TryLower(source, originFile: full);
		if (!lowered.Ok || lowered.Program is null)
			throw new InvalidOperationException(lowered.Diagnostics.Count == 0 ? "The screen did not lower." : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
		var host = new HeadlessHost();
		var root = host.Apply(lowered.Program, new RecordProgram.Bag());
		return Format(root);
	}

	public const int SystemBarDp = 24;

	public static string Format(Element root)
	{
		ArgumentNullException.ThrowIfNull(root);
		return Format(root, BelowBars(HeadlessLayout.Measure(root)), BelowBars(HeadlessLayout.Measure(root, 2f)));
	}

	static List<HeadlessLayout.Box> BelowBars(List<HeadlessLayout.Box> boxes)
	{
		var shifted = new List<HeadlessLayout.Box>(boxes.Count);
		foreach (var box in boxes)
			shifted.Add(box with { Top = box.Top + SystemBarDp });
		return shifted;
	}

	public static string Format(Element root, IReadOnlyList<HeadlessLayout.Box> boxes) =>
		Format(root, boxes, null);

	static string Format(Element root, IReadOnlyList<HeadlessLayout.Box> boxes, IReadOnlyList<HeadlessLayout.Box>? scaled)
	{
		var nodes = ElementIds.Flatten(root);
		var boxById = new Dictionary<string, HeadlessLayout.Box>(StringComparer.Ordinal);
		foreach (var box in boxes)
			boxById[box.Id] = box;
		Dictionary<string, HeadlessLayout.Box>? scaledById = null;
		if (scaled is not null)
		{
			scaledById = new Dictionary<string, HeadlessLayout.Box>(StringComparer.Ordinal);
			foreach (var box in scaled)
				scaledById[box.Id] = box;
		}

		var findings = Lint(nodes, boxById, scaledById);
		var builder = new StringBuilder();
		builder.Append("{\n  \"accessibility\": [\n");
		for (var i = 0; i < nodes.Count; i++)
		{
			var node = nodes[i];
			builder.Append("    {\"heading\":");
			builder.Append(IsHeading(node.Element) ? "true" : "false");
			builder.Append(",\"id\":");
			Quote(builder, node.Id);
			builder.Append(",\"label\":");
			Quote(builder, Label(node.Element));
			builder.Append(",\"role\":");
			Quote(builder, ScreenSnapshot.Role(node.Element));
			builder.Append(",\"text\":");
			Quote(builder, Text(node.Element));
			builder.Append('}');
			if (i + 1 < nodes.Count)
				builder.Append(',');
			builder.Append('\n');
		}

		builder.Append("  ],\n  \"layout\": [\n");
		for (var i = 0; i < boxes.Count; i++)
		{
			var box = boxes[i];
			builder.Append("    {\"height\":");
			builder.Append(box.Height.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"id\":");
			Quote(builder, box.Id);
			builder.Append(",\"left\":");
			builder.Append(box.Left.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"top\":");
			builder.Append(box.Top.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"width\":");
			builder.Append(box.Width.ToString(CultureInfo.InvariantCulture));
			builder.Append('}');
			if (i + 1 < boxes.Count)
				builder.Append(',');
			builder.Append('\n');
		}

		builder.Append("  ],\n  \"lint\": [\n");
		for (var i = 0; i < findings.Count; i++)
		{
			var finding = findings[i];
			builder.Append("    {\"code\":");
			Quote(builder, finding.Code);
			builder.Append(",\"id\":");
			Quote(builder, finding.Id);
			builder.Append(",\"message\":");
			Quote(builder, finding.Message);
			builder.Append('}');
			if (i + 1 < findings.Count)
				builder.Append(',');
			builder.Append('\n');
		}

		builder.Append("  ],\n  \"tree\": [\n");
		for (var i = 0; i < nodes.Count; i++)
		{
			var node = nodes[i];
			builder.Append("    {\"id\":");
			Quote(builder, node.Id);
			builder.Append(",\"label\":");
			Quote(builder, Label(node.Element));
			builder.Append(",\"role\":");
			Quote(builder, ScreenSnapshot.Role(node.Element));
			builder.Append(",\"text\":");
			Quote(builder, Text(node.Element));
			builder.Append('}');
			if (i + 1 < nodes.Count)
				builder.Append(',');
			builder.Append('\n');
		}

		builder.Append("  ]\n}\n");
		return builder.ToString();
	}

	public static int Violations(string json) => Count(json, "\"code\":");

	static List<Finding> Lint(
		IReadOnlyList<ElementIds.Node> nodes,
		Dictionary<string, HeadlessLayout.Box> boxes,
		Dictionary<string, HeadlessLayout.Box>? scaled)
	{
		var findings = new List<Finding>();
		var byPath = new Dictionary<NodePath, ElementIds.Node>(nodes.Count);
		foreach (var node in nodes)
			byPath[node.Path] = node;
		var headings = 0;
		string? previousId = null;
		var previousTop = 0;
		foreach (var node in nodes)
		{
			if (IsHeading(node.Element))
				headings++;
			if (node.Element is ListView list)
			{
				foreach (var row in list.Rows)
				{
					if (IsHeading(row))
						headings++;
				}
			}
			if (MissingLabel(node.Element))
				findings.Add(new Finding("missing-label", node.Id, "Missing label on " + ScreenSnapshot.Role(node.Element) + " " + node.Id + "."));
			if (Contrast(node, nodes, byPath) is { } contrast)
				findings.Add(contrast);
			if (ImageAccess(node.Element, node.Id) is { } imageAccess)
				findings.Add(imageAccess);
			if (FixedHeight(node, nodes, boxes, scaled) is { } fixedHeight)
				findings.Add(fixedHeight);
			if (node.Path.Length > 0 && byPath.TryGetValue(node.Path.Parent(), out var parent))
			{
				if (Overflow(node.Id, parent.Id, boxes, scaled: false) is { } overflow)
					findings.Add(overflow);
				else if (scaled is not null && Overflow(node.Id, parent.Id, scaled, scaled: true) is { } grown)
					findings.Add(grown);
				if (UnderBars(node.Id, boxes, scaled: false) is { } under)
					findings.Add(under);
				else if (scaled is not null && UnderBars(node.Id, scaled, scaled: true) is { } grownUnder)
					findings.Add(grownUnder);
			}
			if (IsTouch(node.Element))
			{
				if (!boxes.TryGetValue(node.Id, out var box) || box.Width < 48 || box.Height < 48)
				{
					var width = boxes.TryGetValue(node.Id, out box) ? box.Width : 0;
					var height = boxes.TryGetValue(node.Id, out box) ? box.Height : 0;
					findings.Add(new Finding(
						"touch-target",
						node.Id,
						"Touch target " + node.Id + " is " + width.ToString(CultureInfo.InvariantCulture) + " by " + height.ToString(CultureInfo.InvariantCulture) + " dp, under 48 dp."));
				}
			}

			if (!IsFocusable(node.Element) || !boxes.TryGetValue(node.Id, out var placed))
				continue;
			if (previousId is not null && placed.Top + 8 < previousTop)
				findings.Add(new Finding(
					"focus-order",
					node.Id,
					"Focus reaches " + node.Id + " above " + previousId + "."));
			previousId = node.Id;
			previousTop = placed.Top;
		}

		if (headings == 0)
			findings.Insert(0, new Finding("missing-heading", "", "The screen has no heading."));
		return findings;
	}

	static Finding? Contrast(
		ElementIds.Node node,
		IReadOnlyList<ElementIds.Node> nodes,
		Dictionary<NodePath, ElementIds.Node> byPath)
	{
		if (node.Element is not (NativePoc.Text or Button or TextField or TextInput or Switch))
			return null;
		var style = node.Element.Style ?? default;
		var color = style.Color != 0 ? style.Color : ThemeTokens.Text(false);
		var background = FirstBackground(node, nodes, byPath);
		if (ChromeFill.Legible(color, background, style.FontSize, style.Weight))
			return null;
		return new Finding("contrast", node.Id, "Text " + node.Id + " is below contrast on its first-frame background.");
	}

	static uint FirstBackground(
		ElementIds.Node node,
		IReadOnlyList<ElementIds.Node> nodes,
		Dictionary<NodePath, ElementIds.Node> byPath)
	{
		var current = node;
		while (true)
		{
			if (current.Element.Style is { } style && (style.Background != 0 || style.Wash != 0))
			{
				var ink = new List<ChromeInk>();
				foreach (var other in nodes)
				{
					if (other.Path.Equals(current.Path) || other.Path.IsDescendantOf(current.Path))
						AddInk(other.Element, ink);
				}

				return ChromeFill.FirstFrame(style, ink);
			}

			if (current.Path.Length == 0 || !byPath.TryGetValue(current.Path.Parent(), out var parent))
				return ThemeTokens.Page(false);
			current = parent;
		}
	}

	static void AddInk(Element element, List<ChromeInk> ink)
	{
		if (element is not (NativePoc.Text or Button or TextField or TextInput or Switch))
			return;
		var style = element.Style ?? default;
		var color = style.Color != 0 ? style.Color : ThemeTokens.Text(false);
		ink.Add(new ChromeInk(color, style.FontSize, style.Weight));
	}

	static Finding? FixedHeight(
		ElementIds.Node node,
		IReadOnlyList<ElementIds.Node> nodes,
		Dictionary<string, HeadlessLayout.Box> boxes,
		Dictionary<string, HeadlessLayout.Box>? scaled)
	{
		var element = node.Element;
		var id = node.Id;
		if (element.Style is { Grow: > 0 } && ContainsText(element) && !WidthShare(node, nodes))
			return new Finding("fixed-height", id, "Text in " + id + " is in a fixed-height container.");
		if (scaled is null || element is not (NativePoc.Text or Button or TextField or TextInput or Switch))
			return null;
		if (!boxes.TryGetValue(id, out var box) || !scaled.TryGetValue(id, out var grown))
			return null;
		if (grown.Height > box.Height)
			return null;
		return new Finding(
			"fixed-height",
			id,
			"Text in " + id + " stays " + box.Height.ToString(CultureInfo.InvariantCulture) + " dp at font scale 2.");
	}

	static Finding? UnderBars(string id, Dictionary<string, HeadlessLayout.Box> boxes, bool scaled)
	{
		if (!boxes.TryGetValue(id, out var box) || box.Top >= SystemBarDp)
			return null;
		var where = scaled ? " at font scale 2" : "";
		return new Finding(
			"system-bar",
			id,
			"Child " + id + " sits under the system bars" + where + ".");
	}

	static Finding? Overflow(string id, string parentId, Dictionary<string, HeadlessLayout.Box> boxes, bool scaled)
	{
		if (!boxes.TryGetValue(id, out var child) || !boxes.TryGetValue(parentId, out var parent))
			return null;
		if (child.Left >= parent.Left
			&& child.Top >= parent.Top
			&& child.Left + child.Width <= parent.Left + parent.Width
			&& child.Top + child.Height <= parent.Top + parent.Height)
			return null;
		var where = scaled ? " at font scale 2" : "";
		return new Finding(
			"overflow",
			id,
			"Child " + id + " extends outside " + parentId + where + " and is clipped.");
	}

	static Finding? ImageAccess(Element element, string id)
	{
		if (element is not Image image)
			return null;
		if (image.Decorative || image.Description.Length > 0)
			return null;
		return new Finding("image-a11y", id, "Image " + id + " needs a description or Decorative = true.");
	}

	static bool WidthShare(ElementIds.Node node, IReadOnlyList<ElementIds.Node> nodes)
	{
		if (node.Path.Length == 0)
			return false;
		var parent = node.Path.Parent();
		foreach (var other in nodes)
		{
			if (other.Path.Equals(parent) && other.Element.Style is { Across: > 0 })
				return true;
		}

		return false;
	}

	static bool ContainsText(Element element)
	{
		switch (element)
		{
			case NativePoc.Text:
			case Button:
			case TextField:
			case TextInput:
			case Switch:
				return true;
			case Column column:
				foreach (var child in column.Children)
				{
					if (ContainsText(child))
						return true;
				}

				return false;
			case ScrollView scroll:
				return ContainsText(scroll.Child);
			case ListView list:
				foreach (var row in list.Rows)
				{
					if (ContainsText(row))
						return true;
				}

				return false;
			case Image:
			case HostView:
				return false;
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static bool IsHeading(Element element) => element is Text { Style: { FontSize: >= 20 } };

	static bool IsTouch(Element element) => element switch
	{
		Button => true,
		TextField => true,
		TextInput => true,
		Switch => true,
		Text text => text.OnTap is not null || text.OnLongPress is not null,
		Image image => image.OnTap is not null,
		Column column => column.OnTap is not null,
		_ => false,
	};

	static bool IsFocusable(Element element) => element switch
	{
		Button => true,
		TextField => true,
		TextInput => true,
		Switch => true,
		Text text => IsHeading(text) || text.OnTap is not null || text.OnLongPress is not null,
		_ => false,
	};

	static bool MissingLabel(Element element) => element switch
	{
		Button button => button.AccessibilityLabel.Length == 0 && button.Title.Length == 0,
		TextField field => field.AccessibilityLabel.Length == 0 && field.Placeholder.Length == 0,
		TextInput field => field.AccessibilityLabel.Length == 0 && field.Placeholder.Length == 0,
		Switch toggle => toggle.AccessibilityLabel.Length == 0,
		_ => false,
	};

	static string Label(Element element) => element switch
	{
		Button button => button.AccessibilityLabel,
		TextField field => field.AccessibilityLabel,
		TextInput field => field.AccessibilityLabel,
		Switch toggle => toggle.AccessibilityLabel,
		_ => "",
	};

	static string Text(Element element) => element switch
	{
		Text text => text.Value,
		Button button => button.Title,
		TextField field => field.Value,
		TextInput field => field.Value,
		Switch toggle => toggle.Value ? "on" : "off",
		Image image => image.Source,
		_ => "",
	};

	static int Count(string json, string needle)
	{
		var count = 0;
		var from = 0;
		while (true)
		{
			var at = json.IndexOf(needle, from, StringComparison.Ordinal);
			if (at < 0)
				return count;
			count++;
			from = at + needle.Length;
		}
	}

	static void Quote(StringBuilder builder, string value)
	{
		builder.Append('"');
		foreach (var ch in value)
		{
			switch (ch)
			{
				case '\\':
				case '"':
					builder.Append('\\');
					builder.Append(ch);
					break;
				case '\n':
					builder.Append("\\n");
					break;
				case '\r':
					builder.Append("\\r");
					break;
				default:
					builder.Append(ch);
					break;
			}
		}

		builder.Append('"');
	}

	readonly record struct Finding(string Code, string Id, string Message);
}
