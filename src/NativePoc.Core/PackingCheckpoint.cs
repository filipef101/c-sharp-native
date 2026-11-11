using System.Globalization;
using System.Text;

namespace NativePoc;

public readonly record struct PackingCheckpointItem(string Id, string Name, bool Packed);

public sealed class PackingCheckpoint
{
	public bool Editor { get; init; }

	public bool DraftPacked { get; init; }

	public int NextNumber { get; init; }

	public string? EditingId { get; init; }

	public string DraftName { get; init; } = "";

	public string DraftNote { get; init; } = "";

	public IReadOnlyList<PackingCheckpointItem> Items { get; init; } = [];

	public static int Size(PackingCheckpoint packing)
	{
		ArgumentNullException.ThrowIfNull(packing);
		var size = 1 + 1 + 4 + 2 + ByteCount(packing.EditingId) + 2 + ByteCount(packing.DraftName) + 2 + 2 + ByteCount(packing.DraftNote);
		foreach (var item in packing.Items)
			size += 2 + ByteCount(item.Id) + 2 + ByteCount(item.Name) + 1;
		return size;
	}

	public static void Write(byte[] payload, ref int cursor, PackingCheckpoint packing)
	{
		ArgumentNullException.ThrowIfNull(packing);
		payload[cursor++] = packing.Editor ? (byte)1 : (byte)0;
		payload[cursor++] = packing.DraftPacked ? (byte)1 : (byte)0;
		payload[cursor++] = (byte)packing.NextNumber;
		payload[cursor++] = (byte)(packing.NextNumber >> 8);
		payload[cursor++] = (byte)(packing.NextNumber >> 16);
		payload[cursor++] = (byte)(packing.NextNumber >> 24);
		cursor = WriteText(payload, cursor, packing.EditingId);
		cursor = WriteText(payload, cursor, packing.DraftName);
		if (packing.Items.Count > ushort.MaxValue)
			throw new InvalidOperationException("The packing list is too long to checkpoint.");
		payload[cursor++] = (byte)packing.Items.Count;
		payload[cursor++] = (byte)(packing.Items.Count >> 8);
		foreach (var item in packing.Items)
		{
			cursor = WriteText(payload, cursor, item.Id);
			cursor = WriteText(payload, cursor, item.Name);
			payload[cursor++] = item.Packed ? (byte)1 : (byte)0;
		}

		cursor = WriteText(payload, cursor, packing.DraftNote);
	}

