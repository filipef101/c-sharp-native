namespace NativePoc;

public readonly record struct NodePath(byte Length, int S0 = 0, int S1 = 0, int S2 = 0, int S3 = 0)
{
	public static NodePath Root { get; } = new(0);

	public NodePath Child(int index)
	{
		return Length switch
		{
			0 => new NodePath(1, index),
			1 => new NodePath(2, S0, index),
			2 => new NodePath(3, S0, S1, index),
			3 => new NodePath(4, S0, S1, S2, index),
			_ => throw new InvalidOperationException("Element path is deeper than 4."),
		};
	}

	public NodePath Parent()
	{
		return Length switch
		{
			1 => Root,
			2 => new NodePath(1, S0),
			3 => new NodePath(2, S0, S1),
			4 => new NodePath(3, S0, S1, S2),
			_ => throw new InvalidOperationException("Root has no parent."),
		};
	}

	public int LastIndex()
	{
		return Length switch
		{
			1 => S0,
			2 => S1,
			3 => S2,
			4 => S3,
			_ => throw new InvalidOperationException("Root has no index."),
		};
	}

	public int Segment(int index)
	{
		return index switch
		{
			0 => S0,
			1 => S1,
			2 => S2,
			3 => S3,
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		};
	}

	public bool IsDescendantOf(NodePath ancestor)
	{
		if (Length <= ancestor.Length)
			return false;
		for (var i = 0; i < ancestor.Length; i++)
		{
			if (Segment(i) != ancestor.Segment(i))
				return false;
		}
		return true;
	}
}

public abstract record Patch
{
	private protected Patch()
	{
	}
}

public sealed record Mount(NodePath Path, ElementKind Kind) : Patch;

public sealed record Replace(NodePath Path, ElementKind Kind) : Patch;

public sealed record Unmount(NodePath Path) : Patch;

public sealed record SetText(NodePath Path, string Value) : Patch;

public sealed record SetButton(NodePath Path, string Title, string AccessibilityLabel) : Patch;

public sealed record SetTextField(NodePath Path, string Value, string Placeholder, string AccessibilityLabel) : Patch;

public sealed record SetSwitch(NodePath Path, bool Value, string AccessibilityLabel) : Patch;

public sealed record Reorder(NodePath Parent, IReadOnlyList<int> Sources) : Patch;

public static class TreeDiff
{
	public static List<Patch> Diff(Element? previous, Element next)
	{
		ArgumentNullException.ThrowIfNull(next);
		var patches = new List<Patch>();
		DiffNode(previous, next, NodePath.Root, patches);
		return patches;
	}

	public static Element At(Element root, NodePath path)
	{
		var current = root;
		for (var i = 0; i < path.Length; i++)
		{
			if (current is not Column and not ScrollView)
				throw new InvalidOperationException("Path crosses a leaf.");
			var children = ChildrenOf(current);
			var index = path.Segment(i);
			if ((uint)index >= (uint)children.Count)
				throw new ArgumentOutOfRangeException(nameof(path));
			current = children[index];
		}
		return current;
	}

	static void DiffNode(Element? previous, Element next, NodePath path, List<Patch> patches)
	{
		if (previous is null)
		{
			MountTree(next, path, patches);
			return;
		}

		var previousKind = KindOf(previous);
		var nextKind = KindOf(next);
		if (previousKind != nextKind)
		{
			patches.Add(new Replace(path, nextKind));
			ApplyProps(null, next, path, patches);
			DiffChildren(Array.Empty<Element>(), ChildrenOf(next), path, patches);
			return;
		}

		ApplyProps(previous, next, path, patches);
		DiffChildren(ChildrenOf(previous), ChildrenOf(next), path, patches);
	}

	static void MountTree(Element next, NodePath path, List<Patch> patches)
	{
		patches.Add(new Mount(path, KindOf(next)));
		ApplyProps(null, next, path, patches);
		DiffChildren(Array.Empty<Element>(), ChildrenOf(next), path, patches);
	}

	static void DiffChildren(
		IReadOnlyList<Element> previous,
		IReadOnlyList<Element> next,
		NodePath path,
		List<Patch> patches)
	{
		if (UsesKeys(previous) || UsesKeys(next))
		{
			DiffKeyedChildren(previous, next, path, patches);
			return;
		}

		var shared = previous.Count < next.Count ? previous.Count : next.Count;
		for (var i = 0; i < shared; i++)
			DiffNode(previous[i], next[i], path.Child(i), patches);
		for (var i = previous.Count - 1; i >= shared; i--)
			patches.Add(new Unmount(path.Child(i)));
		for (var i = shared; i < next.Count; i++)
			MountTree(next[i], path.Child(i), patches);
	}

