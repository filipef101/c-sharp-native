using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NativePoc;

/// <summary>
/// A screen as data. The bytes are a stack program the process already contains.
/// Applying one does not load an assembly.
/// </summary>
public static partial class ScreenProgram
{
	public const byte Version = 1;

	public const byte OpPushString = 1;
	public const byte OpPushCount = 2;
	public const byte OpPushName = 3;
	public const byte OpPushPlainRoot = 4;
	public const byte OpPushGreetingFirst = 5;
	public const byte OpConcat = 6;
	public const byte OpIsEmpty = 7;
	public const byte OpJumpIfFalse = 8;
	public const byte OpJump = 9;
	public const byte OpStore = 10;
	public const byte OpLoad = 11;
	public const byte OpMakeText = 12;
	public const byte OpMakeField = 13;
	public const byte OpMakeButton = 14;
	public const byte OpMakeColumn = 15;
	public const byte OpReturn = 16;
	public const byte OpPushEditor = 17;
	public const byte OpPushEditingId = 18;
	public const byte OpPushDraftName = 19;
	public const byte OpPushDraftPacked = 20;
	public const byte OpPushItemCount = 21;
	public const byte OpPushPackedCount = 22;
	public const byte OpPushItemName = 23;
	public const byte OpPushItemPacked = 24;
	public const byte OpMark = 25;
	public const byte OpForEachItem = 26;
	public const byte OpMakeKeyedColumn = 27;
	public const byte OpMakeItemColumn = 28;
	public const byte OpMakeDynamicColumn = 29;
	public const byte OpMakeSwitch = 30;
	public const byte OpMakeActionButton = 31;
	public const byte OpMakeDraftField = 32;

	public const byte PackAdd = 1;
	public const byte PackEdit = 2;
	public const byte PackSave = 3;
	public const byte PackBack = 4;
	public const byte PackCopy = 5;
	public const byte PackItemPacked = 6;
	public const byte PackDraftPacked = 7;

	public const int MaxStrings = 64;
	public const int MaxStringBytes = 4096;
	public const int MaxCode = 8192;
	public const int MaxStack = 64;
	public const int MaxLocals = 8;
	public const int MaxChildren = 64;

	public static bool IsProgram(ReadOnlySpan<byte> payload) =>
		payload.Length >= 4
		&& payload[0] == (byte)'N'
		&& payload[1] == (byte)'P'
		&& payload[2] == (byte)'C'
		&& payload[3] == (byte)'1';

	public static bool IsPacking(ReadOnlySpan<byte> payload) =>
		IsProgram(payload) && payload.Length > 5 && payload[5] == 1;

