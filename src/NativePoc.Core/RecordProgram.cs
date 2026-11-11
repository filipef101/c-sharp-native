using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NativePoc;

public static partial class RecordProgram
{
	public const byte OpPushStr = 1;
	public const byte OpPushBool = 2;
	public const byte OpLoadStr = 3;
	public const byte OpLoadBool = 4;
	public const byte OpLoadInt = 5;
	public const byte OpConcat = 6;
	public const byte OpJumpIfFalse = 7;
	public const byte OpJump = 8;
	public const byte OpMakeText = 9;
	public const byte OpMakeTextKey = 10;
	public const byte OpMakeField = 11;
	public const byte OpMakeSwitch = 12;
	public const byte OpMakeButton = 13;
	public const byte OpMakeColumn = 14;
	public const byte OpMakeScroll = 15;
	public const byte OpReturn = 16;
	public const byte OpMark = 17;
	public const byte OpForEach = 18;
	public const byte OpItemStr = 19;
	public const byte OpGather = 20;
	public const byte OpStrEmpty = 21;
	public const byte OpMakeColumnKey = 22;
	public const byte OpListCount = 23;
	public const byte OpListFlagCount = 24;
	public const byte OpEqStr = 25;
	public const byte OpItemBool = 26;
	public const byte OpMakeList = 27;
	public const byte OpStyle = 28;
	public const byte OpMakeImage = 29;
	public const byte OpEffect = 30;
	public const byte OpEvery = 31;
	public const byte OpFieldMode = 32;
	public const byte OpMotion = 33;
	public const byte OpGesture = 34;
	public const byte OpHostEvent = 35;
	public const byte OpRes = 36;
	public const byte OpPlural = 37;
	public const byte OpFormat = 38;
	public const byte OpMakeHost = 39;

	public const int EagerRowLimit = 24;

	public const byte ActRender = 1;
	public const byte ActSetStr = 2;
	public const byte ActSetBool = 3;
	public const byte ActSetStrLit = 4;
	public const byte ActSetBoolLit = 5;
	public const byte ActInc = 6;
	public const byte ActCopyLit = 7;
	public const byte ActCopyProp = 8;
	public const byte ActCopyPrefixInt = 9;
	public const byte ActSetRowBool = 10;
	public const byte ActTakeStr = 11;
	public const byte ActTakeBool = 12;
	public const byte ActPulse = 13;
	public const byte ActCopyRows = 14;
	public const byte ActCommitDraft = 15;
	public const byte ActPush = 16;
	public const byte ActPop = 17;
	public const byte ActSetStrEmpty = 18;
	public const byte ActSetBoolLen = 19;
	public const byte ActModule = 20;
	public const byte ActDropRow = 21;
	public const byte ActEffect = 22;
	public const byte ActAddInt = 23;
	public const byte ActNotBool = 24;
	public const byte ActSetStrCat = 25;
	public const byte ActIf = 26;
	public const byte ActSetInt = 27;
	public const byte ActLocal = 28;
	public const byte ActAddRow = 29;
	public const byte ActClearList = 30;
	public const byte ActRemoveAt = 31;
	public const byte ActSetBoolSlot = 32;
	public const byte ActNotSlot = 33;
	public const byte ActVm = 34;

	public const int ActionStepBudget = 100_000;

	public const byte IntExprLit = 0;
	public const byte IntExprProp = 1;
	public const byte IntExprLocal = 2;
	public const byte IntExprAdd = 3;
	public const byte IntExprSub = 4;

	public const byte StrLit = 0;
	public const byte StrProp = 1;
	public const byte StrInt = 2;
	public const byte StrLocal = 3;
	public const byte StrIntLocal = 4;

	public const byte CondBool = 0;
	public const byte CondNotBool = 1;
	public const byte CondEmpty = 2;
	public const byte CondPresent = 3;
	public const byte CondStrEq = 4;
	public const byte CondIntEq = 5;
	public const byte CondIntNe = 6;
	public const byte CondLocal = 7;
	public const byte CondNotLocal = 8;
	public const byte CondStrNe = 9;
	public const byte CondLocalIntEq = 10;
	public const byte CondLit = 11;

	public const byte LocalInt = 1;
	public const byte LocalStr = 2;
	public const byte LocalBool = 3;

	public const string CounterScreenId = "counter";
	public const string PackingScreenId = "packing";

	public const byte TypeString = 1;
	public const byte TypeBool = 2;
	public const byte TypeInt = 3;
	public const byte TypeList = 4;
	public const byte TypeScreen = 5;

	public const int NoKey = 0xFFFF;

	public const int MaxRows = 2048;

	public static Bag Shared { get; } = new();

	public readonly record struct PropDef(
		byte Type,
		int Name,
		int Text,
		bool Flag,
		int Number,
		int[] Fields,
		int[][] Rows);

	public readonly struct Bound
	{
		readonly Bag _bag;
		readonly string[] _names;

		public Bound(Func<IPlatform, Element> render, string sourceHash, Bag bag, string[] names, string screenId)
		{
			Render = render;
			SourceHash = sourceHash;
			_bag = bag;
			_names = names;
			ScreenId = screenId;
		}

		public Func<IPlatform, Element> Render { get; }

		public string SourceHash { get; }

		public string ScreenId { get; }

		public Bag State => _bag;

		public bool Has(string name)
		{
			foreach (var item in _names)
			{
				if (item == name)
					return true;
			}

			return false;
		}

		public string FormatState() => _bag.Format();
	}

	public static Bound Bind(ReadOnlySpan<byte> payload) => Bind(payload, Shared);

	public static Bound Bind(ReadOnlySpan<byte> payload, Bag bag)
	{
		ArgumentNullException.ThrowIfNull(bag);
		var decoded = Decode(payload);
		Seed(decoded, bag);
		var code = decoded.Code;
		var strings = decoded.Strings;
		var names = NamesOf(decoded);
		return new Bound(
			platform =>
			{
				EffectContext.Bag = bag;
				try
				{
					return Eval(code, strings, bag, platform);
				}
				finally
				{
					EffectContext.Bag = null;
				}
			},
			decoded.SourceHash,
			bag,
			names,
			ScreenIdOf(decoded));
	}

	public static bool SharesAppCounter(ReadOnlySpan<byte> payload) =>
		string.Equals(ScreenId(payload), CounterScreenId, StringComparison.Ordinal);

	public static bool SharesPacking(ReadOnlySpan<byte> payload) =>
		string.Equals(ScreenId(payload), PackingScreenId, StringComparison.Ordinal);

	public static void SeedAppCounter(ReadOnlySpan<byte> payload, Bag bag, int count, string name, bool greetingFirst, bool plainRoot)
	{
		ArgumentNullException.ThrowIfNull(bag);
		if (!SharesAppCounter(payload))
			return;
		var names = Names(payload);
		if (!ContainsName(names, "Count"))
			return;
		bag.SeedInt("Count", count);
		if (ContainsName(names, "Name"))
			bag.SeedString("Name", name ?? "");
		if (ContainsName(names, "GreetingFirst"))
			bag.SeedBool("GreetingFirst", greetingFirst);
		if (ContainsName(names, "PlainRoot"))
			bag.SeedBool("PlainRoot", plainRoot);
	}

	public static bool PublishAppCounter(Bound program, int count, string name, bool greetingFirst, bool plainRoot, out int nextCount, out string nextName, out bool nextGreeting, out bool nextPlain)
	{
		nextCount = count;
		nextName = name ?? "";
		nextGreeting = greetingFirst;
		nextPlain = plainRoot;
		if (!string.Equals(program.ScreenId, CounterScreenId, StringComparison.Ordinal) || !program.Has("Count"))
			return false;
		nextCount = program.State.ReadInt("Count");
		if (program.Has("Name"))
			nextName = program.State.ReadString("Name");
		if (program.Has("GreetingFirst"))
			nextGreeting = program.State.ReadBool("GreetingFirst");
		if (program.Has("PlainRoot"))
			nextPlain = program.State.ReadBool("PlainRoot");
		return true;
	}

