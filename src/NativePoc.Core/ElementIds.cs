using System.Globalization;

namespace NativePoc;

public static class ElementIds
{
	public readonly record struct Node(string Id, int Depth, Element Element);

	public static IReadOnlyList<Node> Flatten(Element root)
	{
		ArgumentNullException.ThrowIfNull(root);
		var nodes = new List<Node>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var id = root.Key is { Length: > 0 } key ? key : "root";
		Walk(root, id, 0, nodes, seen);
		return nodes;
	}

	public static Element Require(Element root, string id)
	{
		foreach (var node in Flatten(root))
		{
			if (string.Equals(node.Id, id, StringComparison.Ordinal))
				return node.Element;
		}

		throw new InvalidOperationException("No element id " + id + ".");
	}

	public static string Invoke(Element root, string id, string? text)
	{
		var element = Require(root, id);
		switch (element)
		{
			case Button button when text is null:
				button.OnPress();
				return "pressed " + id;
			case Switch toggle when text is null:
				toggle.OnChange(!toggle.Value);
				return "pressed " + id;
			case TextField field when text is not null:
				field.OnChange(text);
				return "entered " + id;
			case Button:
			case Switch:
			case TextField:
				throw new InvalidOperationException("Element " + id + " does not take that input.");
			case Column:
			case Text:
				throw new InvalidOperationException("Element " + id + " does not take input.");
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static void Walk(Element element, string id, int depth, List<Node> nodes, HashSet<string> seen)
	{
		if (!seen.Add(id))
			throw new InvalidOperationException("Duplicate element id " + id + ".");
		nodes.Add(new Node(id, depth, element));
		if (element is not Column column)
			return;
		var prefix = id == "root" ? "" : id;
		for (var index = 0; index < column.Children.Count; index++)
		{
			var child = column.Children[index];
			var childId = child.Key is { Length: > 0 } key
				? (prefix.Length == 0 ? key : prefix + "/" + key)
				: prefix.Length == 0
					? index.ToString(CultureInfo.InvariantCulture)
					: prefix + "/" + index.ToString(CultureInfo.InvariantCulture);
			Walk(child, childId, depth + 1, nodes, seen);
		}
	}
}
