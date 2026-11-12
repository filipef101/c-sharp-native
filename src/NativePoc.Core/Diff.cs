namespace NativePoc;

internal readonly record struct NodePath(byte Length, int S0 = 0, int S1 = 0, int S2 = 0, int S3 = 0)
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

internal abstract record Patch
{
	private protected Patch()
	{
	}
}

internal sealed record Mount(NodePath Path, ElementKind Kind) : Patch;

internal sealed record Replace(NodePath Path, ElementKind Kind) : Patch;

internal sealed record Unmount(NodePath Path) : Patch;

internal sealed record SetText(NodePath Path, string Value) : Patch;

internal sealed record SetButton(NodePath Path, string Title, string AccessibilityLabel) : Patch;

internal sealed record SetTextField(NodePath Path, string Value, string Placeholder, string AccessibilityLabel) : Patch;

internal sealed record SetFieldMode(NodePath Path, Keyboard Keyboard, int Lines, Submit Submit) : Patch;

internal sealed record SetSwitch(NodePath Path, bool Value, string AccessibilityLabel) : Patch;

internal sealed record Reorder(NodePath Parent, IReadOnlyList<int> Sources) : Patch;

internal sealed record SetList(NodePath Path, IReadOnlyList<Element> Rows) : Patch;

internal sealed record SetStyle(NodePath Path, Style Style) : Patch;

internal sealed record SetMotion(NodePath Path, Motion Motion) : Patch;

internal sealed record SetGesture(NodePath Path, byte Mask) : Patch;

internal sealed record SetImage(NodePath Path, string Source, string Description, bool Decorative) : Patch;

internal sealed record SetHost(NodePath Path, string Kind, string Prop, string Value, string Event, Action<string, string>? OnEvent) : Patch;

internal static class TreeDiff
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

		if (ReferenceEquals(previous, next))
			return;

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

	enum ChildKeys
	{
		Empty,
		Plain,
		Keyed,
		Mixed,
	}

	static void DiffChildren(
		IReadOnlyList<Element> previous,
		IReadOnlyList<Element> next,
		NodePath path,
		List<Patch> patches)
	{
		var previousMode = KeyMode(previous);
		var nextMode = KeyMode(next);
		// A parent that mixes keyed and unkeyed children is an invalid tree.
		// A parent that is entirely keyed diffed against one that is entirely
		// unkeyed is a valid edit: the warm column has no keys, and Screen.cs
		// keys every child. Replace those children instead of refusing.
		if (previousMode == ChildKeys.Mixed || nextMode == ChildKeys.Mixed)
			throw new InvalidOperationException("Keyed children cannot be mixed with unkeyed children.");
		if (previousMode == ChildKeys.Plain && nextMode == ChildKeys.Keyed
			|| previousMode == ChildKeys.Keyed && nextMode == ChildKeys.Plain)
		{
			ReplaceChildren(previous, next, path, patches);
			return;
		}

		if (previousMode == ChildKeys.Keyed || nextMode == ChildKeys.Keyed)
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

	static ChildKeys KeyMode(IReadOnlyList<Element> children)
	{
		if (children.Count == 0)
			return ChildKeys.Empty;
		var keyed = 0;
		foreach (var child in children)
		{
			if (child.Key is not null)
				keyed++;
		}

		if (keyed == 0)
			return ChildKeys.Plain;
		if (keyed == children.Count)
			return ChildKeys.Keyed;
		return ChildKeys.Mixed;
	}

	static void ReplaceChildren(
		IReadOnlyList<Element> previous,
		IReadOnlyList<Element> next,
		NodePath path,
		List<Patch> patches)
	{
		for (var i = previous.Count - 1; i >= 0; i--)
			patches.Add(new Unmount(path.Child(i)));
		for (var i = 0; i < next.Count; i++)
			MountTree(next[i], path.Child(i), patches);
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
		if (next.Style is Style style && previous?.Style != style)
			patches.Add(new SetStyle(path, style));
		if (next.Motion != previous?.Motion)
		{
			if (next.Motion is Motion motion)
				patches.Add(new SetMotion(path, motion));
			else if (previous?.Motion is not null)
				patches.Add(new SetMotion(path, Motion.Rest));
		}

		var previousMask = MaskOf(previous);
		var nextMask = MaskOf(next);
		if (previousMask != nextMask)
			patches.Add(new SetGesture(path, nextMask));
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
				PatchField(patches, path, previous, field.Value, field.Placeholder, field.AccessibilityLabel, field.Keyboard, field.Lines, field.Submit);
				return;
			case TextInput input:
				PatchField(patches, path, previous, input.Value, input.Placeholder, input.AccessibilityLabel, input.Keyboard, input.Lines, input.Submit);
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
			case ListView list:
				patches.Add(new SetList(path, list.Rows));
				return;
			case Image image:
				if (previous is not Image previousImage
					|| previousImage.Source != image.Source
					|| previousImage.Description != image.Description
					|| previousImage.Decorative != image.Decorative)
				{
					patches.Add(new SetImage(path, image.Source, image.Description, image.Decorative));
				}
				return;
			case HostView host:
				patches.Add(new SetHost(path, host.Kind, host.Prop, host.Value, host.Event, host.OnEvent));
				return;
			default:
				throw UnknownElement(next);
		}
	}

	static void PatchField(
		List<Patch> patches,
		NodePath path,
		Element? previous,
		string value,
		string placeholder,
		string label,
		Keyboard keyboard,
		int lines,
		Submit submit)
	{
		var sameText = previous switch
		{
			TextField field => field.Value == value && field.Placeholder == placeholder && field.AccessibilityLabel == label,
			TextInput input => input.Value == value && input.Placeholder == placeholder && input.AccessibilityLabel == label,
			_ => false,
		};
		if (!sameText)
			patches.Add(new SetTextField(path, value, placeholder, label));
		var sameMode = previous switch
		{
			TextField field => field.Keyboard == keyboard && field.Lines == lines && field.Submit == submit,
			TextInput input => input.Keyboard == keyboard && input.Lines == lines && input.Submit == submit,
			_ => false,
		};
		if (!sameMode && (keyboard != Keyboard.Text || lines > 1 || submit != Submit.None))
			patches.Add(new SetFieldMode(path, keyboard, lines, submit));
	}

	public static byte MaskOf(Element? element)
	{
		if (element is null)
			return 0;
		var mask = element.Gesture;
		if (element.OnTap is not null)
			mask |= Gestures.Tap;
		if (element.OnLongPress is not null)
			mask |= Gestures.LongPress;
		if (element.OnSwipe is not null)
			mask |= Gestures.Swipe;
		if (element.OnRefresh is not null)
			mask |= Gestures.Refresh;
		if (element.OnUndo is not null)
			mask |= Gestures.Undo;
		return mask;
	}

	static IReadOnlyList<Element> ChildrenOf(Element element)
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
			case TextInput:
				return ElementKind.TextField;
			case Switch:
				return ElementKind.Switch;
			case ScrollView:
				return ElementKind.Scroll;
			case ListView:
				return ElementKind.List;
			case Image:
				return ElementKind.Image;
			case HostView:
				return ElementKind.Host;
			default:
				throw UnknownElement(element);
		}
	}

	static InvalidOperationException UnknownElement(Element element) =>
		new("Unknown element.");
}
