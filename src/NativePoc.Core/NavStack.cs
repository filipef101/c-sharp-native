namespace NativePoc;

/* The stack a screen platform keeps across a hot reload. Push and pop
   record the heading key of the screen that is now showing. Android back
   is a pop. The view call that moves accessibility focus is NavFocus,
   and that file is compiled only when NativePocNav is set. */
public sealed class NavFocusState
{
	readonly List<string> _stack = new() { "home" };

	public int Depth => _stack.Count;

	public string Focus { get; private set; } = "";

	public bool Push(string screen)
	{
		if (screen.Length == 0)
			return false;
		if (_stack.Count > 0 && string.Equals(_stack[^1], screen, StringComparison.Ordinal))
			return true;
		_stack.Add(screen);
		return true;
	}

	public bool Pop()
	{
		if (_stack.Count <= 1)
			return false;
		_stack.RemoveAt(_stack.Count - 1);
		return true;
	}

	public void Reset()
	{
		_stack.Clear();
		_stack.Add("home");
		Focus = "";
	}

	public void FocusFrom(Element? tree) => Focus = HeadingKey(tree) ?? "";

	static string? HeadingKey(Element? element)
	{
		switch (element)
		{
			case null:
				return null;
			case Text text when text.Style is { } style && HostPolicy.IsHeading(style.FontSize) && text.Key is { Length: > 0 } key:
				return key;
			case Column column:
				return First(column.Children);
			case ScrollView scroll:
				return HeadingKey(scroll.Child);
			case ListView list:
				return First(list.Rows);
			case Button:
			case TextField:
			case TextInput:
			case Switch:
			case Image:
			case HostView:
			case Text:
				return null;
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static string? First(IReadOnlyList<Element> children)
	{
		foreach (var child in children)
		{
			var found = HeadingKey(child);
			if (found is not null)
				return found;
		}

		return null;
	}
}

public static class Nav
{
	public static void Push(IPlatform platform, string screen)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("nav", "Push", [screen]);
	}

	public static void Pop(IPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);
		platform.Module("nav", "Pop", []);
	}
}