	public static string SourceHash(string source)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
		return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
	}

	public readonly struct Bound
	{
		public Bound(Func<AppState, IPlatform, Element> render, string sourceHash)
		{
			Render = render;
			SourceHash = sourceHash;
		}

		public Func<AppState, IPlatform, Element> Render { get; }

		public string SourceHash { get; }
	}

	public readonly struct PackingBound
	{
		public PackingBound(Func<IPackingProgram, Element> render, string sourceHash)
		{
			Render = render;
			SourceHash = sourceHash;
		}

		public Func<IPackingProgram, Element> Render { get; }

		public string SourceHash { get; }
	}

	public static Bound Bind(ReadOnlySpan<byte> payload)
	{
		var decoded = Decode(payload);
		if (decoded.Kind != 0)
			throw new InvalidOperationException("Rejected screen program.");
		Verify(decoded.Code, decoded.Strings.Length);
		var code = decoded.Code;
		var strings = decoded.Strings;
		var sourceHash = decoded.SourceHash;
		return new Bound(
			(state, platform) => Eval(code, strings, state, platform, null),
			sourceHash);
	}

	public static PackingBound BindPacking(ReadOnlySpan<byte> payload)
	{
		var decoded = Decode(payload);
		if (decoded.Kind != 1)
			throw new InvalidOperationException("Rejected screen program.");
		Verify(decoded.Code, decoded.Strings.Length);
		var code = decoded.Code;
		var strings = decoded.Strings;
		var sourceHash = decoded.SourceHash;
		return new PackingBound(
			packing => Eval(code, strings, null, null, packing),
			sourceHash);
	}

	public sealed class Writer
	{
		readonly List<string> _strings = new();
		readonly List<byte> _code = new();

		public int Intern(string text)
		{
			ArgumentNullException.ThrowIfNull(text);
			var bytes = Encoding.UTF8.GetByteCount(text);
			if (bytes > MaxStringBytes)
				throw new InvalidOperationException("A screen string is too long.");
			for (var index = 0; index < _strings.Count; index++)
			{
				if (string.Equals(_strings[index], text, StringComparison.Ordinal))
					return index;
			}

			if (_strings.Count == MaxStrings)
				throw new InvalidOperationException("A screen program has too many strings.");
			_strings.Add(text);
			return _strings.Count - 1;
		}

		public int Here => _code.Count;

		public void PushString(int index) => EmitIndex(OpPushString, index);

		public void PushCount() => _code.Add(OpPushCount);

		public void PushName() => _code.Add(OpPushName);

		public void PushPlainRoot() => _code.Add(OpPushPlainRoot);

		public void PushGreetingFirst() => _code.Add(OpPushGreetingFirst);

		public void Concat() => _code.Add(OpConcat);

		public void IsEmpty() => _code.Add(OpIsEmpty);

		public int JumpIfFalse() => EmitJump(OpJumpIfFalse);

		public int Jump() => EmitJump(OpJump);

		public void Patch(int operandAt, int target)
		{
			var relative = target - (operandAt + 2);
			if (relative <= 0 || relative > short.MaxValue)
				throw new InvalidOperationException("A screen jump has to move forward.");
			_code[operandAt] = (byte)relative;
			_code[operandAt + 1] = (byte)(relative >> 8);
		}

		public void Store(byte slot) => EmitSlot(OpStore, slot);

		public void Load(byte slot) => EmitSlot(OpLoad, slot);

		public void MakeText(int key) => EmitIndex(OpMakeText, key);

		public void MakeField(int key) => EmitIndex(OpMakeField, key);

		public void MakeButton(int key, int noncePrefix)
		{
			EmitIndex(OpMakeButton, key);
			EmitU16(noncePrefix);
		}

		public void MakeColumn(byte count)
		{
			if (count > MaxChildren)
				throw new InvalidOperationException("A screen column has too many children.");
			_code.Add(OpMakeColumn);
			_code.Add(count);
		}

		public void PushEditor() => _code.Add(OpPushEditor);

		public void PushEditingId() => _code.Add(OpPushEditingId);

		public void PushDraftName() => _code.Add(OpPushDraftName);

		public void PushDraftPacked() => _code.Add(OpPushDraftPacked);

		public void PushItemCount() => _code.Add(OpPushItemCount);

		public void PushPackedCount() => _code.Add(OpPushPackedCount);

		public void PushItemName() => _code.Add(OpPushItemName);

		public void PushItemPacked() => _code.Add(OpPushItemPacked);

		public void Mark() => _code.Add(OpMark);

		public int BeginItems()
		{
			_code.Add(OpForEachItem);
			var at = _code.Count;
			_code.Add(0);
			_code.Add(0);
			return at;
		}

		public void EndItems(int operandAt)
		{
			var length = _code.Count - (operandAt + 2);
			if (length <= 0 || length > ushort.MaxValue)
				throw new InvalidOperationException("An item row has no body.");
			_code[operandAt] = (byte)length;
			_code[operandAt + 1] = (byte)(length >> 8);
		}

		public void MakeKeyedColumn(byte count, int key)
		{
			if (count > MaxChildren)
				throw new InvalidOperationException("A screen column has too many children.");
			if ((uint)key >= (uint)_strings.Count)
				throw new InvalidOperationException("A screen string index is out of range.");
			_code.Add(OpMakeKeyedColumn);
			_code.Add(count);
			EmitU16(key);
		}

		public void MakeItemColumn(byte count)
		{
			if (count > MaxChildren)
				throw new InvalidOperationException("A screen column has too many children.");
			_code.Add(OpMakeItemColumn);
			_code.Add(count);
		}

		public void MakeDynamicColumn(int key) => EmitIndex(OpMakeDynamicColumn, key);

		public void MakeSwitch(int key, byte action)
		{
			EmitIndex(OpMakeSwitch, key);
			_code.Add(action);
		}

		public void MakeActionButton(int key, byte action)
		{
			EmitIndex(OpMakeActionButton, key);
			_code.Add(action);
		}

		public void MakeDraftField(int key) => EmitIndex(OpMakeDraftField, key);

		public void Return() => _code.Add(OpReturn);

		public byte[] Finish(string? source) => Finish(source, 0);

		public byte[] Finish(string? source, byte kind)
		{
			if (kind > 1)
				throw new InvalidOperationException("A screen program kind is out of range.");
			if (_code.Count == 0 || _code.Count > MaxCode)
				throw new InvalidOperationException("A screen program has no body.");
			var hash = new byte[HashSize];
			if (source is not null)
				SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, HashSize).CopyTo(hash);
			var size = 8 + HashSize;
			foreach (var text in _strings)
				size += 2 + Encoding.UTF8.GetByteCount(text);
			size += 2 + _code.Count;
			var payload = new byte[size];
			payload[0] = (byte)'N';
			payload[1] = (byte)'P';
			payload[2] = (byte)'C';
			payload[3] = (byte)'1';
			payload[4] = Version;
			payload[5] = kind;
			var cursor = 6;
			cursor = WriteU16(payload, cursor, _strings.Count);
			foreach (var text in _strings)
			{
				var encoded = Encoding.UTF8.GetBytes(text);
				cursor = WriteU16(payload, cursor, encoded.Length);
				encoded.CopyTo(payload, cursor);
				cursor += encoded.Length;
			}

			cursor = WriteU16(payload, cursor, _code.Count);
			foreach (var item in _code)
				payload[cursor++] = item;
			hash.CopyTo(payload, cursor);
			return payload;
		}

		int EmitJump(byte op)
		{
			_code.Add(op);
			var at = _code.Count;
			_code.Add(0);
			_code.Add(0);
			return at;
		}

		void EmitSlot(byte op, byte slot)
		{
			if (slot >= MaxLocals)
				throw new InvalidOperationException("A screen local is out of range.");
			_code.Add(op);
			_code.Add(slot);
		}

		void EmitIndex(byte op, int index)
		{
			if ((uint)index >= (uint)_strings.Count)
				throw new InvalidOperationException("A screen string index is out of range.");
			_code.Add(op);
			EmitU16(index);
		}

		void EmitU16(int value)
		{
			if ((uint)value > ushort.MaxValue)
				throw new InvalidOperationException("A screen operand is out of range.");
			_code.Add((byte)value);
			_code.Add((byte)(value >> 8));
		}

		static int WriteU16(byte[] payload, int cursor, int value)
		{
			payload[cursor] = (byte)value;
			payload[cursor + 1] = (byte)(value >> 8);
			return cursor + 2;
		}
	}

	const int HashSize = 8;

	readonly record struct Decoded(string[] Strings, byte[] Code, string SourceHash, byte Kind);

	static Decoded Decode(ReadOnlySpan<byte> payload)
	{
		if (!IsProgram(payload))
			throw new InvalidOperationException("Rejected screen program.");
		if (payload.Length < 6 + HashSize || payload[4] != Version || payload[5] > 1)
			throw new InvalidOperationException("Rejected screen program.");
		var cursor = 6;
		var stringCount = ReadU16(payload, ref cursor);
		if (stringCount > MaxStrings)
			throw new InvalidOperationException("Rejected screen program.");
		var strings = new string[stringCount];
		for (var index = 0; index < stringCount; index++)
		{
			var length = ReadU16(payload, ref cursor);
			if (length > MaxStringBytes || cursor + length > payload.Length - HashSize)
				throw new InvalidOperationException("Rejected screen program.");
			strings[index] = Encoding.UTF8.GetString(payload.Slice(cursor, length));
			cursor += length;
		}

		var codeLength = ReadU16(payload, ref cursor);
		if (codeLength == 0 || codeLength > MaxCode || cursor + codeLength + HashSize != payload.Length)
			throw new InvalidOperationException("Rejected screen program.");
		var code = payload.Slice(cursor, codeLength).ToArray();
		var hash = payload.Slice(cursor + codeLength, HashSize);
		var sourceHash = hash.IndexOf((byte)0) >= 0 && hash.Trim((byte)0).Length == 0
			? ""
			: Convert.ToHexString(hash).ToLowerInvariant();
		return new Decoded(strings, code, sourceHash, payload[5]);
	}

	static int ReadU16(ReadOnlySpan<byte> payload, ref int cursor)
	{
		if (cursor + 2 > payload.Length)
			throw new InvalidOperationException("Rejected screen program.");
		var value = payload[cursor] | (payload[cursor + 1] << 8);
		cursor += 2;
		return value;
	}

	enum Kind : byte
	{
		None = 0,
		Str = 1,
		Bool = 2,
		Elem = 3,
		Mark = 4,
		Repeat = 5,
	}

	static void Verify(byte[] code, int stringCount) =>
		VerifyRegion(code, stringCount, 0, code.Length, expectReturn: true, new List<(int Start, int End)>());

	static void VerifyRegion(byte[] code, int stringCount, int start, int end, bool expectReturn, List<(int Start, int End)> bodies)
	{
		var seen = new Dictionary<int, string>();
		var work = new Queue<Frame>();
		work.Enqueue(new Frame(start, new Kind[MaxStack], 0, new Kind[MaxLocals]));
		var returned = false;
		while (work.Count > 0)
		{
			var frame = work.Dequeue();
			var signature = Signature(frame);
			if (seen.TryGetValue(frame.Ip, out var previous))
			{
				if (!string.Equals(previous, signature, StringComparison.Ordinal))
					throw new InvalidOperationException("Rejected screen program.");
				continue;
			}

			if (!expectReturn && frame.Ip == end)
			{
				if (frame.Sp != 1 || frame.Stack[0] != Kind.Elem)
					throw new InvalidOperationException("Rejected screen program.");
				returned = true;
				continue;
			}

			if ((uint)frame.Ip >= (uint)end)
				throw new InvalidOperationException("Rejected screen program.");
			if (expectReturn)
			{
				foreach (var body in bodies)
				{
					if (frame.Ip >= body.Start && frame.Ip < body.End)
						throw new InvalidOperationException("Rejected screen program.");
				}
			}

			seen[frame.Ip] = signature;
			var ip = frame.Ip;
			var op = code[ip++];
			var stack = frame.Stack;
			var sp = frame.Sp;
			var locals = frame.Locals;
			switch (op)
			{
				case OpPushString:
					Require(ref ip, code, 2);
					if (U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					PushKind(stack, ref sp, Kind.Str);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpPushCount:
				case OpPushName:
					PushKind(stack, ref sp, Kind.Str);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpPushPlainRoot:
				case OpPushGreetingFirst:
					PushKind(stack, ref sp, Kind.Bool);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpConcat:
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Str);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpIsEmpty:
					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Bool);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpJumpIfFalse:
				{
					Require(ref ip, code, 2);
					PopKind(stack, ref sp, Kind.Bool);
					var target = ip + I16(code, ip - 2);
					if (target <= frame.Ip || target > end || (!expectReturn && target < start))
						throw new InvalidOperationException("Rejected screen program.");
					work.Enqueue(Copy(ip, stack, sp, locals));
					work.Enqueue(Copy(target, stack, sp, locals));
					break;
				}
				case OpJump:
				{
					Require(ref ip, code, 2);
					var target = ip + I16(code, ip - 2);
					if (target <= frame.Ip || target > end || (!expectReturn && target < start))
						throw new InvalidOperationException("Rejected screen program.");
					work.Enqueue(Copy(target, stack, sp, locals));
					break;
				}
				case OpStore:
				{
					Require(ref ip, code, 1);
					var slot = code[ip - 1];
					if (slot >= MaxLocals || sp == 0)
						throw new InvalidOperationException("Rejected screen program.");
					locals = CopyKinds(locals);
					locals[slot] = stack[--sp];
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpLoad:
				{
					Require(ref ip, code, 1);
					var slot = code[ip - 1];
					if (slot >= MaxLocals || locals[slot] == Kind.None)
						throw new InvalidOperationException("Rejected screen program.");
					PushKind(stack, ref sp, locals[slot]);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpMakeText:
				case OpMakeField:
					Require(ref ip, code, 2);
					if (U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					if (op == OpMakeField)
					{
						PopKind(stack, ref sp, Kind.Str);
						PopKind(stack, ref sp, Kind.Str);
					}

					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpMakeButton:
					Require(ref ip, code, 4);
					if (U16(code, ip - 4) >= stringCount || U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpMakeColumn:
				{
					Require(ref ip, code, 1);
					var count = code[ip - 1];
					if (count > MaxChildren)
						throw new InvalidOperationException("Rejected screen program.");
					for (var child = 0; child < count; child++)
						PopKind(stack, ref sp, Kind.Elem);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpReturn:
					if (!expectReturn || sp != 1 || stack[0] != Kind.Elem)
						throw new InvalidOperationException("Rejected screen program.");
					returned = true;
					break;
				case OpPushEditor:
				case OpPushDraftPacked:
				case OpPushItemPacked:
					PushKind(stack, ref sp, Kind.Bool);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpPushEditingId:
				case OpPushDraftName:
				case OpPushItemCount:
				case OpPushPackedCount:
				case OpPushItemName:
					PushKind(stack, ref sp, Kind.Str);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpMark:
					PushKind(stack, ref sp, Kind.Mark);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpForEachItem:
				{
					if (!expectReturn)
						throw new InvalidOperationException("Rejected screen program.");
					Require(ref ip, code, 2);
					var length = U16(code, ip - 2);
					var bodyEnd = ip + length;
					if (length == 0 || bodyEnd > end)
						throw new InvalidOperationException("Rejected screen program.");
					bodies.Add((ip, bodyEnd));
					VerifyRegion(code, stringCount, ip, bodyEnd, expectReturn: false, bodies);
					ip = bodyEnd;
					PushKind(stack, ref sp, Kind.Repeat);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpMakeKeyedColumn:
				{
					Require(ref ip, code, 3);
					var count = code[ip - 3];
					if (count > MaxChildren || U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					for (var child = 0; child < count; child++)
						PopKind(stack, ref sp, Kind.Elem);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpMakeItemColumn:
				{
					Require(ref ip, code, 1);
					var count = code[ip - 1];
					if (count > MaxChildren)
						throw new InvalidOperationException("Rejected screen program.");
					for (var child = 0; child < count; child++)
						PopKind(stack, ref sp, Kind.Elem);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpMakeDynamicColumn:
				{
					Require(ref ip, code, 2);
					if (U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					while (sp > 0 && (stack[sp - 1] == Kind.Elem || stack[sp - 1] == Kind.Repeat))
						sp--;
					PopKind(stack, ref sp, Kind.Mark);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				}
				case OpMakeSwitch:
					Require(ref ip, code, 3);
					if (U16(code, ip - 3) >= stringCount || code[ip - 1] is not (PackItemPacked or PackDraftPacked))
						throw new InvalidOperationException("Rejected screen program.");
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Bool);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpMakeActionButton:
					Require(ref ip, code, 3);
					if (U16(code, ip - 3) >= stringCount || code[ip - 1] is not (PackAdd or PackEdit or PackSave or PackBack or PackCopy))
						throw new InvalidOperationException("Rejected screen program.");
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				case OpMakeDraftField:
					Require(ref ip, code, 2);
					if (U16(code, ip - 2) >= stringCount)
						throw new InvalidOperationException("Rejected screen program.");
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Str);
					PopKind(stack, ref sp, Kind.Str);
					PushKind(stack, ref sp, Kind.Elem);
					work.Enqueue(Copy(ip, stack, sp, locals));
					break;
				default:
					throw new InvalidOperationException("Rejected screen program.");
			}
		}

		if (!returned)
			throw new InvalidOperationException("Rejected screen program.");
	}

	readonly struct Frame
	{
		public Frame(int ip, Kind[] stack, int sp, Kind[] locals)
		{
			Ip = ip;
			Stack = stack;
			Sp = sp;
			Locals = locals;
		}

		public int Ip { get; }

		public Kind[] Stack { get; }

		public int Sp { get; }

		public Kind[] Locals { get; }
	}

	static Frame Copy(int ip, Kind[] stack, int sp, Kind[] locals) =>
		new(ip, CopyKinds(stack), sp, CopyKinds(locals));

	static Kind[] CopyKinds(Kind[] source)
	{
		var copy = new Kind[source.Length];
		source.AsSpan().CopyTo(copy);
		return copy;
	}

	static string Signature(Frame frame)
	{
		var text = new StringBuilder();
		for (var index = 0; index < frame.Sp; index++)
			text.Append((char)('0' + (int)frame.Stack[index]));
		text.Append('|');
		foreach (var local in frame.Locals)
			text.Append((char)('0' + (int)local));
		return text.ToString();
	}

	static void PushKind(Kind[] stack, ref int sp, Kind kind)
	{
		if (sp == MaxStack)
			throw new InvalidOperationException("Rejected screen program.");
		stack[sp++] = kind;
	}

	static void PopKind(Kind[] stack, ref int sp, Kind kind)
	{
		if (sp == 0 || stack[sp - 1] != kind)
			throw new InvalidOperationException("Rejected screen program.");
		sp--;
	}

	static void Require(ref int ip, byte[] code, int width)
	{
		if (ip + width > code.Length)
			throw new InvalidOperationException("Rejected screen program.");
		ip += width;
	}

	static int U16(byte[] code, int ip) => code[ip] | (code[ip + 1] << 8);

	static int I16(byte[] code, int ip) => (short)(code[ip] | (code[ip + 1] << 8));

	readonly struct Slot
	{
		public Slot(Kind kind, string? text, bool flag, Element? node)
		{
			Kind = kind;
			Text = text;
			Flag = flag;
			Node = node;
		}

		public Kind Kind { get; }

		public string? Text { get; }

		public bool Flag { get; }

		public Element? Node { get; }

		public static Slot Str(string text) => new(Kind.Str, text, false, null);

		public static Slot Bool(bool flag) => new(Kind.Bool, null, flag, null);

		public static Slot Elem(Element node) => new(Kind.Elem, null, false, node);

		public static Slot Mark() => new(Kind.Mark, null, false, null);
	}

	static Element Eval(byte[] code, string[] strings, AppState? state, IPlatform? platform, IPackingProgram? packing)
	{
		if (packing is null)
		{
			ArgumentNullException.ThrowIfNull(state);
			ArgumentNullException.ThrowIfNull(platform);
		}
		else
			ArgumentNullException.ThrowIfNull(packing);

		var stack = new Slot[MaxStack];
		var locals = new Slot[MaxLocals];
		var sp = 0;
		var ip = 0;
		var steps = 0;
		var itemIndex = -1;
		var foreachRemaining = 0;
		var foreachStart = 0;
		var foreachEnd = 0;
		var foreachBase = 0;
		var limit = code.Length * 64;
		while (ip < code.Length)
		{
			if (foreachRemaining > 0 && ip == foreachEnd)
			{
				if (sp != foreachBase + 1)
					throw new InvalidOperationException("Screen program did not return.");
				foreachRemaining--;
				itemIndex++;
				if (foreachRemaining == 0)
					continue;
				foreachBase = sp;
				ip = foreachStart;
				continue;
			}

			if (++steps > limit)
				throw new InvalidOperationException("Screen program did not return.");
			var op = code[ip++];
			switch (op)
			{
				case OpPushString:
					stack[sp++] = Slot.Str(strings[U16(code, ip)]);
					ip += 2;
					break;
				case OpPushCount:
					stack[sp++] = Slot.Str(ScreenState().Count.ToString());
					break;
				case OpPushName:
					stack[sp++] = Slot.Str(ScreenState().Name);
					break;
				case OpPushPlainRoot:
					stack[sp++] = Slot.Bool(ScreenState().PlainRoot);
					break;
				case OpPushGreetingFirst:
					stack[sp++] = Slot.Bool(ScreenState().GreetingFirst);
					break;
				case OpConcat:
				{
					var right = stack[--sp].Text ?? "";
					var left = stack[--sp].Text ?? "";
					stack[sp++] = Slot.Str(left + right);
					break;
				}
				case OpIsEmpty:
					stack[sp - 1] = Slot.Bool((stack[sp - 1].Text ?? "").Length == 0);
					break;
				case OpJumpIfFalse:
				{
					var flag = stack[--sp].Flag;
					var relative = I16(code, ip);
					ip += 2;
					if (!flag)
						ip += relative;
					break;
				}
				case OpJump:
					ip += 2 + I16(code, ip);
					break;
				case OpStore:
					locals[code[ip++]] = stack[--sp];
					break;
				case OpLoad:
					stack[sp++] = locals[code[ip++]];
					break;
				case OpMakeText:
				{
					var value = stack[--sp].Text ?? "";
					stack[sp++] = Slot.Elem(new Text(value) { Key = strings[U16(code, ip)] });
					ip += 2;
					break;
				}
				case OpMakeField:
				{
					var label = stack[--sp].Text ?? "";
					var placeholder = stack[--sp].Text ?? "";
					var value = stack[--sp].Text ?? "";
					var screen = ScreenState();
					var screenPlatform = ScreenPlatform();
					stack[sp++] = Slot.Elem(new TextField(
						value,
						next =>
						{
							screen.Name = next;
							screenPlatform.RequestRender();
						},
						placeholder,
						label)
					{ Key = strings[U16(code, ip)] });
					ip += 2;
					break;
				}
				case OpMakeButton:
				{
					var label = stack[--sp].Text ?? "";
					var title = stack[--sp].Text ?? "";
					var key = strings[U16(code, ip)];
					var prefix = strings[U16(code, ip + 2)];
					var counted = ScreenState();
					var countedPlatform = ScreenPlatform();
					stack[sp++] = Slot.Elem(new Button(
						title,
						() =>
						{
							counted.Count++;
							countedPlatform.CopyNonce(prefix + counted.Count);
							countedPlatform.RequestRender();
						},
						label)
					{ Key = key });
					ip += 4;
					break;
				}
				case OpMakeColumn:
				{
					var count = code[ip++];
					var children = new Element[count];
					for (var index = count - 1; index >= 0; index--)
						children[index] = stack[--sp].Node ?? throw new InvalidOperationException("Screen program did not return.");
					stack[sp++] = Slot.Elem(new Column(children));
					break;
				}
				case OpReturn:
					if (sp != 1 || stack[0].Node is not Element result)
						throw new InvalidOperationException("Screen program did not return.");
					return result;
				case OpPushEditor:
					stack[sp++] = Slot.Bool(Rows().Editor);
					break;
				case OpPushEditingId:
					stack[sp++] = Slot.Str(Rows().EditingId ?? "");
					break;
				case OpPushDraftName:
					stack[sp++] = Slot.Str(Rows().DraftName);
					break;
				case OpPushDraftPacked:
					stack[sp++] = Slot.Bool(Rows().DraftPacked);
					break;
				case OpPushItemCount:
					stack[sp++] = Slot.Str(Rows().ItemCount.ToString(CultureInfo.InvariantCulture));
					break;
				case OpPushPackedCount:
					stack[sp++] = Slot.Str(Rows().PackedCount.ToString(CultureInfo.InvariantCulture));
					break;
				case OpPushItemName:
					stack[sp++] = Slot.Str(Rows().ItemName(itemIndex));
					break;
				case OpPushItemPacked:
					stack[sp++] = Slot.Bool(Rows().ItemPacked(itemIndex));
					break;
				case OpMark:
					stack[sp++] = Slot.Mark();
					break;
				case OpForEachItem:
				{
					var bodyLength = U16(code, ip);
					ip += 2;
					var bodyStart = ip;
					var bodyEnd = ip + bodyLength;
					if (bodyEnd > code.Length)
						throw new InvalidOperationException("Screen program did not return.");
					var count = Rows().ItemCount;
					if (count > MaxChildren)
						throw new InvalidOperationException("Screen program did not return.");
					if (count == 0)
					{
						ip = bodyEnd;
						break;
					}

					foreachStart = bodyStart;
					foreachEnd = bodyEnd;
					foreachRemaining = count;
					foreachBase = sp;
					itemIndex = 0;
					ip = bodyStart;
					break;
				}
				case OpMakeKeyedColumn:
				{
					var count = code[ip++];
					var key = strings[U16(code, ip)];
					ip += 2;
					var keyed = TakeColumn(stack, ref sp, count, key);
					stack[sp++] = Slot.Elem(keyed);
					break;
				}
				case OpMakeItemColumn:
				{
					var count = code[ip++];
					var itemColumn = TakeColumn(stack, ref sp, count, Rows().ItemId(itemIndex));
					stack[sp++] = Slot.Elem(itemColumn);
					break;
				}
				case OpMakeDynamicColumn:
				{
					var key = strings[U16(code, ip)];
					ip += 2;
					var gathered = new List<Element>();
					while (sp > 0 && stack[sp - 1].Kind != Kind.Mark)
						gathered.Add(stack[--sp].Node ?? throw new InvalidOperationException("Screen program did not return."));
					if (sp == 0 || stack[--sp].Kind != Kind.Mark)
						throw new InvalidOperationException("Screen program did not return.");
					if (gathered.Count > MaxChildren)
						throw new InvalidOperationException("Screen program did not return.");
					gathered.Reverse();
					stack[sp++] = Slot.Elem(new Column(gathered) { Key = key });
					break;
				}
				case OpMakeSwitch:
				{
					var label = stack[--sp].Text ?? "";
					var value = stack[--sp].Flag;
					var key = strings[U16(code, ip)];
					var action = code[ip + 2];
					var index = itemIndex;
					var rows = Rows();
					stack[sp++] = Slot.Elem(new Switch(
						value,
						next =>
						{
							if (action == PackItemPacked)
								rows.SetItemPacked(index, next);
							else
								rows.SetDraftPacked(next);
						},
						label)
					{ Key = key });
					ip += 3;
					break;
				}
				case OpMakeActionButton:
				{
					var label = stack[--sp].Text ?? "";
					var title = stack[--sp].Text ?? "";
					var key = strings[U16(code, ip)];
					var action = code[ip + 2];
					var index = itemIndex;
					var rows = Rows();
					stack[sp++] = Slot.Elem(new Button(
						title,
						() => RunPack(rows, action, index),
						label)
					{ Key = key });
					ip += 3;
					break;
				}
				case OpMakeDraftField:
				{
					var label = stack[--sp].Text ?? "";
					var placeholder = stack[--sp].Text ?? "";
					var value = stack[--sp].Text ?? "";
					var rows = Rows();
					stack[sp++] = Slot.Elem(new TextField(
						value,
						next => rows.SetDraftName(next),
						placeholder,
						label)
					{ Key = strings[U16(code, ip)] });
					ip += 2;
					break;
				}
				default:
					throw new InvalidOperationException("Rejected screen program.");
			}
		}

		throw new InvalidOperationException("Screen program did not return.");

		AppState ScreenState() => state ?? throw new InvalidOperationException("Rejected screen program.");

		IPlatform ScreenPlatform() => platform ?? throw new InvalidOperationException("Rejected screen program.");

		IPackingProgram Rows() => packing ?? throw new InvalidOperationException("Rejected screen program.");
	}

	static Column TakeColumn(Slot[] stack, ref int sp, int count, string? key)
	{
		var children = new Element[count];
		for (var index = count - 1; index >= 0; index--)
			children[index] = stack[--sp].Node ?? throw new InvalidOperationException("Screen program did not return.");
		return new Column(children) { Key = key };
	}

	static void RunPack(IPackingProgram rows, byte action, int index)
	{
		switch (action)
		{
			case PackAdd:
				rows.BeginAdd();
				return;
			case PackEdit:
				rows.BeginEdit(index);
				return;
			case PackSave:
				rows.Save();
				return;
			case PackBack:
				rows.Back();
				return;
			case PackCopy:
				rows.Copy();
				return;
			default:
				throw new InvalidOperationException("Rejected screen program.");
		}
	}
}
