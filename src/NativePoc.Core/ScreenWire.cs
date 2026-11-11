using System.Globalization;
using System.Text;

namespace NativePoc;

public enum ScreenFreshness
{
	Current,
	Compiled,
	Stale,
	Unreadable,
}

public static class ScreenSnapshot
{
	public static string FormatTree(Element? element)
	{
		var builder = new StringBuilder();
		Write(builder, element, 0);
		return builder.ToString();
	}

	public static string FormatState(AppState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		return string.Create(
			CultureInfo.InvariantCulture,
			$"count={state.Count}\ngreetingFirst={(state.GreetingFirst ? "true" : "false")}\nplainRoot={(state.PlainRoot ? "true" : "false")}\nname={Convert.ToHexString(Encoding.UTF8.GetBytes(state.Name)).ToLowerInvariant()}\n");
	}

	public static bool TryReadState(string text, AppState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (lines.Length != 5 || lines[4].Length != 0)
			return false;
		if (!Take(lines[0], "count=", out var countText) || !int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
			return false;
		if (!Take(lines[1], "greetingFirst=", out var greeting) || !Bool(greeting, out var greetingFirst))
			return false;
		if (!Take(lines[2], "plainRoot=", out var plain) || !Bool(plain, out var plainRoot))
			return false;
		if (!Take(lines[3], "name=", out var name) || !TryHex(name, out var decoded))
			return false;
		state.Count = count;
		state.GreetingFirst = greetingFirst;
		state.PlainRoot = plainRoot;
		state.Name = decoded;
		return true;
	}

	public static ScreenFreshness Freshness(string treeReply, string currentHash)
	{
		var lineEnd = treeReply.IndexOf('\n');
		var line = lineEnd < 0 ? treeReply : treeReply[..lineEnd];
		if (!line.StartsWith("source=", StringComparison.Ordinal))
			return ScreenFreshness.Unreadable;
		var running = line["source=".Length..];
		if (running.Length == 0)
			return ScreenFreshness.Compiled;
		return string.Equals(running, currentHash, StringComparison.OrdinalIgnoreCase)
			? ScreenFreshness.Current
			: ScreenFreshness.Stale;
	}

	static void Write(StringBuilder builder, Element? element, int depth)
	{
		if (element is null)
		{
			builder.Append("empty\n");
			return;
		}

		builder.Append(' ', depth * 2);
		switch (element)
		{
			case Column column:
				builder.Append("column");
				Key(builder, column.Key);
				builder.Append('\n');
				foreach (var child in column.Children)
					Write(builder, child, depth + 1);
				return;
			case Text text:
				builder.Append("text");
				Key(builder, text.Key);
				builder.Append(" value=").Append(Quote(text.Value)).Append('\n');
				return;
			case Button button:
				builder.Append("button");
				Key(builder, button.Key);
				builder.Append(" title=").Append(Quote(button.Title));
				builder.Append(" label=").Append(Quote(button.AccessibilityLabel)).Append('\n');
				return;
			case TextField field:
				builder.Append("textfield");
				Key(builder, field.Key);
				builder.Append(" value=").Append(Quote(field.Value));
				builder.Append(" placeholder=").Append(Quote(field.Placeholder));
				builder.Append(" label=").Append(Quote(field.AccessibilityLabel)).Append('\n');
				return;
			case Switch toggle:
				builder.Append("switch");
				Key(builder, toggle.Key);
				builder.Append(" value=").Append(toggle.Value ? "true" : "false");
				builder.Append(" label=").Append(Quote(toggle.AccessibilityLabel)).Append('\n');
				return;
			default:
				throw Unknown(element);
		}
	}

	static void Key(StringBuilder builder, string? key)
	{
		if (key is null)
			return;
		builder.Append(" key=").Append(Quote(key));
	}

	static string Quote(string value) =>
		"\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

	static bool Take(string line, string prefix, out string value)
	{
		if (!line.StartsWith(prefix, StringComparison.Ordinal))
		{
			value = "";
			return false;
		}

		value = line[prefix.Length..];
		return true;
	}

	static bool Bool(string text, out bool value)
	{
		if (text == "true")
		{
			value = true;
			return true;
		}

		if (text == "false")
		{
			value = false;
			return true;
		}

		value = false;
		return false;
	}

	static bool TryHex(string text, out string value)
	{
		value = "";
		if ((text.Length & 1) != 0)
			return false;
		try
		{
			var bytes = Convert.FromHexString(text);
			value = Encoding.UTF8.GetString(bytes);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	static InvalidOperationException Unknown(Element element) =>
		new("Unknown element.");
}

public static class StateCheckpoint
{
	public static byte[] Write(AppState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		var name = Encoding.UTF8.GetBytes(state.Name);
		if (name.Length > ushort.MaxValue)
			throw new InvalidOperationException("The screen name is too long to checkpoint.");
		var payload = new byte[4 + 1 + 4 + 1 + 2 + name.Length];
		payload[0] = (byte)'S';
		payload[1] = (byte)'T';
		payload[2] = (byte)'C';
		payload[3] = (byte)'1';
		payload[4] = 1;
		payload[5] = (byte)state.Count;
		payload[6] = (byte)(state.Count >> 8);
		payload[7] = (byte)(state.Count >> 16);
		payload[8] = (byte)(state.Count >> 24);
		var flags = 0;
		if (state.GreetingFirst)
			flags |= 1;
		if (state.PlainRoot)
			flags |= 2;
		payload[9] = (byte)flags;
		payload[10] = (byte)name.Length;
		payload[11] = (byte)(name.Length >> 8);
		name.CopyTo(payload, 12);
		return payload;
	}

	public static void ReadInto(ReadOnlySpan<byte> payload, AppState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (payload.Length < 12
			|| payload[0] != (byte)'S'
			|| payload[1] != (byte)'T'
			|| payload[2] != (byte)'C'
			|| payload[3] != (byte)'1'
			|| payload[4] != 1)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var count = payload[5] | (payload[6] << 8) | (payload[7] << 16) | (payload[8] << 24);
		var flags = payload[9];
		if ((flags & ~3) != 0)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var length = payload[10] | (payload[11] << 8);
		if (payload.Length != 12 + length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var name = Encoding.UTF8.GetString(payload.Slice(12, length));
		state.Count = count;
		state.GreetingFirst = (flags & 1) != 0;
		state.PlainRoot = (flags & 2) != 0;
		state.Name = name;
	}
}

public static class ScreenQuery
{
	public const byte Tree = 1;
	public const byte State = 2;
	public const byte Restore = 3;

	public static bool IsQuery(ReadOnlySpan<byte> payload) =>
		payload.Length >= 6
		&& payload[0] == (byte)'N'
		&& payload[1] == (byte)'P'
		&& payload[2] == (byte)'C'
		&& payload[3] == (byte)'Q';

	public static byte Op(ReadOnlySpan<byte> payload)
	{
		if (!IsQuery(payload) || payload[4] != 1 || payload[5] is < Tree or > Restore)
			throw new InvalidOperationException("Rejected screen query.");
		if (payload[5] != Restore && payload.Length != 6)
			throw new InvalidOperationException("Rejected screen query.");
		return payload[5];
	}

	public static byte[] Ask(byte op) => [(byte)'N', (byte)'P', (byte)'C', (byte)'Q', 1, op];

	public static byte[] RestoreState(byte[] checkpoint)
	{
		ArgumentNullException.ThrowIfNull(checkpoint);
		var payload = new byte[6 + checkpoint.Length];
		payload[0] = (byte)'N';
		payload[1] = (byte)'P';
		payload[2] = (byte)'C';
		payload[3] = (byte)'Q';
		payload[4] = 1;
		payload[5] = Restore;
		checkpoint.CopyTo(payload, 6);
		return payload;
	}
}