	public static string KeptNote(ReadOnlySpan<byte> payload, Bag bag)
	{
		ArgumentNullException.ThrowIfNull(bag);
		var decoded = Decode(payload);
		var parts = new List<string>();
		foreach (var prop in decoded.Props)
		{
			var name = decoded.Strings[prop.Name];
			switch (prop.Type)
			{
				case TypeString:
					if (!bag.TryReadString(name, out var liveText))
						continue;
					var initialText = decoded.Strings[prop.Text];
					if (!string.Equals(liveText, initialText, StringComparison.Ordinal))
						parts.Add(name + "=\"" + OneLine(liveText) + "\" (initializer \"" + OneLine(initialText) + "\")");
					break;
				case TypeBool:
					if (!bag.TryReadBool(name, out var liveFlag))
						continue;
					if (liveFlag != prop.Flag)
						parts.Add(name + "=" + (liveFlag ? "true" : "false") + " (initializer " + (prop.Flag ? "true" : "false") + ")");
					break;
				case TypeInt:
					if (!bag.TryReadInt(name, out var liveNumber))
						continue;
					if (liveNumber != prop.Number)
						parts.Add(name + "=" + liveNumber.ToString(CultureInfo.InvariantCulture) + " (initializer " + prop.Number.ToString(CultureInfo.InvariantCulture) + ")");
					break;
			}
		}

		if (parts.Count == 0)
			return "";
		return "state kept " + string.Join(", ", parts) + ". cnative reset-state";
	}

	static string ScreenIdOf(Decoded decoded)
	{
		foreach (var prop in decoded.Props)
		{
			if (prop.Type == TypeScreen)
				return decoded.Strings[prop.Text];
		}

		return "";
	}

	static bool ContainsName(string[] names, string name)
	{
		foreach (var item in names)
		{
			if (item == name)
				return true;
		}

		return false;
	}

	static string OneLine(string value)
	{
		return value
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\r", "", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal);
	}

	public static string[] Names(ReadOnlySpan<byte> payload) => NamesOf(Decode(payload));

	public static string ScreenId(ReadOnlySpan<byte> payload)
	{
		var decoded = Decode(payload);
		foreach (var prop in decoded.Props)
		{
			if (prop.Type == TypeScreen)
				return decoded.Strings[prop.Text];
		}

		return "";
	}

	static string[] NamesOf(Decoded decoded)
	{
		var names = new List<string>();
		foreach (var prop in decoded.Props)
		{
			if (prop.Type == TypeScreen)
				continue;
			names.Add(decoded.Strings[prop.Name]);
		}

		return names.ToArray();
	}

	public readonly record struct ListShape(string Name, string[] Fields);

	public static ListShape[] Lists(ReadOnlySpan<byte> payload)
	{
		var decoded = Decode(payload);
		var found = new List<ListShape>();
		foreach (var prop in decoded.Props)
		{
			if (prop.Type != TypeList)
				continue;
			var fields = new string[prop.Fields.Length];
			for (var index = 0; index < prop.Fields.Length; index++)
				fields[index] = decoded.Strings[prop.Fields[index]];
			found.Add(new ListShape(decoded.Strings[prop.Name], fields));
		}

		return found.ToArray();
	}

	public static byte[] Encode(IReadOnlyList<string> strings, IReadOnlyList<PropDef> props, ReadOnlySpan<byte> code, string? source)
	{
		ArgumentNullException.ThrowIfNull(strings);
		ArgumentNullException.ThrowIfNull(props);
		if (code.Length == 0 || code.Length > ScreenProgram.MaxCode)
			throw new InvalidOperationException("A record program has no body.");
		var hash = new byte[16];
		if (source is not null)
			SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 16).CopyTo(hash);
		var size = 8 + 16;
		foreach (var text in strings)
			size += 2 + Encoding.UTF8.GetByteCount(text);
		size += 2;
		foreach (var prop in props)
			size += PropSize(prop);
		size += 2 + code.Length;
		var payload = new byte[size];
		payload[0] = (byte)'N';
		payload[1] = (byte)'P';
		payload[2] = (byte)'C';
		payload[3] = (byte)'1';
		payload[4] = ScreenProgram.Version;
		payload[5] = 2;
		var cursor = 6;
		cursor = WriteU16(payload, cursor, strings.Count);
		foreach (var text in strings)
		{
			var encoded = Encoding.UTF8.GetBytes(text);
			cursor = WriteU16(payload, cursor, encoded.Length);
			encoded.CopyTo(payload, cursor);
			cursor += encoded.Length;
		}

