namespace NativePoc;

public readonly record struct RowFace(string Key, string Title, string Subtitle, string Picture)
{
	public string Describe()
	{
		if (Title.Length == 0)
			return Subtitle;
		if (Subtitle.Length == 0)
			return Title;
		return Title + ", " + Subtitle;
	}

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
			case TextInput input:
				return new RowFace(key, input.Value, "", "");
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

public sealed class RowPrefix
{
	readonly int[] _tree;
	readonly int[] _height;

	public RowPrefix(int count, int fallback)
	{
		if (count < 0)
			throw new ArgumentOutOfRangeException(nameof(count));
		if (fallback < 1)
			fallback = 1;
		Count = count;
		_height = new int[count];
		_tree = new int[count + 1];
		for (var index = 0; index < count; index++)
		{
			_height[index] = fallback;
			Add(index, fallback);
		}
	}

	public int Count { get; }

	public int Total => Sum(Count);

	public int Y(int index)
	{
		if ((uint)index > (uint)Count)
			throw new ArgumentOutOfRangeException(nameof(index));
		return Sum(index);
	}

	public int HeightAt(int index)
	{
		if ((uint)index >= (uint)Count)
			throw new ArgumentOutOfRangeException(nameof(index));
		return _height[index];
	}

	public void Set(int index, int height)
	{
		if ((uint)index >= (uint)Count)
			throw new ArgumentOutOfRangeException(nameof(index));
		if (height < 1)
			height = 1;
		var delta = height - _height[index];
		if (delta == 0)
			return;
		_height[index] = height;
		Add(index, delta);
	}

	public int IndexAt(int y)
	{
		if (Count == 0)
			return 0;
		if (y < 0)
			y = 0;
		var lo = 0;
		var hi = Count - 1;
		while (lo < hi)
		{
			var mid = (lo + hi + 1) >>> 1;
			if (Y(mid) <= y)
				lo = mid;
			else
				hi = mid - 1;
		}

		return lo;
	}

	void Add(int index, int delta)
	{
		for (var at = index + 1; at <= Count; at += at & -at)
			_tree[at] += delta;
	}

	int Sum(int count)
	{
		var total = 0;
		for (var at = count; at > 0; at -= at & -at)
			total += _tree[at];
		return total;
	}
}

public static class RowWindowPlan
{
	public static int[] Assign(ReadOnlySpan<int> current, int first, int rowCount, int window)
	{
		if (window < 0)
			throw new ArgumentOutOfRangeException(nameof(window));
		if (first < 0)
			first = 0;
		if (rowCount < 0)
			rowCount = 0;
		var next = new int[window];
		Array.Fill(next, -1);
		var last = rowCount;
		if (last > first + window)
			last = first + window;
		var visible = last - first;
		if (visible <= 0)
			return next;
		var claimed = new bool[visible];
		var used = new bool[window];
		var limit = current.Length < window ? current.Length : window;
		for (var slot = 0; slot < limit; slot++)
		{
			var index = current[slot];
			if (index < first || index >= last)
				continue;
			var offset = index - first;
			if (claimed[offset])
				continue;
			claimed[offset] = true;
			used[slot] = true;
			next[slot] = index;
		}

		var free = 0;
		for (var offset = 0; offset < visible; offset++)
		{
			if (claimed[offset])
				continue;
			while (free < window && used[free])
				free++;
			if (free >= window)
				break;
			used[free] = true;
			next[free] = first + offset;
			free++;
		}

		return next;
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
