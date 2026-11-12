using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace NativePoc;

public static class PersistRows
{
	public static string Encode(IReadOnlyList<string[]> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		var builder = new StringBuilder();
		builder.Append("1\n");
		builder.Append(rows.Count.ToString(CultureInfo.InvariantCulture));
		builder.Append('\n');
		foreach (var row in rows)
		{
			ArgumentNullException.ThrowIfNull(row);
			builder.Append(row.Length.ToString(CultureInfo.InvariantCulture));
			builder.Append('\n');
			foreach (var cell in row)
				Write(builder, cell ?? "");
		}

		return builder.ToString();
	}

	public static string Encode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(IReadOnlyList<T> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		var props = Props<T>();
		var cells = new List<string[]>(rows.Count);
		foreach (var row in rows)
		{
			if (row is null)
				throw new ArgumentNullException(nameof(rows));
			var line = new string[props.Length];
			for (var index = 0; index < props.Length; index++)
				line[index] = Cell(props[index].GetValue(row));
			cells.Add(line);
		}

		return Encode(cells);
	}

	public static bool TryDecode(string text, out List<string[]> rows)
	{
		rows = new List<string[]>();
		if (text is null || !text.StartsWith("1\n", StringComparison.Ordinal))
			return false;
		var cursor = 2;
		if (!ReadInt(text, ref cursor, out var count))
			return false;
		for (var index = 0; index < count; index++)
		{
			if (!ReadInt(text, ref cursor, out var width))
				return false;
			var line = new string[width];
			for (var cell = 0; cell < width; cell++)
			{
				if (!Read(text, ref cursor, out line[cell]))
					return false;
			}

			rows.Add(line);
		}

		if (cursor != text.Length)
			return false;
		return true;
	}

	public static void Fill<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)] T>(IList<T> rows, string text)
		where T : new()
	{
		ArgumentNullException.ThrowIfNull(rows);
		if (!TryDecode(text, out var decoded))
			return;
		var props = Props<T>();
		rows.Clear();
		foreach (var cells in decoded)
		{
			var row = new T();
			var width = Math.Min(props.Length, cells.Length);
			for (var index = 0; index < width; index++)
			{
				if (props[index].PropertyType == typeof(bool))
					props[index].SetValue(row, cells[index] == "true");
				else if (props[index].PropertyType == typeof(string))
					props[index].SetValue(row, cells[index]);
			}

			rows.Add(row);
		}
	}

	static PropertyInfo[] Props<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
	{
		var props = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
		Array.Sort(props, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
		return props;
	}

	static string Cell(object? value) => value switch
	{
		bool flag => flag ? "true" : "false",
		string text => text,
		null => "",
		_ => "",
	};

	static void Write(StringBuilder builder, string value)
	{
		builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
		builder.Append('\n');
		builder.Append(value);
		builder.Append('\n');
	}

	static bool ReadInt(string text, ref int cursor, out int value)
	{
		value = 0;
		var line = text.IndexOf('\n', cursor);
		if (line < 0)
			return false;
		if (!int.TryParse(text.AsSpan(cursor, line - cursor), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0)
			return false;
		cursor = line + 1;
		return true;
	}

	static bool Read(string text, ref int cursor, out string value)
	{
		value = "";
		if (!ReadInt(text, ref cursor, out var length))
			return false;
		var end = cursor + length;
		if (end >= text.Length || text[end] != '\n')
			return false;
		value = text.Substring(cursor, length);
		cursor = end + 1;
		return true;
	}
}