		cursor = WriteU16(payload, cursor, props.Count);
		foreach (var prop in props)
			cursor = WriteProp(payload, cursor, prop);
		cursor = WriteU16(payload, cursor, code.Length);
		code.CopyTo(payload.AsSpan(cursor));
		cursor += code.Length;
		hash.CopyTo(payload, cursor);
		return payload;
	}

	static int PropSize(PropDef prop)
	{
		var size = 1 + 2;
		switch (prop.Type)
		{
			case TypeString:
				return size + 2;
			case TypeBool:
				return size + 1;
			case TypeInt:
				return size + 4;
			case TypeList:
				size += 2 + (prop.Fields.Length * 2) + 2;
				size += prop.Rows.Length * prop.Fields.Length * 2;
				return size;
			case TypeScreen:
				return size + 2;
			default:
				throw new InvalidOperationException("A record property is out of range.");
		}
	}

	static int WriteProp(byte[] payload, int cursor, PropDef prop)
	{
		payload[cursor++] = prop.Type;
		cursor = WriteU16(payload, cursor, prop.Name);
		switch (prop.Type)
		{
			case TypeString:
				return WriteU16(payload, cursor, prop.Text);
			case TypeBool:
				payload[cursor++] = prop.Flag ? (byte)1 : (byte)0;
				return cursor;
			case TypeInt:
				payload[cursor++] = (byte)prop.Number;
				payload[cursor++] = (byte)(prop.Number >> 8);
				payload[cursor++] = (byte)(prop.Number >> 16);
				payload[cursor++] = (byte)(prop.Number >> 24);
				return cursor;
			case TypeList:
				cursor = WriteU16(payload, cursor, prop.Fields.Length);
				foreach (var field in prop.Fields)
					cursor = WriteU16(payload, cursor, field);
				cursor = WriteU16(payload, cursor, prop.Rows.Length);
				foreach (var row in prop.Rows)
				{
					if (row.Length != prop.Fields.Length)
						throw new InvalidOperationException("A record row is the wrong width.");
					foreach (var cell in row)
						cursor = WriteU16(payload, cursor, cell);
				}

				return cursor;
			case TypeScreen:
				return WriteU16(payload, cursor, prop.Text);
			default:
				throw new InvalidOperationException("A record property is out of range.");
		}
	}

	static int WriteU16(byte[] payload, int cursor, int value)
	{
		payload[cursor] = (byte)value;
		payload[cursor + 1] = (byte)(value >> 8);
		return cursor + 2;
	}

	readonly record struct Decoded(string[] Strings, PropDef[] Props, byte[] Code, string SourceHash);

	static Decoded Decode(ReadOnlySpan<byte> payload)
	{
		if (!ScreenProgram.IsRecord(payload) || payload.Length < 8 + 16 || payload[4] != ScreenProgram.Version)
			throw new InvalidOperationException("Rejected record program.");
		var cursor = 6;
		var stringCount = ReadU16(payload, ref cursor);
		if (stringCount > ScreenProgram.MaxStrings)
			throw new InvalidOperationException("Rejected record program.");
		var strings = new string[stringCount];
		for (var index = 0; index < stringCount; index++)
		{
			var length = ReadU16(payload, ref cursor);
			if (length > ScreenProgram.MaxStringBytes || cursor + length > payload.Length - 16)
				throw new InvalidOperationException("Rejected record program.");
			strings[index] = Encoding.UTF8.GetString(payload.Slice(cursor, length));
			cursor += length;
		}

		var propCount = ReadU16(payload, ref cursor);
		var props = new PropDef[propCount];
		for (var index = 0; index < propCount; index++)
			props[index] = ReadProp(payload, ref cursor, stringCount);
		var codeLength = ReadU16(payload, ref cursor);
		if (codeLength == 0 || codeLength > ScreenProgram.MaxCode || cursor + codeLength + 16 != payload.Length)
			throw new InvalidOperationException("Rejected record program.");
		var code = payload.Slice(cursor, codeLength).ToArray();
		var hash = payload.Slice(cursor + codeLength, 16);
		var sourceHash = Convert.ToHexString(hash).ToLowerInvariant();
		if (hash.IndexOf((byte)0) >= 0 && hash.Trim((byte)0).Length == 0)
			sourceHash = "";
		return new Decoded(strings, props, code, sourceHash);
	}

	static PropDef ReadProp(ReadOnlySpan<byte> payload, ref int cursor, int stringCount)
	{
		if (cursor >= payload.Length - 16)
			throw new InvalidOperationException("Rejected record program.");
		var type = payload[cursor++];
		var name = ReadU16(payload, ref cursor);
		if (name >= stringCount)
			throw new InvalidOperationException("Rejected record program.");
		switch (type)
		{
			case TypeString:
				var text = ReadU16(payload, ref cursor);
				if (text >= stringCount)
					throw new InvalidOperationException("Rejected record program.");
				return new PropDef(type, name, text, false, 0, Array.Empty<int>(), Array.Empty<int[]>());
			case TypeBool:
				if (cursor >= payload.Length - 16)
					throw new InvalidOperationException("Rejected record program.");
				var flag = payload[cursor++];
				if (flag > 1)
					throw new InvalidOperationException("Rejected record program.");
				return new PropDef(type, name, 0, flag == 1, 0, Array.Empty<int>(), Array.Empty<int[]>());
			case TypeInt:
				if (cursor + 4 > payload.Length - 16)
					throw new InvalidOperationException("Rejected record program.");
				var number = payload[cursor] | (payload[cursor + 1] << 8) | (payload[cursor + 2] << 16) | (payload[cursor + 3] << 24);
				cursor += 4;
				return new PropDef(type, name, 0, false, number, Array.Empty<int>(), Array.Empty<int[]>());
			case TypeList:
				var fieldCount = ReadU16(payload, ref cursor);
				if (fieldCount == 0 || fieldCount > 16)
					throw new InvalidOperationException("Rejected record program.");
				var fields = new int[fieldCount];
				for (var field = 0; field < fieldCount; field++)
				{
					fields[field] = ReadU16(payload, ref cursor);
					if (fields[field] >= stringCount)
						throw new InvalidOperationException("Rejected record program.");
				}

				var rowCount = ReadU16(payload, ref cursor);
				if (rowCount > 2048)
					throw new InvalidOperationException("Rejected record program.");
				var rows = new int[rowCount][];
				for (var row = 0; row < rowCount; row++)
				{
					rows[row] = new int[fieldCount];
					for (var cell = 0; cell < fieldCount; cell++)
					{
						rows[row][cell] = ReadU16(payload, ref cursor);
						if (rows[row][cell] >= stringCount)
							throw new InvalidOperationException("Rejected record program.");
					}
				}

				return new PropDef(type, name, 0, false, 0, fields, rows);
			case TypeScreen:
				var screen = ReadU16(payload, ref cursor);
				if (screen >= stringCount)
					throw new InvalidOperationException("Rejected record program.");
				return new PropDef(type, name, screen, false, 0, Array.Empty<int>(), Array.Empty<int[]>());
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static int ReadU16(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 2 > payload.Length)
			throw new InvalidOperationException("Rejected record program.");
		var value = payload[cursor] | (payload[cursor + 1] << 8);
		cursor += 2;
		return value;
	}

	static void Seed(Decoded decoded, Bag bag)
	{
		foreach (var prop in decoded.Props)
		{
			var name = decoded.Strings[prop.Name];
			switch (prop.Type)
			{
				case TypeString:
					bag.SeedString(name, decoded.Strings[prop.Text]);
					break;
				case TypeBool:
					bag.SeedBool(name, prop.Flag);
					break;
				case TypeInt:
					bag.SeedInt(name, prop.Number);
					break;
				case TypeList:
					var rows = new List<string[]>(prop.Rows.Length);
					foreach (var row in prop.Rows)
					{
						var cells = new string[row.Length];
						for (var index = 0; index < row.Length; index++)
							cells[index] = decoded.Strings[row[index]];
						rows.Add(cells);
					}

					bag.SeedList(name, rows);
					break;
				case TypeScreen:
					break;
				default:
					throw new InvalidOperationException("Rejected record program.");
			}
		}
	}

	static Element Eval(byte[] code, string[] strings, Bag bag, IPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);
		var stack = new Slot[ScreenProgram.MaxStack];
		var sp = 0;
		string[]? row = null;
		Element? returned = null;
		var ip = 0;
		while (ip < code.Length)
		{
			ip = Step(code, strings, bag, platform, stack, ref sp, ref row, ip, code.Length, ref returned);
			if (returned is not null)
				return returned;
		}

		throw new InvalidOperationException("Rejected record program.");
	}

	static int Step(
		byte[] code,
		string[] strings,
		Bag bag,
		IPlatform platform,
		Slot[] stack,
		ref int sp,
		ref string[]? row,
		int ip,
		int end,
		ref Element? returned)
	{
		if (ip >= end)
			throw new InvalidOperationException("Rejected record program.");
		var op = code[ip++];
		switch (op)
		{
			case OpPushStr:
			{
				var index = TakeU16(code, ref ip, end);
				Push(stack, ref sp, Slot.Str(strings[RequireString(index, strings)]));
				return ip;
			}
			case OpPushBool:
			{
				if (ip >= end)
					throw new InvalidOperationException("Rejected record program.");
				var flag = code[ip++];
				if (flag > 1)
					throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Bool(flag == 1));
				return ip;
			}
			case OpLoadStr:
				Push(stack, ref sp, Slot.Str(bag.ReadString(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
				return ip;
			case OpLoadBool:
				Push(stack, ref sp, Slot.Bool(bag.ReadBool(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
				return ip;
			case OpLoadInt:
				Push(stack, ref sp, Slot.Str(bag.ReadInt(strings[RequireString(TakeU16(code, ref ip, end), strings)]).ToString(CultureInfo.InvariantCulture)));
				return ip;
			case OpConcat:
			{
				var right = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var left = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Str(left + right));
				return ip;
			}
			case OpJumpIfFalse:
			{
				var relative = TakeU16(code, ref ip, end);
				var flag = Pop(stack, ref sp, SlotKind.Bool).Flag;
				if (!flag)
					ip += relative;
				if (ip > end)
					throw new InvalidOperationException("Rejected record program.");
				return ip;
			}
			case OpJump:
			{
				var relative = TakeU16(code, ref ip, end);
				ip += relative;
				if (ip > end)
					throw new InvalidOperationException("Rejected record program.");
				return ip;
			}
			case OpMakeText:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var value = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Elem(new Text(value) { Key = key }));
				return ip;
			}
			case OpMakeTextKey:
			{
				var key = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var value = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Elem(new Text(value) { Key = key }));
				return ip;
			}
			case OpStrEmpty:
			{
				var text = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Bool(text.Length == 0));
				return ip;
			}
			case OpEqStr:
			{
				var right = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var left = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Bool(left == right));
				return ip;
			}
			case OpListCount:
				Push(stack, ref sp, Slot.Str(bag.CountRows(strings[RequireString(TakeU16(code, ref ip, end), strings)]).ToString(CultureInfo.InvariantCulture)));
				return ip;
			case OpListFlagCount:
			{
				var name = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				var field = TakeU16(code, ref ip, end);
				Push(stack, ref sp, Slot.Str(bag.CountFlag(name, field).ToString(CultureInfo.InvariantCulture)));
				return ip;
			}
			case OpItemBool:
			{
				var field = TakeU16(code, ref ip, end);
				var current = row ?? throw new InvalidOperationException("Rejected record program.");
				if ((uint)field >= (uint)current.Length)
					throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Bool(current[field] == "true"));
				return ip;
			}
			case OpMakeColumnKey:
			{
				if (ip >= end)
					throw new InvalidOperationException("Rejected record program.");
				var count = code[ip++];
				var key = Pop(stack, ref sp, SlotKind.Str).Text;
				Push(stack, ref sp, Slot.Elem(TakeColumn(stack, ref sp, count, key)));
				return ip;
			}
			case OpMakeField:
			case OpMakeSwitch:
			case OpMakeButton:
				return MakeControl(op, code, strings, bag, platform, stack, ref sp, row, ip, end);
			case OpMakeColumn:
			{
				if (ip >= end)
					throw new InvalidOperationException("Rejected record program.");
				var count = code[ip++];
				var key = Key(strings, TakeU16(code, ref ip, end));
				Push(stack, ref sp, Slot.Elem(TakeColumn(stack, ref sp, count, key)));
				return ip;
			}
			case OpMakeScroll:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var child = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Elem(new ScrollView(child) { Key = key }));
				return ip;
			}
			case OpReturn:
				returned = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
				return ip;
			case OpMark:
				Push(stack, ref sp, Slot.Mark());
				return ip;
			case OpForEach:
			{
				var name = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				var length = TakeU16(code, ref ip, end);
				var bodyEnd = ip + length;
				if (bodyEnd > end)
					throw new InvalidOperationException("Rejected record program.");
				var rows = bag.Rows(name);
				if (rows.Count > EagerRowLimit)
				{
					Push(stack, ref sp, Slot.Elem(new ListView(new WindowList(code, strings, bag, platform, rows, ip, bodyEnd)) { Key = "window" }));
					row = null;
					return bodyEnd;
				}

				foreach (var item in rows)
				{
					row = item;
					var cursor = ip;
					while (cursor < bodyEnd)
						cursor = Step(code, strings, bag, platform, stack, ref sp, ref row, cursor, bodyEnd, ref returned);
					if (returned is not null)
						throw new InvalidOperationException("Rejected record program.");
				}

				row = null;
				return bodyEnd;
			}
			case OpItemStr:
			{
				var field = TakeU16(code, ref ip, end);
				var current = row ?? throw new InvalidOperationException("Rejected record program.");
				if ((uint)field >= (uint)current.Length)
					throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Str(current[field]));
				return ip;
			}
			case OpGather:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var gathered = new List<Element>();
				while (sp > 0 && stack[sp - 1].Kind != SlotKind.Mark)
					gathered.Add(Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program."));
				if (sp == 0 || Pop(stack, ref sp, SlotKind.Mark).Kind != SlotKind.Mark)
					throw new InvalidOperationException("Rejected record program.");
				if (gathered.Count > MaxRows)
					throw new InvalidOperationException("Rejected record program.");
				gathered.Reverse();
				Push(stack, ref sp, Slot.Elem(new Column(gathered) { Key = key }));
				return ip;
			}
			case OpMakeList:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var gathered = new List<Element>();
				while (sp > 0 && stack[sp - 1].Kind != SlotKind.Mark)
					gathered.Add(Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program."));
				if (sp == 0 || Pop(stack, ref sp, SlotKind.Mark).Kind != SlotKind.Mark)
					throw new InvalidOperationException("Rejected record program.");
				if (gathered.Count > MaxRows)
					throw new InvalidOperationException("Rejected record program.");
				gathered.Reverse();
				if (gathered.Count == 1 && gathered[0] is ListView window && window.Rows is WindowList)
				{
					Push(stack, ref sp, Slot.Elem(window with { Key = key }));
					return ip;
				}

				Push(stack, ref sp, Slot.Elem(new ListView(gathered) { Key = key }));
				return ip;
			}
			case OpStyle:
			{
				if (ip + StyleCodec.Bytes > end)
					throw new InvalidOperationException("Rejected record program.");
				var style = StyleCodec.Read(code.AsSpan(ip, StyleCodec.Bytes));
				ip += StyleCodec.Bytes;
				var node = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Elem(WithStyle(node, style)));
				return ip;
			}
			case OpMakeImage:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var source = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Elem(new Image(source) { Key = key }));
				return ip;
			}
			case OpEffect:
			{
				var property = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				var resource = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				platform.Effect(property, resource);
				return ip;
			}
			case OpEvery:
			{
				var property = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				platform.Every(property);
				return ip;
			}
			case OpFieldMode:
			{
				if (ip + 5 > end)
					throw new InvalidOperationException("Rejected record program.");
				var keyboard = (Keyboard)code[ip];
				var lines = code[ip + 1];
				var submit = (Submit)code[ip + 2];
				ip += 3;
				if (!Enum.IsDefined(keyboard) || !Enum.IsDefined(submit))
					throw new InvalidOperationException("Rejected record program.");
				var length = TakeU16(code, ref ip, end);
				if (ip + length > end)
					throw new InvalidOperationException("Rejected record program.");
				var script = code.AsSpan(ip, length).ToArray();
				ip += length;
				if (Pop(stack, ref sp, SlotKind.Elem).Node is not TextField field)
					throw new InvalidOperationException("Rejected record program.");
				Action? onSubmit = script.Length == 0
					? null
					: () => RunScript(script, strings, bag, platform, null, false, null);
				Push(stack, ref sp, Slot.Elem(field with
				{
					Keyboard = keyboard,
					Lines = lines,
					Submit = submit,
					OnSubmit = onSubmit,
				}));
				return ip;
			}
			case OpMotion:
			{
				if (ip + MotionCodec.Bytes > end)
					throw new InvalidOperationException("Rejected record program.");
				var motion = MotionCodec.Read(code.AsSpan(ip, MotionCodec.Bytes));
				ip += MotionCodec.Bytes;
				var node = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Elem(WithMotion(node, motion)));
				return ip;
			}
			case OpGesture:
			{
				if (ip >= end)
					throw new InvalidOperationException("Rejected record program.");
				var kind = code[ip++];
				if (kind is not (Gestures.Tap or Gestures.LongPress or Gestures.Swipe or Gestures.Refresh))
					throw new InvalidOperationException("Rejected record program.");
				var length = TakeU16(code, ref ip, end);
				if (length == 0 || ip + length > end)
					throw new InvalidOperationException("Rejected record program.");
				var script = code.AsSpan(ip, length).ToArray();
				ip += length;
				var node = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
				var rowId = row is { Length: > 0 } ? row[0] : null;
				Action run = () => RunScript(script, strings, bag, platform, null, false, rowId);
				Push(stack, ref sp, Slot.Elem(WithGesture(node, kind, run)));
				return ip;
			}
			case OpMakeHost:
			{
				var key = Key(strings, TakeU16(code, ref ip, end));
				var value = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var prop = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var kind = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				if (kind.Length == 0 || prop.Length == 0)
					throw new InvalidOperationException("Rejected record program.");
				Push(stack, ref sp, Slot.Elem(new HostView(kind, prop, value) { Key = key }));
				return ip;
			}
			case OpHostEvent:
			{
				var eventName = strings[RequireString(TakeU16(code, ref ip, end), strings)];
				var length = TakeU16(code, ref ip, end);
				if (ip + length > end)
					throw new InvalidOperationException("Rejected record program.");
				var script = code.AsSpan(ip, length).ToArray();
				ip += length;
				if (Pop(stack, ref sp, SlotKind.Elem).Node is not HostView host)
					throw new InvalidOperationException("Rejected record program.");
				Action<string, string>? onEvent = script.Length == 0
					? null
					: (name, payload) =>
					{
						if (eventName.Length > 0 && !string.Equals(name, eventName, StringComparison.Ordinal))
							return;
						RunScript(script, strings, bag, platform, payload, false, null);
					};
				Push(stack, ref sp, Slot.Elem(host with { Event = eventName, OnEvent = onEvent }));
				return ip;
			}
			case OpRes:
			{
				var key = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Str(StringTable.Get(key)));
				return ip;
			}
			case OpPlural:
			{
				var other = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var one = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var count = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Str(StringTable.Plural(count, one, other)));
				return ip;
			}
			case OpFormat:
			{
				if (ip >= end)
					throw new InvalidOperationException("Rejected record program.");
				var argc = code[ip++];
				if (argc > 8)
					throw new InvalidOperationException("Rejected record program.");
				var args = new string[argc];
				for (var index = argc - 1; index >= 0; index--)
					args[index] = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var template = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Str(StringTable.Format(template, args)));
				return ip;
			}
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static Element WithGesture(Element element, byte kind, Action run)
	{
		var mask = (byte)(element.Gesture | kind);
		var tap = kind == Gestures.Tap ? run : element.OnTap;
		var longPress = kind == Gestures.LongPress ? run : element.OnLongPress;
		var swipe = kind == Gestures.Swipe ? run : element.OnSwipe;
		var refresh = kind == Gestures.Refresh ? run : element.OnRefresh;
		return element switch
		{
			Column column => column with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			Text text => text with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			Button button => button with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			TextField field => field with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			Switch toggle => toggle with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			ScrollView scroll => scroll with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			ListView list => list with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			Image image => image with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			HostView host => host with { Gesture = mask, OnTap = tap, OnLongPress = longPress, OnSwipe = swipe, OnRefresh = refresh },
			_ => throw new InvalidOperationException("Rejected record program."),
		};
	}

	static Element WithMotion(Element element, Motion motion) =>
		element switch
		{
			Column column => column with { Motion = motion },
			Text text => text with { Motion = motion },
			Button button => button with { Motion = motion },
			TextField field => field with { Motion = motion },
			Switch toggle => toggle with { Motion = motion },
			ScrollView scroll => scroll with { Motion = motion },
			ListView list => list with { Motion = motion },
			Image image => image with { Motion = motion },
			HostView host => host with { Motion = motion },
			_ => throw new InvalidOperationException("Rejected record program."),
		};

	static Element WithStyle(Element element, Style style) =>
		element switch
		{
			Column column => column with { Style = style },
			Text text => text with { Style = style },
			Button button => button with { Style = style },
			TextField field => field with { Style = style },
			Switch toggle => toggle with { Style = style },
			ScrollView scroll => scroll with { Style = style },
			ListView list => list with { Style = style },
			Image image => image with { Style = style },
			HostView host => host with { Style = style },
			_ => throw new InvalidOperationException("Rejected record program."),
		};

	static int MakeControl(
		byte op,
		byte[] code,
		string[] strings,
		Bag bag,
		IPlatform platform,
		Slot[] stack,
		ref int sp,
		string[]? row,
		int ip,
		int end)
	{
		var key = Key(strings, TakeU16(code, ref ip, end));
		var length = TakeU16(code, ref ip, end);
		if (length == 0 || ip + length > end)
			throw new InvalidOperationException("Rejected record program.");
		var script = code.AsSpan(ip, length).ToArray();
		ip += length;
		var rowId = row is { Length: > 0 } ? row[0] : null;
		switch (op)
		{
			case OpMakeField:
			{
				var label = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var placeholder = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var value = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Elem(new TextField(
					value,
					text => RunScript(script, strings, bag, platform, text, false, rowId),
					placeholder,
					label)
				{ Key = key }));
				return ip;
			}
			case OpMakeSwitch:
			{
				var label = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var value = Pop(stack, ref sp, SlotKind.Bool).Flag;
				Push(stack, ref sp, Slot.Elem(new Switch(
					value,
					next => RunScript(script, strings, bag, platform, null, next, rowId),
					label)
				{ Key = key }));
				return ip;
			}
			case OpMakeButton:
			{
				var label = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				var title = Pop(stack, ref sp, SlotKind.Str).Text ?? "";
				Push(stack, ref sp, Slot.Elem(new Button(
					title,
					() => RunScript(script, strings, bag, platform, null, false, rowId),
					label)
				{ Key = key }));
				return ip;
			}
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static void RunScript(byte[] script, string[] strings, Bag bag, IPlatform platform, string? text, bool flag, string? rowId)
	{
		var previous = EffectContext.Bag;
		EffectContext.Bag = bag;
		var frame = new HandlerFrame();
		string? stop = null;
		var clock = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			RunSteps(script, strings, bag, platform, text, flag, rowId, frame, 0);
		}
		catch (ActionStoppedException ex)
		{
			stop = ex.Message;
			platform.RequestRender();
		}
		finally
		{
			clock.Stop();
			EffectContext.Bag = previous;
			var micros = clock.ElapsedTicks * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
			ActionLog.Note(micros, frame.Steps, stop);
		}
	}

	static void RunSteps(byte[] script, string[] strings, Bag bag, IPlatform platform, string? text, bool flag, string? rowId, HandlerFrame frame, int depth) =>
		RunSteps(script, strings, bag, platform, text, flag, rowId, frame, depth, 0, script.Length);

	static void RunSteps(byte[] script, string[] strings, Bag bag, IPlatform platform, string? text, bool flag, string? rowId, HandlerFrame frame, int depth, int origin, int limit)
	{
		if (depth > 8)
			throw new InvalidOperationException("Rejected record program.");
		var ip = origin;
		var end = origin + limit;
		if (end > script.Length)
			throw new InvalidOperationException("Rejected record program.");
		while (ip < end)
		{
			frame.Tick();
			var op = script[ip++];
			switch (op)
			{
				case ActRender:
					platform.RequestRender();
					break;
				case ActSetStr:
					bag.WriteString(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)], text ?? "");
					break;
				case ActSetBool:
					bag.WriteBool(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)], flag);
					break;
				case ActSetStrLit:
				{
					var name = RequireString(TakeU16(script, ref ip, script.Length), strings);
					var literal = RequireString(TakeU16(script, ref ip, script.Length), strings);
					bag.WriteString(strings[name], strings[literal]);
					break;
				}
				case ActSetBoolLit:
				{
					var name = RequireString(TakeU16(script, ref ip, script.Length), strings);
					if (ip >= script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var value = script[ip++];
					if (value > 1)
						throw new InvalidOperationException("Rejected record program.");
					bag.WriteBool(strings[name], value == 1);
					break;
				}
				case ActInc:
					bag.AddInt(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)], 1);
					break;
				case ActCopyLit:
					platform.CopyNonce(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
					break;
				case ActCopyProp:
					platform.CopyNonce(bag.ReadString(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]));
					break;
				case ActCopyPrefixInt:
				{
					var prefix = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					platform.CopyNonce(prefix + bag.ReadInt(name).ToString(CultureInfo.InvariantCulture));
					break;
				}
				case ActSetRowBool:
				{
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var field = TakeU16(script, ref ip, script.Length);
					bag.WriteCell(list, rowId ?? throw new InvalidOperationException("Rejected record program."), 0, field, flag ? "true" : "false");
					break;
				}
				case ActTakeStr:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var field = TakeU16(script, ref ip, script.Length);
					bag.WriteString(dest, bag.Cell(list, rowId ?? throw new InvalidOperationException("Rejected record program."), 0, field));
					break;
				}
				case ActTakeBool:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var field = TakeU16(script, ref ip, script.Length);
					bag.WriteBool(dest, bag.Cell(list, rowId ?? throw new InvalidOperationException("Rejected record program."), 0, field) == "true");
					break;
				}
				case ActPulse:
					platform.Pulse();
					break;
				case ActCopyRows:
				{
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var textField = TakeU16(script, ref ip, script.Length);
					var flagField = TakeU16(script, ref ip, script.Length);
					var label = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					platform.CopyText(label, bag.RowLines(list, textField, flagField));
					break;
				}
				case ActCommitDraft:
					if (!CommitDraft(script, strings, bag, ref ip))
						return;
					break;
				case ActPush:
					platform.Push(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
					break;
				case ActPop:
					platform.Pop();
					break;
				case ActSetStrEmpty:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var source = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var whenEmpty = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var whenFull = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteString(dest, bag.ReadString(source).Length == 0 ? whenEmpty : whenFull);
					break;
				}
				case ActSetBoolLen:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var source = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteBool(dest, bag.ReadString(source).Length != 0);
					break;
				}
				case ActModule:
				{
					var module = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var method = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					if (ip >= script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var count = script[ip++];
					var moduleArgs = new string[count];
					for (var index = 0; index < count; index++)
					{
						if (ip >= script.Length)
							throw new InvalidOperationException("Rejected record program.");
						var kind = script[ip++];
						var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
						moduleArgs[index] = kind switch
						{
							0 => name,
							1 => bag.ReadString(name),
							2 => bag.ReadBool(name) ? "true" : "false",
							_ => throw new InvalidOperationException("Rejected record program."),
						};
					}

					platform.Module(module, method, moduleArgs);
					break;
				}
				case ActDropRow:
				{
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var field = TakeU16(script, ref ip, script.Length);
					bag.RemoveRow(list, rowId ?? throw new InvalidOperationException("Rejected record program."), field);
					break;
				}
				case ActEffect:
				{
					var property = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var resource = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					platform.Effect(property, resource);
					break;
				}
				case ActAddInt:
				{
					var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.AddInt(name, ReadIntExpr(script, ref ip, strings, bag, frame));
					break;
				}
				case ActNotBool:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					var source = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteBool(dest, !bag.ReadBool(source));
					break;
				}
				case ActSetStrCat:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					if (ip >= script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var count = script[ip++];
					var builder = new StringBuilder();
					for (var index = 0; index < count; index++)
						builder.Append(ReadStrPart(script, ref ip, strings, bag, frame));
					bag.WriteString(dest, builder.ToString());
					break;
				}
				case ActIf:
				{
					var condition = ReadCond(script, ref ip, strings, bag, frame);
					var thenLen = TakeU16(script, ref ip, script.Length);
					if (ip + thenLen > script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var thenAt = ip;
					ip += thenLen;
					var elseLen = TakeU16(script, ref ip, script.Length);
					if (ip + elseLen > script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var elseAt = ip;
					ip += elseLen;
					var chosenAt = condition ? thenAt : elseAt;
					var chosenLen = condition ? thenLen : elseLen;
					if (chosenLen > 0)
						RunSteps(script.AsSpan(chosenAt, chosenLen).ToArray(), strings, bag, platform, text, flag, rowId, frame, depth + 1);
					break;
				}
				case ActSetInt:
				{
					var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteInt(name, ReadIntExpr(script, ref ip, strings, bag, frame));
					break;
				}
				case ActLocal:
				{
					var slot = ReadSlot(script, ref ip);
					if (ip >= script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var type = script[ip++];
					switch (type)
					{
						case LocalInt:
							frame.Ints[slot] = ReadIntExpr(script, ref ip, strings, bag, frame);
							break;
						case LocalStr:
							if (ip >= script.Length)
								throw new InvalidOperationException("Rejected record program.");
							var pieces = script[ip++];
							var builder = new StringBuilder();
							for (var index = 0; index < pieces; index++)
								builder.Append(ReadStrPart(script, ref ip, strings, bag, frame));
							frame.Strings[slot] = builder.ToString();
							break;
						case LocalBool:
							frame.Flags[slot] = ReadCond(script, ref ip, strings, bag, frame);
							break;
						default:
							throw new InvalidOperationException("Rejected record program.");
					}

					break;
				}
				case ActAddRow:
				{
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					if (ip >= script.Length)
						throw new InvalidOperationException("Rejected record program.");
					var width = script[ip++];
					var cells = new string[width];
					for (var index = 0; index < width; index++)
						cells[index] = ReadStrPart(script, ref ip, strings, bag, frame);
					bag.AddRow(list, cells);
					break;
				}
				case ActClearList:
					bag.ClearList(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
					break;
				case ActRemoveAt:
				{
					var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.RemoveAt(list, ReadIntExpr(script, ref ip, strings, bag, frame));
					break;
				}
				case ActSetBoolSlot:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteBool(dest, frame.Flags[ReadSlot(script, ref ip)]);
					break;
				}
				case ActNotSlot:
				{
					var dest = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
					bag.WriteBool(dest, !frame.Flags[ReadSlot(script, ref ip)]);
					break;
				}
				case ActVm:
				{
					var length = TakeU16(script, ref ip, end);
					if (ip + length > end)
						throw new InvalidOperationException("Rejected record program.");
					RunVm(script, ip, length, strings, bag, platform, text, flag, rowId, frame, depth);
					ip += length;
					break;
				}
				default:
					throw new InvalidOperationException("Rejected record program.");
			}
		}
	}

	internal sealed class HandlerFrame
	{
		public readonly int[] Ints = new int[8];
		public readonly double[] Doubles = new double[8];
		public readonly string?[] Strings = new string?[8];
		public readonly bool[] Flags = new bool[8];
		public int Steps;

		public void Tick()
		{
			if (++Steps > ActionStepBudget)
				throw new ActionStoppedException("action stopped after " + ActionStepBudget.ToString(CultureInfo.InvariantCulture) + " steps");
		}
	}

	static int ReadIntExpr(byte[] script, ref int ip, string[] strings, Bag bag, HandlerFrame frame)
	{
		if (ip >= script.Length)
			throw new InvalidOperationException("Rejected record program.");
		var tag = script[ip++];
		switch (tag)
		{
			case IntExprLit:
				return ReadI32(script, ref ip);
			case IntExprProp:
				return bag.ReadInt(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
			case IntExprLocal:
				return frame.Ints[ReadSlot(script, ref ip)];
			case IntExprAdd:
				return ReadIntExpr(script, ref ip, strings, bag, frame) + ReadIntExpr(script, ref ip, strings, bag, frame);
			case IntExprSub:
				return ReadIntExpr(script, ref ip, strings, bag, frame) - ReadIntExpr(script, ref ip, strings, bag, frame);
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static string ReadStrPart(byte[] script, ref int ip, string[] strings, Bag bag, HandlerFrame frame)
	{
		if (ip >= script.Length)
			throw new InvalidOperationException("Rejected record program.");
		var kind = script[ip++];
		switch (kind)
		{
			case StrLit:
				return strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
			case StrProp:
				return bag.ReadString(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
			case StrInt:
				return bag.ReadInt(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]).ToString(CultureInfo.InvariantCulture);
			case StrLocal:
				return frame.Strings[TakeSlot(script, ref ip)] ?? "";
			case StrIntLocal:
				return frame.Ints[TakeSlot(script, ref ip)].ToString(CultureInfo.InvariantCulture);
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static bool ReadCond(byte[] script, ref int ip, string[] strings, Bag bag, HandlerFrame frame)
	{
		if (ip >= script.Length)
			throw new InvalidOperationException("Rejected record program.");
		var tag = script[ip++];
		switch (tag)
		{
			case CondBool:
				return bag.ReadBool(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
			case CondNotBool:
				return !bag.ReadBool(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]);
			case CondEmpty:
				return bag.ReadString(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]).Length == 0;
			case CondPresent:
				return bag.ReadString(strings[RequireString(TakeU16(script, ref ip, script.Length), strings)]).Length != 0;
			case CondStrEq:
			{
				var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				var literal = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				return bag.ReadString(name) == literal;
			}
			case CondStrNe:
			{
				var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				var literal = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				return bag.ReadString(name) != literal;
			}
			case CondIntEq:
			{
				var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				return bag.ReadInt(name) == ReadI32(script, ref ip);
			}
			case CondIntNe:
			{
				var name = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
				return bag.ReadInt(name) != ReadI32(script, ref ip);
			}
			case CondLocal:
				return frame.Flags[ReadSlot(script, ref ip)];
			case CondNotLocal:
				return !frame.Flags[ReadSlot(script, ref ip)];
			case CondLocalIntEq:
			{
				var slot = ReadSlot(script, ref ip);
				return frame.Ints[slot] == ReadI32(script, ref ip);
			}
			case CondLit:
			{
				if (ip >= script.Length)
					throw new InvalidOperationException("Rejected record program.");
				var bit = script[ip++];
				if (bit > 1)
					throw new InvalidOperationException("Rejected record program.");
				return bit == 1;
			}
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static int ReadI32(byte[] script, ref int ip)
	{
		if (ip + 4 > script.Length)
			throw new InvalidOperationException("Rejected record program.");
		var value = script[ip] | (script[ip + 1] << 8) | (script[ip + 2] << 16) | (script[ip + 3] << 24);
		ip += 4;
		return value;
	}

	static int ReadSlot(byte[] script, ref int ip)
	{
		if (ip >= script.Length)
			throw new InvalidOperationException("Rejected record program.");
		var slot = script[ip++];
		if (slot > 7)
			throw new InvalidOperationException("Rejected record program.");
		return slot;
	}

	static int TakeSlot(byte[] script, ref int ip)
	{
		var slot = TakeU16(script, ref ip, script.Length);
		if (slot > 7)
			throw new InvalidOperationException("Rejected record program.");
		return slot;
	}

	static bool CommitDraft(byte[] script, string[] strings, Bag bag, ref int ip)
	{
		var list = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var idField = TakeU16(script, ref ip, script.Length);
		var nameField = TakeU16(script, ref ip, script.Length);
		var packedField = TakeU16(script, ref ip, script.Length);
		var draftName = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var draftPacked = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var editingId = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var nextName = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var editing = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var prefix = strings[RequireString(TakeU16(script, ref ip, script.Length), strings)];
		var trimmed = bag.ReadString(draftName).Trim();
		if (trimmed.Length == 0)
			return false;
		var id = bag.ReadString(editingId);
		if (id.Length == 0)
		{
			var next = bag.ReadInt(nextName);
			var rows = bag.Rows(list);
			var width = rows.Count > 0 ? rows[0].Length : Math.Max(idField, Math.Max(nameField, packedField)) + 1;
			var cells = new string[width];
			Array.Fill(cells, "");
			cells[idField] = prefix + next.ToString(CultureInfo.InvariantCulture);
			cells[nameField] = trimmed;
			cells[packedField] = bag.ReadBool(draftPacked) ? "true" : "false";
			bag.AddRow(list, cells);
			bag.AddInt(nextName, 1);
		}
		else
		{
			bag.WriteCell(list, id, idField, nameField, trimmed);
			bag.WriteCell(list, id, idField, packedField, bag.ReadBool(draftPacked) ? "true" : "false");
		}

		bag.WriteBool(editing, false);
		return true;
	}

	static Column TakeColumn(Slot[] stack, ref int sp, int count, string? key)
	{
		if (count > MaxRows)
			throw new InvalidOperationException("Rejected record program.");
		var children = new Element[count];
		for (var index = count - 1; index >= 0; index--)
			children[index] = Pop(stack, ref sp, SlotKind.Elem).Node ?? throw new InvalidOperationException("Rejected record program.");
		return new Column(children) { Key = key };
	}

	static int TakeU16(byte[] code, ref int ip, int end)
	{
		if (ip + 2 > end)
			throw new InvalidOperationException("Rejected record program.");
		var value = code[ip] | (code[ip + 1] << 8);
		ip += 2;
		return value;
	}

	static int RequireString(int index, string[] strings)
	{
		if ((uint)index >= (uint)strings.Length && index != NoKey)
			throw new InvalidOperationException("Rejected record program.");
		if (index == NoKey)
			throw new InvalidOperationException("Rejected record program.");
		return index;
	}

	static string? Key(string[] strings, int index)
	{
		if (index == NoKey)
			return null;
		if ((uint)index >= (uint)strings.Length)
			throw new InvalidOperationException("Rejected record program.");
		return strings[index];
	}

	static void Push(Slot[] stack, ref int sp, Slot slot)
	{
		if (sp >= stack.Length)
			throw new InvalidOperationException("Rejected record program.");
		stack[sp++] = slot;
	}

	static Slot Pop(Slot[] stack, ref int sp, SlotKind kind)
	{
		if (sp == 0 || stack[sp - 1].Kind != kind)
			throw new InvalidOperationException("Rejected record program.");
		return stack[--sp];
	}

	enum SlotKind : byte
	{
		Str = 1,
		Bool = 2,
		Elem = 3,
		Mark = 4,
	}

	readonly record struct Slot(SlotKind Kind, string? Text, bool Flag, Element? Node)
	{
		public static Slot Str(string text) => new(SlotKind.Str, text, false, null);

		public static Slot Bool(bool flag) => new(SlotKind.Bool, null, flag, null);

		public static Slot Elem(Element node) => new(SlotKind.Elem, null, false, node);

		public static Slot Mark() => new(SlotKind.Mark, null, false, null);
	}

	public sealed class WindowList : IReadOnlyList<Element>
	{
		readonly byte[] _code;
		readonly string[] _strings;
		readonly Bag _bag;
		readonly IPlatform _platform;
		readonly IReadOnlyList<string[]> _rows;
		readonly int _start;
		readonly int _end;
		readonly Element?[] _cache;

		internal WindowList(byte[] code, string[] strings, Bag bag, IPlatform platform, IReadOnlyList<string[]> rows, int start, int end)
		{
			_code = code;
			_strings = strings;
			_bag = bag;
			_platform = platform;
			_rows = rows;
			_start = start;
			_end = end;
			_cache = new Element[rows.Count];
		}

		public int Count => _rows.Count;

		public int Built { get; private set; }

		public Element this[int index]
		{
			get
			{
				if ((uint)index >= (uint)_cache.Length)
					throw new ArgumentOutOfRangeException(nameof(index));
				var cached = _cache[index];
				if (cached is not null)
					return cached;
				var row = _rows[index];
				var stack = new Slot[256];
				var sp = 0;
				Element? returned = null;
				var cursor = _start;
				while (cursor < _end)
					cursor = Step(_code, _strings, _bag, _platform, stack, ref sp, ref row, cursor, _end, ref returned);
				if (returned is not null || sp != 1 || stack[0].Kind != SlotKind.Elem || stack[0].Node is not Element built)
					throw new InvalidOperationException("Rejected record program.");
				Built++;
				_cache[index] = built;
				return built;
			}
		}

		public IEnumerator<Element> GetEnumerator()
		{
			for (var index = 0; index < Count; index++)
				yield return this[index];
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}

	public sealed class BagImage
	{
		public IReadOnlyList<(string Name, string Value)> Strings { get; init; } = [];

		public IReadOnlyList<(string Name, bool Value)> Flags { get; init; } = [];

		public IReadOnlyList<(string Name, int Value)> Numbers { get; init; } = [];

		public IReadOnlyList<(string Name, string[][] Rows)> Lists { get; init; } = [];
	}

	public sealed class Bag
	{
		readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
		readonly Dictionary<string, bool> _flags = new(StringComparer.Ordinal);
		readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
		readonly Dictionary<string, List<string[]>> _lists = new(StringComparer.Ordinal);
		readonly object _gate = new();

		public void SeedString(string name, string value)
		{
			lock (_gate)
			{
				if (!_strings.ContainsKey(name))
					_strings[name] = value;
			}
		}

		public void SeedBool(string name, bool value)
		{
			lock (_gate)
			{
				if (!_flags.ContainsKey(name))
					_flags[name] = value;
			}
		}

		public void SeedInt(string name, int value)
		{
			lock (_gate)
			{
				if (!_numbers.ContainsKey(name))
					_numbers[name] = value;
			}
		}

		public void SeedList(string name, List<string[]> rows)
		{
			lock (_gate)
			{
				if (!_lists.ContainsKey(name))
					_lists[name] = rows;
			}
		}

		public string ReadString(string name)
		{
			lock (_gate)
				return _strings.TryGetValue(name, out var value) ? value : "";
		}

		public bool ReadBool(string name)
		{
			lock (_gate)
				return _flags.TryGetValue(name, out var value) && value;
		}

		public int ReadInt(string name)
		{
			lock (_gate)
				return _numbers.TryGetValue(name, out var value) ? value : 0;
		}

		public bool TryReadString(string name, out string value)
		{
			lock (_gate)
			{
				if (_strings.TryGetValue(name, out var found))
				{
					value = found;
					return true;
				}
			}

			value = "";
			return false;
		}

		public bool TryReadBool(string name, out bool value)
		{
			lock (_gate)
				return _flags.TryGetValue(name, out value);
		}

		public bool TryReadInt(string name, out int value)
		{
			lock (_gate)
				return _numbers.TryGetValue(name, out value);
		}

		public void WriteString(string name, string value)
		{
			lock (_gate)
				_strings[name] = value;
		}

		public void WriteBool(string name, bool value)
		{
			lock (_gate)
				_flags[name] = value;
		}

		public void WriteInt(string name, int value)
		{
			lock (_gate)
				_numbers[name] = value;
		}

		public void AddInt(string name, int delta)
		{
			lock (_gate)
			{
				_numbers.TryGetValue(name, out var value);
				_numbers[name] = value + delta;
			}
		}

		public IReadOnlyList<string[]> Rows(string name)
		{
			lock (_gate)
				return _lists.TryGetValue(name, out var rows) ? rows : Array.Empty<string[]>();
		}

		public int CountRows(string name)
		{
			lock (_gate)
				return _lists.TryGetValue(name, out var rows) ? rows.Count : 0;
		}

		public int CountFlag(string name, int field)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(name, out var rows))
					return 0;
				var count = 0;
				foreach (var row in rows)
				{
					if ((uint)field < (uint)row.Length && row[field] == "true")
						count++;
				}

				return count;
			}
		}

		public string Cell(string list, string id, int idField, int field)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(list, out var rows))
					return "";
				foreach (var row in rows)
				{
					if ((uint)idField < (uint)row.Length && row[idField] == id && (uint)field < (uint)row.Length)
						return row[field];
				}
			}

			return "";
		}

		public void WriteCell(string list, string id, int idField, int field, string value)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(list, out var rows))
					throw new InvalidOperationException("Rejected record program.");
				foreach (var row in rows)
				{
					if ((uint)idField < (uint)row.Length && row[idField] == id && (uint)field < (uint)row.Length)
					{
						row[field] = value;
						return;
					}
				}
			}

			throw new InvalidOperationException("Rejected record program.");
		}

		public void RemoveRow(string list, string id, int idField)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(list, out var rows))
					return;
				for (var index = rows.Count - 1; index >= 0; index--)
				{
					var row = rows[index];
					if ((uint)idField < (uint)row.Length && row[idField] == id)
						rows.RemoveAt(index);
				}
			}
		}

		public void ReplaceRows(string name, List<string[]> rows)
		{
			ArgumentNullException.ThrowIfNull(rows);
			lock (_gate)
				_lists[name] = rows;
		}

		public void AddRow(string name, string[] cells)
		{
			ArgumentNullException.ThrowIfNull(cells);
			lock (_gate)
			{
				if (!_lists.TryGetValue(name, out var rows))
				{
					rows = new List<string[]>();
					_lists[name] = rows;
				}

				rows.Add(cells);
			}
		}

		public void ClearList(string name)
		{
			lock (_gate)
			{
				if (_lists.TryGetValue(name, out var rows))
					rows.Clear();
			}
		}

		public void RemoveAt(string name, int index)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(name, out var rows))
					return;
				if ((uint)index >= (uint)rows.Count)
					return;
				rows.RemoveAt(index);
			}
		}

		public string CellAt(string name, int row, int field)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(name, out var rows) || (uint)row >= (uint)rows.Count)
					return "";
				var cells = rows[row];
				return (uint)field < (uint)cells.Length ? cells[field] : "";
			}
		}

		public bool RowInRange(string name, int index)
		{
			lock (_gate)
				return _lists.TryGetValue(name, out var rows) && (uint)index < (uint)rows.Count;
		}

		public string RowLines(string list, int textField, int flagField)
		{
			lock (_gate)
			{
				if (!_lists.TryGetValue(list, out var rows) || rows.Count == 0)
					return "";
				var lines = new string[rows.Count];
				for (var index = 0; index < rows.Count; index++)
				{
					var row = rows[index];
					var text = (uint)textField < (uint)row.Length ? row[textField] : "";
					var packed = (uint)flagField < (uint)row.Length && row[flagField] == "true";
					lines[index] = packed ? text + " [packed]" : text;
				}

				return string.Join('\n', lines);
			}
		}

		public string Format()
		{
			lock (_gate)
			{
				var builder = new StringBuilder();
				foreach (var pair in _strings.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					builder.Append(pair.Key);
					builder.Append('=');
					builder.Append(Convert.ToHexString(Encoding.UTF8.GetBytes(pair.Value)).ToLowerInvariant());
					builder.Append('\n');
				}

				foreach (var pair in _flags.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					builder.Append(pair.Key);
					builder.Append('=');
					builder.Append(pair.Value ? "true" : "false");
					builder.Append('\n');
				}

				foreach (var pair in _numbers.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					builder.Append(pair.Key);
					builder.Append('=');
					builder.Append(pair.Value.ToString(CultureInfo.InvariantCulture));
					builder.Append('\n');
				}

				return builder.ToString();
			}
		}

		public BagImage Capture()
		{
			lock (_gate)
			{
				var lists = new List<(string Name, string[][] Rows)>(_lists.Count);
				foreach (var pair in _lists)
				{
					var rows = new string[pair.Value.Count][];
					for (var index = 0; index < pair.Value.Count; index++)
						rows[index] = (string[])pair.Value[index].Clone();
					lists.Add((pair.Key, rows));
				}

				return new BagImage
				{
					Strings = _strings.Select(pair => (pair.Key, pair.Value)).ToArray(),
					Flags = _flags.Select(pair => (pair.Key, pair.Value)).ToArray(),
					Numbers = _numbers.Select(pair => (pair.Key, pair.Value)).ToArray(),
					Lists = lists,
				};
			}
		}

		public void Replace(BagImage image)
		{
			ArgumentNullException.ThrowIfNull(image);
			lock (_gate)
			{
				_strings.Clear();
				foreach (var (name, value) in image.Strings)
					_strings[name] = value;
				_flags.Clear();
				foreach (var (name, value) in image.Flags)
					_flags[name] = value;
				_numbers.Clear();
				foreach (var (name, value) in image.Numbers)
					_numbers[name] = value;
				_lists.Clear();
				foreach (var (name, rows) in image.Lists)
				{
					var copy = new List<string[]>(rows.Length);
					foreach (var row in rows)
						copy.Add((string[])row.Clone());
					_lists[name] = copy;
				}
			}
		}
	}
}
