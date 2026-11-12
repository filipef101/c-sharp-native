using System.Globalization;
using System.Text;

namespace NativePoc;

public readonly record struct PersistRecord(string Id, string Title, string Note);

/* Length-prefixed text. No reflection and no serializer. A value may contain
   newlines because the reader takes the declared character count. */
internal static class PersistCodec
{
	public static string Encode(PersistRecord record)
	{
		var builder = new StringBuilder();
		builder.Append("1\n");
		Write(builder, record.Id);
		Write(builder, record.Title);
		Write(builder, record.Note);
		return builder.ToString();
	}

	public static bool TryDecode(string text, out PersistRecord record)
	{
		record = default;
		if (text is null || !text.StartsWith("1\n", StringComparison.Ordinal))
			return false;
		var cursor = 2;
		if (!Read(text, ref cursor, out var id) || !Read(text, ref cursor, out var title) || !Read(text, ref cursor, out var note))
			return false;
		if (cursor != text.Length)
			return false;
		record = new PersistRecord(id, title, note);
		return true;
	}

	static void Write(StringBuilder builder, string value)
	{
		builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
		builder.Append('\n');
		builder.Append(value);
		builder.Append('\n');
	}

	static bool Read(string text, ref int cursor, out string value)
	{
		value = "";
		var line = text.IndexOf('\n', cursor);
		if (line < 0)
			return false;
		if (!int.TryParse(text.AsSpan(cursor, line - cursor), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length < 0)
			return false;
		var start = line + 1;
		var end = start + length;
		if (end >= text.Length || text[end] != '\n')
			return false;
		value = text.Substring(start, length);
		cursor = end + 1;
		return true;
	}
}

public static class Persist
{
	public static Task<string> Get(IPlatform platform, string key)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("persist", "Get", [key]);
	}

	public static Task<string> Put(IPlatform platform, string key, string value)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("persist", "Put", [key, value]);
	}

	public static Task<string> GetRecord(IPlatform platform, string key)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("persist", "GetRecord", [key]);
	}

	public static Task<string> PutRecord(IPlatform platform, string key, string id, string title, string note)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("persist", "PutRecord", [key, id, title, note]);
	}
}
