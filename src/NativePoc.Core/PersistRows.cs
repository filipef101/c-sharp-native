using System.Globalization;
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

	public static string Encode<T>(IReadOnlyList<T> rows, Func<T, string[]> fields)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(fields);
		var cells = new List<string[]>(rows.Count);
		foreach (var row in rows)
		{
			if (row is null)
				throw new ArgumentNullException(nameof(rows));
			cells.Add(fields(row) ?? throw new ArgumentNullException(nameof(fields)));
		}

		return Encode(cells);
	}

	internal static string EncodeOrder(IReadOnlyList<string[]> rows, IReadOnlyList<int> order)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(order);
		var cells = new List<string[]>(rows.Count);
		foreach (var row in rows)
		{
			ArgumentNullException.ThrowIfNull(row);
			var next = new string[order.Count];
			for (var index = 0; index < order.Count; index++)
			{
				var source = order[index];
				next[index] = (uint)source < (uint)row.Length ? row[source] ?? "" : "";
			}

			cells.Add(next);
		}

		return Encode(cells);
	}

	internal static List<string[]> ApplyOrder(List<string[]> rows, IReadOnlyList<int> order)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(order);
		var mapped = new List<string[]>(rows.Count);
		foreach (var row in rows)
		{
			var next = new string[order.Count];
			for (var index = 0; index < order.Count; index++)
			{
				var source = order[index];
				next[index] = row is null || (uint)source >= (uint)row.Length ? "" : row[source] ?? "";
			}

			mapped.Add(next);
		}

		return mapped;
	}

	public static string EncodeFields(params string[] fields)
	{
		ArgumentNullException.ThrowIfNull(fields);
		var builder = new StringBuilder();
		builder.Append("2\n");
		builder.Append(fields.Length.ToString(CultureInfo.InvariantCulture));
		builder.Append('\n');
		foreach (var field in fields)
			Write(builder, field ?? "");
		return builder.ToString();
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

	public static bool TryDecodeFields(string text, out string[] fields)
	{
		fields = [];
		if (text is null || !text.StartsWith("2\n", StringComparison.Ordinal))
			return false;
		var cursor = 2;
		if (!ReadInt(text, ref cursor, out var count))
			return false;
		var line = new string[count];
		for (var index = 0; index < count; index++)
		{
			if (!Read(text, ref cursor, out line[index]))
				return false;
		}

		if (cursor != text.Length)
			return false;
		fields = line;
		return true;
	}

	public static void Fill<T>(IList<T> rows, string text, Func<string[], T> make)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(make);
		if (!TryDecode(text, out var decoded))
			return;
		rows.Clear();
		foreach (var cells in decoded)
			rows.Add(make(cells) ?? throw new ArgumentNullException(nameof(make)));
	}

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
