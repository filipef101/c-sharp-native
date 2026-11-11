using System.Text;

namespace NativePoc.Devx;

static class AccessibilityDump
{
	public static string Read(string serial)
	{
		var dumped = AndroidLoop.Adb(serial, "shell", "uiautomator", "dump", "/sdcard/window_dump.xml");
		if (dumped.Code != 0)
			throw new InvalidOperationException("uiautomator dump failed: " + dumped.Stderr + dumped.Stdout);
		var read = AndroidLoop.Adb(serial, "shell", "cat", "/sdcard/window_dump.xml");
		if (read.Code != 0 || read.Stdout.Length == 0)
			throw new InvalidOperationException("uiautomator dump was empty: " + read.Stderr + read.Stdout);
		return read.Stdout;
	}

	public static string Summarize(string xml)
	{
		var builder = new StringBuilder();
		var cursor = 0;
		while (cursor < xml.Length)
		{
			var node = xml.IndexOf("<node ", cursor, StringComparison.Ordinal);
			if (node < 0)
				break;
			var end = xml.IndexOf('>', node);
			if (end < 0)
				break;
			var piece = xml[node..end];
			builder.Append("class=").Append(Attr(piece, "class"));
			builder.Append(" desc=").Append(Quote(Attr(piece, "content-desc")));
			builder.Append(" text=").Append(Quote(Attr(piece, "text")));
			builder.Append(" bounds=").Append(Attr(piece, "bounds"));
			builder.Append('\n');
			cursor = end + 1;
		}

		return builder.ToString();
	}

	static string Attr(string node, string name)
	{
		var needle = name + "=\"";
		var start = node.IndexOf(needle, StringComparison.Ordinal);
		if (start < 0)
			return "";
		start += needle.Length;
		var end = node.IndexOf('"', start);
		if (end < 0)
			return "";
		return node[start..end];
	}

	static string Quote(string value) =>
		"\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
