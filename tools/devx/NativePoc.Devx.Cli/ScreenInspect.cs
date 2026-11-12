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

	public static string Format(Element root) => Format(root, HeadlessLayout.Measure(root));

	public static string Format(Element root, IReadOnlyList<HeadlessLayout.Box> boxes)
	{
		var nodes = ElementIds.Flatten(root);
		var boxById = new Dictionary<string, HeadlessLayout.Box>(StringComparer.Ordinal);
		foreach (var box in boxes)
			boxById[box.Id] = box;
		var findings = Lint(nodes, boxById);
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

	static List<Finding> Lint(IReadOnlyList<ElementIds.Node> nodes, Dictionary<string, HeadlessLayout.Box> boxes)
	{
		var findings = new List<Finding>();
		var headings = 0;
		string? previousId = null;
		var previousTop = 0;
		foreach (var node in nodes)
		{
			if (IsHeading(node.Element))
				headings++;
			if (MissingLabel(node.Element))
				findings.Add(new Finding("missing-label", node.Id, "Missing label on " + ScreenSnapshot.Role(node.Element) + " " + node.Id + "."));
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
