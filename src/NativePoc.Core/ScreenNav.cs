using System.Text;

namespace NativePoc;

public sealed class NavCheckpoint
{
	public IReadOnlyList<string> Stack { get; init; } = [];

	public IReadOnlyList<NavScreen> Screens { get; init; } = [];

	public static byte[] Write(NavCheckpoint nav)
	{
		ArgumentNullException.ThrowIfNull(nav);
		using var buffer = new MemoryStream();
		buffer.WriteByte((byte)'N');
		buffer.WriteByte((byte)'A');
		buffer.WriteByte((byte)'V');
		buffer.WriteByte(2);
		if (nav.Stack.Count > byte.MaxValue || nav.Screens.Count > byte.MaxValue)
			throw new InvalidOperationException("Rejected state checkpoint.");
		buffer.WriteByte((byte)nav.Stack.Count);
		foreach (var id in nav.Stack)
			WriteText(buffer, id);
		buffer.WriteByte((byte)nav.Screens.Count);
		foreach (var screen in nav.Screens)
		{
			WriteText(buffer, screen.Id);
			WritePairs(buffer, screen.State.Strings, (output, value) => WriteText(output, value));
			WritePairs(buffer, screen.State.Flags, (output, value) => output.WriteByte(value ? (byte)1 : (byte)0));
			WritePairs(buffer, screen.State.Numbers, WriteInt);
			if (screen.State.Lists.Count > ushort.MaxValue)
				throw new InvalidOperationException("Rejected state checkpoint.");
			WriteU16(buffer, screen.State.Lists.Count);
			foreach (var (name, rows) in screen.State.Lists)
			{
				WriteText(buffer, name);
				if (rows.Length > ushort.MaxValue)
					throw new InvalidOperationException("Rejected state checkpoint.");
				WriteU16(buffer, rows.Length);
				foreach (var row in rows)
				{
					if (row.Length > ushort.MaxValue)
						throw new InvalidOperationException("Rejected state checkpoint.");
					WriteU16(buffer, row.Length);
					foreach (var cell in row)
						WriteText(buffer, cell);
				}
			}

			var program = screen.Program ?? [];
			WriteInt(buffer, program.Length);
			if (program.Length > 0)
				buffer.Write(program, 0, program.Length);
		}

		return buffer.ToArray();
	}