	public static PackingCheckpoint Read(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 8 > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var editor = payload[cursor++];
		var draftPacked = payload[cursor++];
		if (editor > 1 || draftPacked > 1)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var next = payload[cursor] | (payload[cursor + 1] << 8) | (payload[cursor + 2] << 16) | (payload[cursor + 3] << 24);
		cursor += 4;
		var editing = ReadText(payload, ref cursor);
		var draft = ReadText(payload, ref cursor);
		if (cursor + 2 > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var count = payload[cursor] | (payload[cursor + 1] << 8);
		cursor += 2;
		var items = new PackingCheckpointItem[count];
		for (var index = 0; index < count; index++)
		{
			var id = ReadText(payload, ref cursor) ?? throw new InvalidOperationException("Rejected state checkpoint.");
			var name = ReadText(payload, ref cursor) ?? "";
			if (cursor >= payload.Length)
				throw new InvalidOperationException("Rejected state checkpoint.");
			var packed = payload[cursor++];
			if (packed > 1 || id.Length == 0)
				throw new InvalidOperationException("Rejected state checkpoint.");
			items[index] = new PackingCheckpointItem(id, name, packed == 1);
		}

		var note = "";
		if (cursor < payload.Length)
			note = ReadText(payload, ref cursor) ?? "";
		return new PackingCheckpoint
		{
			Editor = editor == 1,
			DraftPacked = draftPacked == 1,
			NextNumber = next,
			EditingId = editing,
			DraftName = draft ?? "",
			DraftNote = note,
			Items = items,
		};
	}

	public string Format()
	{
		var builder = new StringBuilder();
		builder.Append("route=").Append(Editor ? "Editor" : "List").Append('\n');
		builder.Append("draftPacked=").Append(DraftPacked ? "true" : "false").Append('\n');
		builder.Append(CultureInfo.InvariantCulture, $"next={NextNumber}\n");
		builder.Append("editing=").Append(EditingId is null ? "" : Hex(EditingId)).Append('\n');
		builder.Append("draft=").Append(Hex(DraftName)).Append('\n');
		builder.Append("note=").Append(Hex(DraftNote)).Append('\n');
		foreach (var item in Items)
		{
			builder.Append("item=")
				.Append(Hex(item.Id))
				.Append(' ')
				.Append(Hex(item.Name))
				.Append(' ')
				.Append(item.Packed ? "true" : "false")
				.Append('\n');
		}

		return builder.ToString();
	}

	public static bool TryParse(string text, out PackingCheckpoint checkpoint)
	{
		checkpoint = new PackingCheckpoint();
		var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		string? route = null;
		bool? draftPacked = null;
		int? next = null;
		string? editingLine = null;
		string? draftLine = null;
		string? noteLine = null;
		var items = new List<PackingCheckpointItem>();
		foreach (var line in lines)
		{
			if (line.Length == 0)
				continue;
			if (Take(line, "route=", out var routeValue))
			{
				route = routeValue;
				continue;
			}

			if (Take(line, "draftPacked=", out var packedValue))
			{
				if (packedValue == "true")
					draftPacked = true;
				else if (packedValue == "false")
					draftPacked = false;
				else
					return false;
				continue;
			}

			if (Take(line, "next=", out var nextValue))
			{
				if (!int.TryParse(nextValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
					return false;
				next = parsed;
				continue;
			}

			if (Take(line, "editing=", out var editingValue))
			{
				editingLine = editingValue;
				continue;
			}

			if (Take(line, "draft=", out var draftValue))
			{
				draftLine = draftValue;
				continue;
			}

			if (Take(line, "note=", out var noteValue))
			{
				noteLine = noteValue;
				continue;
			}

			if (!Take(line, "item=", out var itemValue))
				continue;
			var parts = itemValue.Split(' ');
			if (parts.Length != 3 || !Hex(parts[0], out var id) || id.Length == 0 || !Hex(parts[1], out var name))
				return false;
			if (parts[2] == "true")
				items.Add(new PackingCheckpointItem(id, name, true));
			else if (parts[2] == "false")
				items.Add(new PackingCheckpointItem(id, name, false));
			else
				return false;
		}

		if (route is null && draftPacked is null && next is null && editingLine is null && draftLine is null && items.Count == 0)
			return false;
		if (route is not ("List" or "Editor") || draftPacked is null || next is null || editingLine is null || draftLine is null)
			return false;
		string? editing = null;
		if (editingLine.Length != 0)
		{
			if (!Hex(editingLine, out var decoded))
				return false;
			editing = decoded;
		}
		if (!Hex(draftLine, out var draft))
			return false;
		var note = "";
		if (noteLine is not null && !Hex(noteLine, out note))
			return false;
		checkpoint = new PackingCheckpoint
		{
			Editor = route == "Editor",
			DraftPacked = draftPacked.Value,
			NextNumber = next.Value,
			EditingId = editing,
			DraftName = draft,
			DraftNote = note,
			Items = items,
		};
		return true;
	}

	static int ByteCount(string? text) => Encoding.UTF8.GetByteCount(text ?? "");

	static int WriteText(byte[] payload, int cursor, string? text)
	{
		var bytes = Encoding.UTF8.GetBytes(text ?? "");
		if (bytes.Length > ushort.MaxValue)
			throw new InvalidOperationException("A packing string is too long to checkpoint.");
		payload[cursor] = (byte)bytes.Length;
		payload[cursor + 1] = (byte)(bytes.Length >> 8);
		bytes.CopyTo(payload, cursor + 2);
		return cursor + 2 + bytes.Length;
	}

	static string? ReadText(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 2 > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var length = payload[cursor] | (payload[cursor + 1] << 8);
		cursor += 2;
		if (cursor + length > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		if (length == 0)
		{
			return null;
		}

		var text = Encoding.UTF8.GetString(payload.Slice(cursor, length));
		cursor += length;
		return text;
	}

	static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text)).ToLowerInvariant();

	static bool Hex(string text, out string value)
	{
		value = "";
		if ((text.Length & 1) != 0)
			return false;
		try
		{
			value = Encoding.UTF8.GetString(Convert.FromHexString(text));
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

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
}