	static bool UsesKeys(IReadOnlyList<Element> children)
	{
		foreach (var child in children)
		{
			if (child.Key is not null)
				return true;
		}
		return false;
	}

	static void DiffKeyedChildren(
		IReadOnlyList<Element> previous,
		IReadOnlyList<Element> next,
		NodePath path,
		List<Patch> patches)
	{
		RequireKeys(previous);
		RequireKeys(next);
		var oldIndex = new Dictionary<string, int>(previous.Count);
		for (var i = 0; i < previous.Count; i++)
		{
			var key = previous[i].Key!;
			if (!oldIndex.TryAdd(key, i))
				throw new InvalidOperationException("Duplicate child key.");
		}
		var seen = new HashSet<string>(next.Count);
		var sources = new int[next.Count];
		for (var i = 0; i < next.Count; i++)
		{
			var key = next[i].Key!;
			if (!seen.Add(key))
				throw new InvalidOperationException("Duplicate child key.");
			sources[i] = oldIndex.TryGetValue(key, out var index) ? index : -1;
		}

		if (previous.Count == 0)
		{
			for (var i = 0; i < next.Count; i++)
				MountTree(next[i], path.Child(i), patches);
			return;
		}

		var moved = previous.Count != next.Count;
		if (!moved)
		{
			for (var i = 0; i < sources.Length; i++)
			{
				if (sources[i] != i)
				{
					moved = true;
					break;
				}
			}
		}
		if (moved)
			patches.Add(new Reorder(path, sources));

		for (var i = 0; i < next.Count; i++)
		{
			if (sources[i] < 0)
				MountTree(next[i], path.Child(i), patches);
			else
				DiffNode(previous[sources[i]], next[i], path.Child(i), patches);
		}
	}

	static void RequireKeys(IReadOnlyList<Element> children)
	{
		foreach (var child in children)
		{
			if (child.Key is null)
				throw new InvalidOperationException("Keyed children cannot be mixed with unkeyed children.");
		}
	}

	static void ApplyProps(Element? previous, Element next, NodePath path, List<Patch> patches)
	{
		switch (next)
		{
			case Column:
				return;
			case Text text:
				if (previous is not Text previousText || previousText.Value != text.Value)
					patches.Add(new SetText(path, text.Value));
				return;
			case Button button:
				if (previous is not Button previousButton
					|| previousButton.Title != button.Title
					|| previousButton.AccessibilityLabel != button.AccessibilityLabel)
				{
					patches.Add(new SetButton(path, button.Title, button.AccessibilityLabel));
				}
				return;
			case TextField field:
				if (previous is not TextField previousField
					|| previousField.Value != field.Value
					|| previousField.Placeholder != field.Placeholder
					|| previousField.AccessibilityLabel != field.AccessibilityLabel)
				{
					patches.Add(new SetTextField(path, field.Value, field.Placeholder, field.AccessibilityLabel));
				}
				return;
			case Switch toggle:
				if (previous is not Switch previousToggle
					|| previousToggle.Value != toggle.Value
					|| previousToggle.AccessibilityLabel != toggle.AccessibilityLabel)
				{
					patches.Add(new SetSwitch(path, toggle.Value, toggle.AccessibilityLabel));
				}
				return;
			case ScrollView:
				return;
			default:
				throw UnknownElement(next);
		}
	}

	static IReadOnlyList<Element> ChildrenOf(Element element)
	{
		switch (element)
		{
			case Column column:
				return column.Children;
			case ScrollView scroll:
				return [scroll.Child];
			case Text:
			case Button:
			case TextField:
			case Switch:
				return Array.Empty<Element>();
			default:
				throw UnknownElement(element);
		}
	}

	public static ElementKind KindOf(Element element)
	{
		switch (element)
		{
			case Column:
				return ElementKind.Column;
			case Text:
				return ElementKind.Text;
			case Button:
				return ElementKind.Button;
			case TextField:
				return ElementKind.TextField;
			case Switch:
				return ElementKind.Switch;
			case ScrollView:
				return ElementKind.Scroll;
			default:
				throw UnknownElement(element);
		}
	}

	static InvalidOperationException UnknownElement(Element element) =>
		new("Unknown element.");
}