	public static bool TryRead(ReadOnlySpan<byte> payload, ref int cursor, out NavCheckpoint nav)
	{
		nav = new NavCheckpoint();
		if (cursor + 4 > payload.Length)
			return false;
		if (payload[cursor] != (byte)'N' || payload[cursor + 1] != (byte)'A' || payload[cursor + 2] != (byte)'V')
			return false;
		var version = payload[cursor + 3];
		if (version is not (1 or 2))
			return false;
		cursor += 4;
		if (cursor >= payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var stackCount = payload[cursor++];
		var stack = new string[stackCount];
		for (var index = 0; index < stackCount; index++)
			stack[index] = ReadText(payload, ref cursor);
		if (cursor >= payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var screenCount = payload[cursor++];
		var screens = new NavScreen[screenCount];
		for (var index = 0; index < screenCount; index++)
		{
			var id = ReadText(payload, ref cursor);
			var strings = ReadPairs(payload, ref cursor, ReadText);
			var flags = ReadPairs(payload, ref cursor, (ReadOnlySpan<byte> bytes, ref int at) =>
			{
				if (at >= bytes.Length)
					throw new InvalidOperationException("Rejected state checkpoint.");
				var flag = bytes[at++];
				if (flag > 1)
					throw new InvalidOperationException("Rejected state checkpoint.");
				return flag == 1;
			});
			var numbers = ReadPairs(payload, ref cursor, ReadInt);
			var listCount = ReadU16(payload, ref cursor);
			var lists = new List<(string Name, string[][] Rows)>(listCount);
			for (var list = 0; list < listCount; list++)
			{
				var name = ReadText(payload, ref cursor);
				var rowCount = ReadU16(payload, ref cursor);
				var rows = new string[rowCount][];
				for (var row = 0; row < rowCount; row++)
				{
					var cellCount = ReadU16(payload, ref cursor);
					var cells = new string[cellCount];
					for (var cell = 0; cell < cellCount; cell++)
						cells[cell] = ReadText(payload, ref cursor);
					rows[row] = cells;
				}

				lists.Add((name, rows));
			}

			var program = Array.Empty<byte>();
			if (version == 2)
			{
				var length = ReadInt(payload, ref cursor);
				if (length < 0 || cursor + length > payload.Length)
					throw new InvalidOperationException("Rejected state checkpoint.");
				if (length > 0)
				{
					program = new byte[length];
					payload.Slice(cursor, length).CopyTo(program);
				}

				cursor += length;
			}

			screens[index] = new NavScreen
			{
				Id = id,
				State = new RecordProgram.BagImage
				{
					Strings = strings,
					Flags = flags,
					Numbers = numbers,
					Lists = lists,
				},
				Program = program,
			};
		}

		nav = new NavCheckpoint { Stack = stack, Screens = screens };
		return true;
	}

	static void WritePairs<T>(MemoryStream output, IReadOnlyList<(string Name, T Value)> pairs, Action<MemoryStream, T> write)
	{
		if (pairs.Count > ushort.MaxValue)
			throw new InvalidOperationException("Rejected state checkpoint.");
		WriteU16(output, pairs.Count);
		foreach (var (name, value) in pairs)
		{
			WriteText(output, name);
			write(output, value);
		}
	}

	static List<(string Name, T Value)> ReadPairs<T>(ReadOnlySpan<byte> payload, ref int cursor, Reader<T> read)
	{
		var count = ReadU16(payload, ref cursor);
		var pairs = new List<(string Name, T Value)>(count);
		for (var index = 0; index < count; index++)
		{
			var name = ReadText(payload, ref cursor);
			pairs.Add((name, read(payload, ref cursor)));
		}

		return pairs;
	}

	static void WriteInt(MemoryStream output, int value)
	{
		output.WriteByte((byte)value);
		output.WriteByte((byte)(value >> 8));
		output.WriteByte((byte)(value >> 16));
		output.WriteByte((byte)(value >> 24));
	}

	static int ReadInt(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 4 > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var value = payload[cursor] | (payload[cursor + 1] << 8) | (payload[cursor + 2] << 16) | (payload[cursor + 3] << 24);
		cursor += 4;
		return value;
	}

	static void WriteText(MemoryStream output, string? text)
	{
		var bytes = Encoding.UTF8.GetBytes(text ?? "");
		if (bytes.Length > ushort.MaxValue)
			throw new InvalidOperationException("Rejected state checkpoint.");
		WriteU16(output, bytes.Length);
		output.Write(bytes, 0, bytes.Length);
	}

	static string ReadText(ReadOnlySpan<byte> payload, ref int cursor)
	{
		var length = ReadU16(payload, ref cursor);
		if (cursor + length > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var text = Encoding.UTF8.GetString(payload.Slice(cursor, length));
		cursor += length;
		return text;
	}

	static void WriteU16(MemoryStream output, int value)
	{
		output.WriteByte((byte)value);
		output.WriteByte((byte)(value >> 8));
	}

	static int ReadU16(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 2 > payload.Length)
			throw new InvalidOperationException("Rejected state checkpoint.");
		var value = payload[cursor] | (payload[cursor + 1] << 8);
		cursor += 2;
		return value;
	}

	delegate T Reader<T>(ReadOnlySpan<byte> payload, ref int cursor);
}

public sealed class NavScreen
{
	public string Id { get; init; } = "";

	internal RecordProgram.BagImage State { get; init; } = new();

	public byte[] Program { get; init; } = [];
}

public sealed class ScreenNav
{
	readonly object _gate = new();
	readonly List<string> _stack = new();
	readonly Dictionary<string, byte[]> _programs = new(StringComparer.Ordinal);
	readonly Dictionary<string, byte[]> _pending = new(StringComparer.Ordinal);
	readonly Dictionary<string, RecordProgram.Bag> _bags = new(StringComparer.Ordinal);
	readonly HashSet<string> _allowed = new(StringComparer.Ordinal);
	byte[]? _topProgram;
	RecordProgram.Bag? _topBag;
	RecordProgram.Bound? _topBound;

	public static ScreenNav Shared { get; } = new();

	/* Empty means the first Show on an empty stack is the one screen.
	   A named root is the only frame a cold launch may add. */
	public string LaunchRoot { get; set; } = "";

	internal RecordProgram.Bag Bag(string id)
	{
		lock (_gate)
			return BagLocked(id);
	}

	public int Loaded
	{
		get
		{
			lock (_gate)
				return _programs.Count;
		}
	}

	public int Staged
	{
		get
		{
			lock (_gate)
				return _pending.Count;
		}
	}

	public void Stage(string id, byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		if (id.Length == 0)
			throw new InvalidOperationException("A screen id is required.");
		lock (_gate)
		{
			if (_programs.ContainsKey(id))
				return;
			_pending[id] = program.ToArray();
		}
	}

	public void ShowRoot(string id)
	{
		if (id.Length == 0)
			throw new InvalidOperationException("A screen id is required.");
		lock (_gate)
		{
			if (!_pending.ContainsKey(id) && !_programs.ContainsKey(id))
				throw new InvalidOperationException("Screen " + id + " is not staged.");
			if (_stack.Count == 0)
				_stack.Add(id);
		}
	}

	public void Register(byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		var id = IdOf(program);
		lock (_gate)
		{
			_programs[id] = program.ToArray();
			_ = BagLocked(id);
			DropCache();
		}
	}

	public void ShowRoot(byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		var id = IdOf(program);
		lock (_gate)
		{
			_programs[id] = program.ToArray();
			_ = BagLocked(id);
			DropCache();
			if (_stack.Count == 0)
				_stack.Add(id);
		}
	}

	public void Allow(string id)
	{
		if (id.Length == 0)
			return;
		lock (_gate)
			_allowed.Add(id);
	}

	public string TopId()
	{
		lock (_gate)
			return _stack.Count == 0 ? "" : _stack[^1];
	}

	public int Depth
	{
		get
		{
			lock (_gate)
				return _stack.Count;
		}
	}

	public bool HasProgram(string id)
	{
		lock (_gate)
		{
			Ensure(id);
			return _programs.ContainsKey(id);
		}
	}

	public bool ReplaceRoot(string id)
	{
		if (id.Length == 0)
			return false;
		lock (_gate)
		{
			Ensure(id);
			if (!_programs.ContainsKey(id) && !_allowed.Contains(id) && !_pending.ContainsKey(id))
				return false;
			_stack.Clear();
			_stack.Add(id);
			DropCache();
			StateEpoch.Bump();
			return true;
		}
	}

	public void Show(byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		var id = IdOf(program);
		lock (_gate)
		{
			_programs[id] = program.ToArray();
			_ = BagLocked(id);
			DropCache();
			var at = _stack.IndexOf(id);
			if (at >= 0)
			{
				StateEpoch.Bump();
				return;
			}

			if (_stack.Count == 0 && (LaunchRoot.Length == 0 || string.Equals(LaunchRoot, id, StringComparison.Ordinal)))
			{
				_stack.Add(id);
				StateEpoch.Bump();
			}
		}
	}

	public bool Push(string id)
	{
		if (id.Length == 0)
			return false;
		lock (_gate)
		{
			Ensure(id);
			if (!_programs.ContainsKey(id) && !_allowed.Contains(id))
				return false;
			var at = _stack.IndexOf(id);
			if (at < 0)
				_stack.Add(id);
			else if (at != _stack.Count - 1)
			{
				_stack.RemoveAt(at);
				_stack.Add(id);
			}
			else
				return true;
			DropCache();
			StateEpoch.Bump();
			return true;
		}
	}

	public void DeliverRow(
		string screen,
		string list,
		string rowId,
		int idField,
		(string Name, int Index, string Value)[] fields,
		(string Name, int Index, bool Value)[] flags)
	{
		ArgumentNullException.ThrowIfNull(fields);
		ArgumentNullException.ThrowIfNull(flags);
		var source = TopId();
		if (!Push(screen))
			return;
		var bag = Bag(screen);
		var map = new StringBuilder();
		foreach (var field in fields)
		{
			bag.WriteString(field.Name, field.Value);
			if (map.Length > 0)
				map.Append('\n');
			map.Append(field.Name).Append('=').Append(field.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		var flagMap = new StringBuilder();
		foreach (var field in flags)
		{
			bag.WriteBool(field.Name, field.Value);
			if (flagMap.Length > 0)
				flagMap.Append('\n');
			flagMap.Append(field.Name).Append('=').Append(field.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		bag.WriteString("RouteScreen", source);
		bag.WriteString("RouteList", list);
		bag.WriteString("RouteRow", rowId);
		bag.WriteInt("RouteIdField", idField);
		bag.WriteString("RouteMap", map.ToString());
		bag.WriteString("RouteFlags", flagMap.ToString());
	}

	public bool WriteBack()
	{
		var id = TopId();
		if (id.Length == 0)
			return false;
		var bag = Bag(id);
		var source = bag.ReadString("RouteScreen");
		var list = bag.ReadString("RouteList");
		var row = bag.ReadString("RouteRow");
		if (source.Length == 0 || list.Length == 0 || row.Length == 0 || !bag.TryReadInt("RouteIdField", out var idField))
			return false;
		var dest = Bag(source);
		if (!ApplyMap(bag, dest, list, row, idField, bag.ReadString("RouteMap"), flag: false))
			return false;
		return ApplyMap(bag, dest, list, row, idField, bag.ReadString("RouteFlags"), flag: true);
	}

	static bool ApplyMap(RecordProgram.Bag source, RecordProgram.Bag dest, string list, string row, int idField, string map, bool flag)
	{
		if (map.Length == 0)
			return true;
		foreach (var line in map.Split('\n'))
		{
			var eq = line.IndexOf('=');
			if (eq <= 0 || !int.TryParse(line[(eq + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
				return false;
			var name = line[..eq];
			var value = flag ? (source.ReadBool(name) ? "true" : "false") : source.ReadString(name);
			dest.WriteCell(list, row, idField, index, value);
		}

		return true;
	}

	public bool Pop()
	{
		lock (_gate)
		{
			if (_stack.Count <= 1)
				return false;
			_stack.RemoveAt(_stack.Count - 1);
			DropCache();
			StateEpoch.Bump();
			return true;
		}
	}

	internal RecordProgram.Bound? Top()
	{
		lock (_gate)
		{
			if (_stack.Count == 0)
				return null;
			var id = _stack[^1];
			Ensure(id);
			if (!_programs.TryGetValue(id, out var program))
				return null;
			var bag = BagLocked(id);
			if (_topBound is { } cached && ReferenceEquals(_topProgram, program) && ReferenceEquals(_topBag, bag))
			{
				TapTrace.Bind(hit: true);
				return cached;
			}

			var bound = RecordProgram.Bind(program, bag);
			_topProgram = program;
			_topBag = bag;
			_topBound = bound;
			TapTrace.Bind(hit: false);
			return bound;
		}
	}

	internal string UnderId()
	{
		lock (_gate)
			return _stack.Count < 2 ? "" : _stack[^2];
	}

	internal RecordProgram.Bound? Below()
	{
		lock (_gate)
		{
			if (_stack.Count < 2)
				return null;
			var id = _stack[^2];
			Ensure(id);
			if (!_programs.TryGetValue(id, out var program))
				return null;
			return RecordProgram.Bind(program, BagLocked(id));
		}
	}

	public bool DropUnresolved()
	{
		lock (_gate)
		{
			DropCache();
			while (_stack.Count > 0)
			{
				var id = _stack[^1];
				Ensure(id);
				if (_programs.ContainsKey(id) || _allowed.Contains(id))
					return true;
				_stack.RemoveAt(_stack.Count - 1);
			}

			return false;
		}
	}

	internal void Prepare(Action<byte[], RecordProgram.Bag> prepare)
	{
		ArgumentNullException.ThrowIfNull(prepare);
		List<(byte[] Program, RecordProgram.Bag Bag)> pairs;
		lock (_gate)
		{
			pairs = new List<(byte[], RecordProgram.Bag)>(_programs.Count);
			foreach (var pair in _programs)
				pairs.Add((pair.Value, BagLocked(pair.Key)));
		}

		foreach (var (program, bag) in pairs)
			prepare(program, bag);
	}

	public string Format()
	{
		lock (_gate)
		{
			var builder = new StringBuilder();
			for (var index = 0; index < _stack.Count; index++)
				builder.Append("stack ").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(_stack[index]).Append('\n');
			return builder.ToString();
		}
	}

	public NavCheckpoint Checkpoint()
	{
		List<(string Id, RecordProgram.Bag Bag, byte[] Program)> pairs;
		string[] stack;
		lock (_gate)
		{
			pairs = new List<(string, RecordProgram.Bag, byte[])>(_bags.Count);
			foreach (var pair in _bags)
				pairs.Add((pair.Key, pair.Value, _programs.TryGetValue(pair.Key, out var program) ? program : []));
			stack = _stack.ToArray();
		}

		pairs.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
		var screens = new List<NavScreen>(pairs.Count);
		foreach (var pair in pairs)
		{
			screens.Add(new NavScreen
			{
				Id = pair.Id,
				State = pair.Bag.Capture(),
				Program = pair.Program,
			});
		}

		return new NavCheckpoint
		{
			Stack = stack,
			Screens = screens,
		};
	}

	public void ResetBags()
	{
		lock (_gate)
		{
			foreach (var id in _bags.Keys.ToArray())
				_bags[id] = new RecordProgram.Bag();
			DropCache();
		}
	}

	public void Restore(NavCheckpoint nav)
	{
		ArgumentNullException.ThrowIfNull(nav);
		lock (_gate)
		{
			DropCache();
			_stack.Clear();
			_stack.AddRange(nav.Stack);
			StateEpoch.Bump();
			foreach (var screen in nav.Screens)
			{
				if (screen.Program is { Length: > 0 })
					_programs[screen.Id] = screen.Program.ToArray();
				BagLocked(screen.Id).Replace(screen.State);
			}
		}
	}

	void DropCache()
	{
		_topProgram = null;
		_topBag = null;
		_topBound = null;
	}

	void Ensure(string id)
	{
		if (_programs.ContainsKey(id))
			return;
		if (!_pending.Remove(id, out var program))
			return;
		var actual = IdOf(program);
		if (actual.Length > 0 && !string.Equals(actual, id, StringComparison.Ordinal))
			throw new InvalidOperationException("Staged screen " + id + " is " + actual + ".");
		_programs[id] = program;
		_ = BagLocked(id);
	}

	RecordProgram.Bag BagLocked(string id)
	{
		if (!_bags.TryGetValue(id, out var bag))
		{
			bag = new RecordProgram.Bag();
			_bags[id] = bag;
		}

		return bag;
	}

	static string IdOf(byte[] program)
	{
		var id = RecordProgram.ScreenId(program);
		return id.Length == 0 ? "screen" : id;
	}
}
