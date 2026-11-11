using System.Security.Cryptography;
using System.Text;

namespace NativePoc;

/// <summary>
/// A screen as data. The bytes are a stack program the process already contains.
/// Applying one does not load an assembly.
/// </summary>
public static class ScreenProgram
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

	public const int MaxStrings = 64;
	public const int MaxStringBytes = 4096;
	public const int MaxCode = 8192;
	public const int MaxStack = 16;
	public const int MaxLocals = 8;
	public const int MaxChildren = 8;

	public static bool IsProgram(ReadOnlySpan<byte> payload) =>
		payload.Length >= 4
		&& payload[0] == (byte)'N'
		&& payload[1] == (byte)'P'
		&& payload[2] == (byte)'C'
		&& payload[3] == (byte)'1';

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

	public static Bound Bind(ReadOnlySpan<byte> payload)
	{
		var decoded = Decode(payload);
		Verify(decoded.Code, decoded.Strings.Length);
		var code = decoded.Code;
		var strings = decoded.Strings;
		var sourceHash = decoded.SourceHash;
		return new Bound(
			(state, platform) => Eval(code, strings, state, platform),
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

		public void Return() => _code.Add(OpReturn);

		public byte[] Finish(string? source)
		{
			if (_code.Count == 0 || _code.Count > MaxCode)
				throw new InvalidOperationException("A screen program has no body.");
			var hash = new byte[8];
			if (source is not null)
				SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 8).CopyTo(hash);
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
			payload[5] = 0;
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

	readonly record struct Decoded(string[] Strings, byte[] Code, string SourceHash);

	static Decoded Decode(ReadOnlySpan<byte> payload)
	{
		if (!IsProgram(payload))
			throw new InvalidOperationException("Rejected screen program.");
		if (payload.Length < 6 + HashSize || payload[4] != Version || payload[5] != 0)
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
		return new Decoded(strings, code, sourceHash);
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
	}

	static void Verify(byte[] code, int stringCount)
	{
		var seen = new Dictionary<int, string>();
		var work = new Queue<Frame>();
		work.Enqueue(new Frame(0, new Kind[MaxStack], 0, new Kind[MaxLocals]));
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

			if ((uint)frame.Ip >= (uint)code.Length)
				throw new InvalidOperationException("Rejected screen program.");
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
					if (target <= frame.Ip)
						throw new InvalidOperationException("Rejected screen program.");
					work.Enqueue(Copy(ip, stack, sp, locals));
					work.Enqueue(Copy(target, stack, sp, locals));
					break;
				}
				case OpJump:
				{
					Require(ref ip, code, 2);
					var target = ip + I16(code, ip - 2);
					if (target <= frame.Ip)
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
					if (sp != 1 || stack[0] != Kind.Elem)
						throw new InvalidOperationException("Rejected screen program.");
					returned = true;
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
	}

	static Element Eval(byte[] code, string[] strings, AppState state, IPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(platform);
		var stack = new Slot[MaxStack];
		var locals = new Slot[MaxLocals];
		var sp = 0;
		var ip = 0;
		var steps = 0;
		while (ip < code.Length)
		{
			if (++steps > code.Length)
				throw new InvalidOperationException("Screen program did not return.");
			var op = code[ip++];
			switch (op)
			{
				case OpPushString:
					stack[sp++] = Slot.Str(strings[U16(code, ip)]);
					ip += 2;
					break;
				case OpPushCount:
					stack[sp++] = Slot.Str(state.Count.ToString());
					break;
				case OpPushName:
					stack[sp++] = Slot.Str(state.Name);
					break;
				case OpPushPlainRoot:
					stack[sp++] = Slot.Bool(state.PlainRoot);
					break;
				case OpPushGreetingFirst:
					stack[sp++] = Slot.Bool(state.GreetingFirst);
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
					stack[sp++] = Slot.Elem(new TextField(
						value,
						next =>
						{
							state.Name = next;
							platform.RequestRender();
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
					stack[sp++] = Slot.Elem(new Button(
						title,
						() =>
						{
							state.Count++;
							platform.CopyNonce(prefix + state.Count);
							platform.RequestRender();
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
				default:
					throw new InvalidOperationException("Rejected screen program.");
			}
		}

		throw new InvalidOperationException("Screen program did not return.");
	}
}
