namespace NativePoc;

public readonly record struct RowFace(string Key, string Title, string Subtitle, string Picture)
{
	public static RowFace Read(Element element)
	{
		ArgumentNullException.ThrowIfNull(element);
		var key = element.Key ?? "";
		switch (element)
		{
			case Text text:
				return new RowFace(key, text.Value, "", "");
			case Column column:
				return FromColumn(key, column);
			case Button button:
				return new RowFace(key, button.Title, "", "");
			case TextField field:
				return new RowFace(key, field.Value, "", "");
			case Switch:
			case ScrollView:
			case ListView:
			case Image:
			case HostView:
				return new RowFace(key, "", "", "");
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	static RowFace FromColumn(string key, Column column)
	{
		var title = "";
		var subtitle = "";
				var pictureSource = "";
		foreach (var child in column.Children)
		{
			switch (child)
			{
				case Text text when title.Length == 0:
					title = text.Value;
					break;
				case Text text when subtitle.Length == 0 && text.Value.Length > 0:
					subtitle = text.Value;
					break;
				case Image picture when pictureSource.Length == 0 && picture.Source.Length > 0:
					pictureSource = picture.Source;
					break;
			}
		}

		return new RowFace(key, title, subtitle, pictureSource);
	}
}

public static class RowMetrics
{
	public static int Line(float density, float fontScale)
	{
		if (density <= 0)
			density = 1;
		if (fontScale <= 0)
			fontScale = 1;
		return Math.Max(1, (int)Math.Ceiling(16 * density * fontScale * 1.2));
	}

	public static int ImageBlock(float density)
	{
		if (density <= 0)
			density = 1;
		return Math.Max(1, (int)Math.Ceiling(48 * density));
	}

	public static int Height(RowFace face, float density, float fontScale)
	{
		var height = Line(density, fontScale);
		if (face.Subtitle.Length > 0)
			height += Line(density, fontScale);
		if (face.Picture.Length > 0)
			height += ImageBlock(density);
		return height;
	}
}

public sealed class RowSlotState
{
	public string Key { get; private set; } = "";

	public string Title { get; private set; } = "";

	public string Subtitle { get; private set; } = "";

	public string Picture { get; private set; } = "";

	public void Bind(Element row)
	{
		var face = RowFace.Read(row);
		Key = face.Key;
		Title = face.Title;
		Subtitle = face.Subtitle;
		Picture = face.Picture;
	}
}
