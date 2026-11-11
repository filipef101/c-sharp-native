using System.Globalization;

namespace NativePoc;

public static class ElementIds
{
	public readonly record struct Node(string Id, int Depth, Element Element, NodePath Path);

	public static IReadOnlyList<Node> Flatten(Element root)
	{
		ArgumentNullException.ThrowIfNull(root);
		var nodes = new List<Node>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var id = root.Key is { Length: > 0 } key ? key : "root";
		Walk(root, id, 0, NodePath.Root, nodes, seen);
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

	public static string Submit(Element root, string id)
	{
		if (Require(root, id) is not TextField field || field.OnSubmit is null)
			throw new InvalidOperationException("Element " + id + " has no submit.");
		field.OnSubmit();
		return "submitted " + id;
	}

	public static string Tap(Element root, string id) => Fire(root, id, static element => element.OnTap, "tap");

	public static string LongPress(Element root, string id) => Fire(root, id, static element => element.OnLongPress, "long-press");

	public static string Swipe(Element root, string id) => Fire(root, id, static element => element.OnSwipe, "swipe");

	public static string Refresh(Element root, string id) => Fire(root, id, static element => element.OnRefresh, "refresh");

	public static string Invoke(Element root, string id, string? text)
	{
		TapTrace.Input();
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
			case ScrollView:
			case ListView:
			case Text:
			case Image:
			case HostView:
				throw new InvalidOperationException("Element " + id + " does not take input.");
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static string Fire(Element root, string id, Func<Element, Action?> action, string name)
	{
		var element = Require(root, id);
		var run = action(element);
		if (run is null)
			throw new InvalidOperationException("Element " + id + " has no " + name + ".");
		run();
		return name + " " + id;
	}

	static void Walk(Element element, string id, int depth, NodePath path, List<Node> nodes, HashSet<string> seen)
	{
		if (!seen.Add(id))
			throw new InvalidOperationException("Duplicate element id " + id + ".");
		nodes.Add(new Node(id, depth, element, path));
		var children = Nested(element);
		if (children.Count == 0)
			return;
		var prefix = id == "root" ? "" : id;
		for (var index = 0; index < children.Count; index++)
		{
			var child = children[index];
			var childId = child.Key is { Length: > 0 } key
				? (prefix.Length == 0 ? key : prefix + "/" + key)
				: prefix.Length == 0
					? index.ToString(CultureInfo.InvariantCulture)
					: prefix + "/" + index.ToString(CultureInfo.InvariantCulture);
			Walk(child, childId, depth + 1, path.Child(index), nodes, seen);
		}
	}

	static IReadOnlyList<Element> Nested(Element element)
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
			case Switch:
			case Image:
			case HostView:
				return Array.Empty<Element>();
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}
}
