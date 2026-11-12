namespace NativePoc;

internal static class Speech
{
	public static string? Of(Element element)
	{
		if (element is not Column column)
			return null;
		if (column.Key is "pieces" or "kept" or "kiln"
			&& column.Children.Count == 2
			&& column.Children[0] is Text value
			&& column.Children[1] is Text label)
			return value.Value + " " + label.Value;
		Button? button = null;
		Text? name = null;
		foreach (var child in column.Children)
		{
			if (child is Button tile)
				button = tile;
			else if (child is Text text)
				name = text;
		}

		if (button is null || name is null)
			return null;
		if (name.Value.Length == 0 || !button.AccessibilityLabel.StartsWith(name.Value, StringComparison.Ordinal))
			return null;
		if (button.AccessibilityLabel.Length <= name.Value.Length)
			return null;
		return button.AccessibilityLabel;
	}

	public static string? KeepChange(string previous, string next)
	{
		if (previous.Length == 0 || string.Equals(previous, next, StringComparison.Ordinal))
			return null;
		return next;
	}
}
