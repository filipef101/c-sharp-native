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
		if (element is null)
			return "empty\n";
		var builder = new StringBuilder();
		foreach (var node in ElementIds.Flatten(element))
			Write(builder, node);
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
		if (lines.Length < 5 || lines[^1].Length != 0)
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

	static void Write(StringBuilder builder, ElementIds.Node node)
	{
		builder.Append(' ', node.Depth * 2);
		var element = node.Element;
		builder.Append(Kind(element));
		builder.Append(" role=").Append(Quote(Role(element)));
		builder.Append(" id=").Append(Quote(node.Id));
		switch (element)
		{
			case Column column:
				Key(builder, column.Key);
				builder.Append('\n');
				return;
			case Text text:
				Key(builder, text.Key);
				builder.Append(" value=").Append(Quote(text.Value)).Append('\n');
				return;
			case Button button:
				Key(builder, button.Key);
				builder.Append(" title=").Append(Quote(button.Title));
				builder.Append(" label=").Append(Quote(button.AccessibilityLabel)).Append('\n');
				return;
			case TextField field:
				Key(builder, field.Key);
				builder.Append(" value=").Append(Quote(field.Value));
				builder.Append(" placeholder=").Append(Quote(field.Placeholder));
				builder.Append(" label=").Append(Quote(field.AccessibilityLabel));
				if (field.Keyboard != Keyboard.Text || field.Lines > 1 || field.Submit != Submit.None)
				{
					builder.Append(" keyboard=").Append(field.Keyboard.ToString());
					builder.Append(" lines=").Append(field.Lines.ToString(CultureInfo.InvariantCulture));
					builder.Append(" submit=").Append(field.Submit.ToString());
				}

				builder.Append('\n');
				return;
			case Switch toggle:
				Key(builder, toggle.Key);
				builder.Append(" value=").Append(toggle.Value ? "true" : "false");
				builder.Append(" label=").Append(Quote(toggle.AccessibilityLabel)).Append('\n');
				return;
			case ScrollView scroll:
				Key(builder, scroll.Key);
				builder.Append('\n');
				return;
			case ListView list:
				Key(builder, list.Key);
				builder.Append(" count=").Append(list.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
				return;
			case Image image:
				Key(builder, image.Key);
				builder.Append(" source=").Append(Quote(image.Source)).Append('\n');
				return;
			default:
				throw Unknown(element);
		}
	}

	public static string Role(Element element)
	{
		switch (element)
		{
			case Column:
				return "column";
			case Text:
				return "text";
			case Button:
				return "button";
			case TextField:
				return "textfield";
			case Switch:
				return "switch";
			case ScrollView:
				return "scroll";
			case ListView:
				return "list";
			case Image:
				return "image";
			default:
				throw Unknown(element);
		}
	}

	static string Kind(Element element) =>
		element switch
		{
			Column => "column",
			Text => "text",
			Button => "button",
			TextField => "textfield",
			Switch => "switch",
			ScrollView => "scroll",
			ListView => "list",
			Image => "image",
			_ => throw Unknown(element),
		};

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
	public static byte[] Write(AppState state) => Write(state, null, null);

	public static byte[] Write(AppState state, PackingCheckpoint? packing) => Write(state, packing, null);

	public static byte[] Write(AppState state, PackingCheckpoint? packing, NavCheckpoint? nav)
	{
		ArgumentNullException.ThrowIfNull(state);
		var name = Encoding.UTF8.GetBytes(state.Name);
		if (name.Length > ushort.MaxValue)
			throw new InvalidOperationException("The screen name is too long to checkpoint.");
		var packingSize = packing is null ? 0 : PackingCheckpoint.Size(packing);
		var payload = new byte[12 + name.Length + packingSize];
		payload[0] = (byte)'S';
		payload[1] = (byte)'T';
		payload[2] = (byte)'C';
		payload[3] = (byte)'1';
		payload[4] = packing is null ? (byte)1 : (byte)2;
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
		if (packing is not null)
		{
			var cursor = 12 + name.Length;
			PackingCheckpoint.Write(payload, ref cursor, packing);
			if (cursor != payload.Length)
				throw new InvalidOperationException("The packing checkpoint did not fill its buffer.");
		}

		if (nav is null)
			return payload;
		var trail = NavCheckpoint.Write(nav);
		var both = new byte[payload.Length + trail.Length];
		payload.CopyTo(both, 0);
		trail.CopyTo(both, payload.Length);
		return both;
	}

	public static void ReadInto(ReadOnlySpan<byte> payload, AppState state)
	{
		var (count, greetingFirst, plainRoot, name) = ReadApp(payload, out _, out _);
		state.Count = count;
		state.GreetingFirst = greetingFirst;
		state.PlainRoot = plainRoot;
		state.Name = name;
	}

	public static bool TryReadPacking(ReadOnlySpan<byte> payload, out PackingCheckpoint packing)
	{
		ReadApp(payload, out var read, out _);
		if (read is null)
		{
			packing = new PackingCheckpoint();
			return false;
		}

		packing = read;
		return true;
	}

	public static bool TryReadNav(ReadOnlySpan<byte> payload, out NavCheckpoint nav)
	{
		ReadApp(payload, out _, out var read);
		if (read is null)
		{
			nav = new NavCheckpoint();
			return false;
		}

		nav = read;
		return true;
	}

	static (int Count, bool GreetingFirst, bool PlainRoot, string Name) ReadApp(ReadOnlySpan<byte> payload, out PackingCheckpoint? packing, out NavCheckpoint? nav)
	{
		packing = null;
		nav = null;
		if (payload.Length < 12
			|| payload[0] != (byte)'S'
			|| payload[1] != (byte)'T'
			|| payload[2] != (byte)'C'
			|| payload[3] != (byte)'1'
			|| payload[4] is not (1 or 2))
			throw new InvalidOperationException("Rejected state checkpoint.");
		var count = payload[5] | (payload[6] << 8) | (payload[7] << 16) | (payload[8] << 24);
		var flags = payload[9];
		if ((flags & ~3) != 0)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var length = payload[10] | (payload[11] << 8);
		if (12 + length > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var name = Encoding.UTF8.GetString(payload.Slice(12, length));
		var cursor = 12 + length;
		if (payload[4] == 2)
			packing = PackingCheckpoint.Read(payload, ref cursor);
		if (cursor != payload.Length)
		{
			if (!NavCheckpoint.TryRead(payload, ref cursor, out var read) || cursor != payload.Length)
				throw new InvalidOperationException("Rejected state checkpoint.");
			nav = read;
		}

		return (count, (flags & 1) != 0, (flags & 2) != 0, name);
	}
}

public static class ScreenQuery
{
	public const byte Tree = 1;
	public const byte State = 2;
	public const byte Restore = 3;
	public const byte Press = 4;
	public const byte Enter = 5;
	public const byte Layout = 6;
	public const byte Clock = 7;
	public const byte Submit = 8;
	public const byte Fault = 9;
	public const byte Dismiss = 10;
	public const byte Swipe = 11;

	public static bool IsQuery(ReadOnlySpan<byte> payload) =>
		payload.Length >= 6
		&& payload[0] == (byte)'N'
		&& payload[1] == (byte)'P'
		&& payload[2] == (byte)'C'
		&& payload[3] == (byte)'Q';

	public static byte Op(ReadOnlySpan<byte> payload)
	{
		if (!IsQuery(payload) || payload[4] != 1)
			throw new InvalidOperationException("Rejected screen query.");
		var op = payload[5];
		switch (op)
		{
			case Tree:
			case State:
			case Layout:
			case Clock:
				if (payload.Length != 6)
					throw new InvalidOperationException("Rejected screen query.");
				return op;
			case Restore:
			case Press:
			case Enter:
			case Submit:
			case Fault:
				if (payload.Length < 7)
					throw new InvalidOperationException("Rejected screen query.");
				return op;
			case Dismiss:
				if (payload.Length != 6)
					throw new InvalidOperationException("Rejected screen query.");
				return op;
			case Swipe:
				if (payload.Length < 7)
					throw new InvalidOperationException("Rejected screen query.");
				return op;
			default:
				throw new InvalidOperationException("Rejected screen query.");
		}
	}

	public static byte[] Ask(byte op) => [(byte)'N', (byte)'P', (byte)'C', (byte)'Q', 1, op];

	public static byte[] RestoreState(byte[] checkpoint) => WithBody(Restore, checkpoint);

	public static byte[] PressId(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		return WithBody(Press, Encoding.UTF8.GetBytes(id));
	}

	public static byte[] SwipeId(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		return WithBody(Swipe, Encoding.UTF8.GetBytes(id));
	}

	public static byte[] SubmitId(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		return WithBody(Submit, Encoding.UTF8.GetBytes(id));
	}

	public static byte[] FaultNote(string id, string message, int start, int end, int line, int column)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(message);
		return WithBody(Fault, Encoding.UTF8.GetBytes(id + "\n" + message + "\n" + start.ToString(CultureInfo.InvariantCulture) + ":" + end.ToString(CultureInfo.InvariantCulture) + ":" + line.ToString(CultureInfo.InvariantCulture) + ":" + column.ToString(CultureInfo.InvariantCulture)));
	}

	public static byte[] DismissOverlay() => Ask(Dismiss);

	public static byte[] EnterId(string id, string text)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(text);
		return WithBody(Enter, Encoding.UTF8.GetBytes(id + "\n" + text));
	}

	static byte[] WithBody(byte op, byte[] body)
	{
		var payload = new byte[6 + body.Length];
		payload[0] = (byte)'N';
		payload[1] = (byte)'P';
		payload[2] = (byte)'C';
		payload[3] = (byte)'Q';
		payload[4] = 1;
		payload[5] = op;
		body.CopyTo(payload, 6);
		return payload;
	}
}
