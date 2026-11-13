using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace NativePoc;

internal static partial class RecordProgram
{
	public const byte VmLitI = 1;
	public const byte VmLitD = 2;
	public const byte VmLitB = 3;
	public const byte VmLitS = 4;
	public const byte VmLitNull = 5;
	public const byte VmLoadI = 6;
	public const byte VmLoadB = 7;
	public const byte VmLoadS = 8;
	public const byte VmLoadLocal = 9;
	public const byte VmStoreI = 10;
	public const byte VmStoreB = 11;
	public const byte VmStoreS = 12;
	public const byte VmStoreLocal = 13;
	public const byte VmPop = 14;
	public const byte VmDup = 15;
	public const byte VmNeg = 16;
	public const byte VmNot = 17;
	public const byte VmAdd = 18;
	public const byte VmSub = 19;
	public const byte VmMul = 20;
	public const byte VmDiv = 21;
	public const byte VmRem = 22;
	public const byte VmEq = 23;
	public const byte VmNe = 24;
	public const byte VmLt = 25;
	public const byte VmLe = 26;
	public const byte VmGt = 27;
	public const byte VmGe = 28;
	public const byte VmText = 29;
	public const byte VmToInt = 30;
	public const byte VmToDouble = 31;
	public const byte VmJump = 32;
	public const byte VmJumpIfFalse = 33;
	public const byte VmJumpIfNotNull = 34;
	public const byte VmJumpIfNull = 35;
	public const byte VmCall = 36;
	public const byte VmIncProp = 37;
	public const byte VmIncLocal = 38;
	public const byte VmLoadCount = 39;
	public const byte VmLoadCell = 40;
	public const byte VmRowRemove = 41;
	public const byte VmRowContains = 42;
	public const byte VmRowIndex = 43;
	public const byte VmNest = 44;
	public const byte VmTryParse = 45;
	public const byte VmUser = 46;
	public const byte VmWatch = 47;
	public const byte VmApp = 48;
	public const byte VmTry = 49;
	public const byte VmLeave = 50;
	public const byte VmEndFinally = 51;
	public const byte VmLeaveTo = 52;
	public const byte VmSnap = 53;
	public const byte VmSnapCount = 54;
	public const byte VmSnapEnd = 55;
	public const byte VmLoadRow = 56;

	public const string CollectionModified = "Collection was modified; enumeration operation may not execute.";

	internal readonly struct AppValue
	{
		public byte Kind { get; }

		public int Int { get; }

		public bool Flag { get; }

		public string? Text { get; }

		AppValue(byte kind, int whole, bool flag, string? text)
		{
			Kind = kind;
			Int = whole;
			Flag = flag;
			Text = text;
		}

		public static AppValue FromInt(int value) => new(VmKindInt, value, false, null);

		public static AppValue FromBool(bool value) => new(VmKindBool, 0, value, null);

		public static AppValue FromStr(string value) => new(VmKindStr, 0, false, value);
	}

	internal delegate AppValue AppDispatch(string typeName, string methodName, AppValue[] args);

	static AppDispatch? _appCall;

	public static void BindAppCall(AppDispatch? dispatch) => _appCall = dispatch;

	sealed class Replacement
	{
		public Replacement(byte[] code, string[] strings, byte[] argKinds, byte returnKind)
		{
			Code = code;
			Strings = strings;
			ArgKinds = argKinds;
			ReturnKind = returnKind;
		}

		public byte[] Code { get; }

		public string[] Strings { get; }

		public byte[] ArgKinds { get; }

		public byte ReturnKind { get; }
	}

	static readonly Dictionary<string, Replacement> _replacements = new(StringComparer.Ordinal);

	public static void ClearReplacements() => _replacements.Clear();

	public static string InstallReplacement(ReadOnlySpan<byte> body)
	{
		var cursor = 0;
		var typeName = ReadUtf8(body, ref cursor);
		var methodName = ReadUtf8(body, ref cursor);
		if (cursor >= body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var returnKind = body[cursor++];
		if (cursor >= body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var argc = body[cursor++];
		if (argc > 8 || cursor + argc > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var argKinds = body.Slice(cursor, argc).ToArray();
		cursor += argc;
		if (cursor + 4 > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var codeLen = body[cursor] | (body[cursor + 1] << 8) | (body[cursor + 2] << 16) | (body[cursor + 3] << 24);
		cursor += 4;
		if (codeLen < 0 || cursor + codeLen > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var code = body.Slice(cursor, codeLen).ToArray();
		cursor += codeLen;
		if (cursor + 2 > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var count = body[cursor] | (body[cursor + 1] << 8);
		cursor += 2;
		var strings = new string[count];
		for (var index = 0; index < count; index++)
			strings[index] = ReadUtf8(body, ref cursor);
		if (cursor != body.Length || typeName.Length == 0 || methodName.Length == 0 || code.Length == 0)
			throw new InvalidOperationException("Rejected screen query.");
		var slot = new Replacement(code, strings, argKinds, returnKind);
		_replacements[typeName + "\n" + methodName] = slot;
		var dot = typeName.LastIndexOf('.');
		if (dot >= 0 && dot < typeName.Length - 1)
			_replacements[typeName[(dot + 1)..] + "\n" + methodName] = slot;
		return typeName + "." + methodName;
	}

	public static bool TryRunReplacement(string typeName, string methodName, AppValue[] args, out AppValue result)
	{
		result = default;
		if (!_replacements.TryGetValue(typeName + "\n" + methodName, out var slot))
			return false;
		var frame = new HandlerFrame();
		var count = args.Length < slot.ArgKinds.Length ? args.Length : slot.ArgKinds.Length;
		for (var index = 0; index < count; index++)
			StoreLocal(frame, slot.ArgKinds[index], index, FromApp(args[index]));
		var value = RunVm(slot.Code, 0, slot.Code.Length, slot.Strings, new Bag(), null!, null, false, null, frame, 0);
		result = slot.ReturnKind == 0 ? default : ToApp(value);
		return true;
	}

	static string ReadUtf8(ReadOnlySpan<byte> body, ref int cursor)
	{
		if (cursor + 2 > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var length = body[cursor] | (body[cursor + 1] << 8);
		cursor += 2;
		if (length < 0 || cursor + length > body.Length)
			throw new InvalidOperationException("Rejected screen query.");
		var text = Encoding.UTF8.GetString(body.Slice(cursor, length));
		cursor += length;
		return text;
	}

	static readonly string NullMessage = new NullReferenceException().Message;

	public const byte VmKindInt = 1;
	public const byte VmKindDouble = 2;
	public const byte VmKindBool = 3;
	public const byte VmKindStr = 4;

	public const byte VmIncPre = 1;
	public const byte VmIncPost = 2;
	public const byte VmDecPre = 3;
	public const byte VmDecPost = 4;

	public const ushort VmTrim = 1;
	public const ushort VmUpper = 2;
	public const ushort VmLower = 3;
	public const ushort VmContains = 4;
	public const ushort VmStarts = 5;
	public const ushort VmSubstring = 6;
	public const ushort VmLength = 7;
	public const ushort VmMin = 8;
	public const ushort VmMax = 9;
	public const ushort VmAbs = 10;
	public const ushort VmRound = 11;
	public const ushort VmParse = 12;

	const byte ValNull = 0;
	const byte ValInt = 1;
	const byte ValDouble = 2;
	const byte ValBool = 3;
	const byte ValStr = 4;

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static Val RunVm(byte[] code, int origin, int length, string[] strings, Bag bag, IPlatform platform, string? text, bool flag, string? rowId, HandlerFrame frame, int depth)
	{
		var stack = new Val[64];
		var sp = 0;
		var ip = origin;
		var end = origin + length;
		while (ip < end)
		{
			if (++frame.Steps > ActionStepBudget)
				throw new ActionStoppedException("action stopped after " + ActionStepBudget.ToString(CultureInfo.InvariantCulture) + " steps");
			try
			{
			var op = code[ip++];
			switch (op)
			{
				case VmLitI:
				{
					if (ip + 4 > end || sp >= stack.Length)
						throw new InvalidOperationException("Rejected record program.");
					var value = code[ip] | (code[ip + 1] << 8) | (code[ip + 2] << 16) | (code[ip + 3] << 24);
					ip += 4;
					stack[sp++] = Val.FromInt(value);
					break;
				}
				case VmLitD:
					Push(stack, ref sp, Val.FromDouble(ReadF64(code, ref ip)));
					break;
				case VmLitB:
					Push(stack, ref sp, Val.FromBool(ReadBit(code, ref ip)));
					break;
				case VmLitS:
					Push(stack, ref sp, Val.FromStr(strings[RequireString(TakeU16(code, ref ip, end), strings)]));
					break;
				case VmLitNull:
					Push(stack, ref sp, default);
					break;
				case VmLoadI:
					Push(stack, ref sp, Val.FromInt(bag.ReadInt(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
					break;
				case VmLoadB:
					Push(stack, ref sp, Val.FromBool(bag.ReadBool(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
					break;
				case VmLoadS:
					Push(stack, ref sp, Val.FromStr(bag.ReadString(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
					break;
				case VmLoadLocal:
				{
					if (ip + 2 > end || sp >= stack.Length)
						throw new InvalidOperationException("Rejected record program.");
					var kind = code[ip++];
					var slot = code[ip++];
					if (slot > 7)
						throw new InvalidOperationException("Rejected record program.");
					stack[sp++] = kind == VmKindInt ? Val.FromInt(frame.Ints[slot]) : LoadLocal(frame, kind, slot);
					break;
				}
				case VmStoreI:
					bag.WriteInt(strings[RequireString(TakeU16(code, ref ip, end), strings)], AsInt(Pop(stack, ref sp)));
					break;
				case VmStoreB:
					bag.WriteBool(strings[RequireString(TakeU16(code, ref ip, end), strings)], AsBool(Pop(stack, ref sp)));
					break;
				case VmStoreS:
					bag.WriteString(strings[RequireString(TakeU16(code, ref ip, end), strings)], AsText(Pop(stack, ref sp)));
					break;
				case VmStoreLocal:
					StoreLocal(frame, ReadKind(code, ref ip), ReadSlot(code, ref ip), Pop(stack, ref sp));
					break;
				case VmPop:
					if (sp == 0)
						throw new InvalidOperationException("Rejected record program.");
					sp--;
					break;
				case VmDup:
				{
					var top = Pop(stack, ref sp);
					Push(stack, ref sp, top);
					Push(stack, ref sp, top);
					break;
				}
				case VmNeg:
					Push(stack, ref sp, Negate(Pop(stack, ref sp)));
					break;
				case VmNot:
					Push(stack, ref sp, Val.FromBool(!AsBool(Pop(stack, ref sp))));
					break;
				case VmAdd:
				case VmSub:
				case VmMul:
				case VmDiv:
				case VmRem:
				{
					var right = Pop(stack, ref sp);
					var left = Pop(stack, ref sp);
					Push(stack, ref sp, Arithmetic(op, left, right));
					break;
				}
				case VmEq:
				case VmNe:
				case VmLt:
				case VmLe:
				case VmGt:
				case VmGe:
				{
					var right = stack[--sp];
					var left = stack[--sp];
					var result = op == VmLt && left.Kind == ValInt && right.Kind == ValInt
						? left.Int < right.Int
						: Compare(op, left, right);
					stack[sp++] = Val.FromBool(result);
					break;
				}
				case VmText:
					Push(stack, ref sp, Val.FromStr(AsText(Pop(stack, ref sp))));
					break;
				case VmToInt:
					Push(stack, ref sp, Val.FromInt(AsInt(Pop(stack, ref sp))));
					break;
				case VmToDouble:
					Push(stack, ref sp, Val.FromDouble(AsDouble(Pop(stack, ref sp))));
					break;
				case VmJump:
				{
					if (ip + 2 > end)
						throw new InvalidOperationException("Rejected record program.");
					var rel = (short)(code[ip] | (code[ip + 1] << 8));
					ip += 2 + rel;
					if ((uint)ip > (uint)end)
						throw new InvalidOperationException("Rejected record program.");
					break;
				}
				case VmJumpIfFalse:
				{
					if (ip + 2 > end || sp == 0)
						throw new InvalidOperationException("Rejected record program.");
					var rel = (short)(code[ip] | (code[ip + 1] << 8));
					ip += 2;
					var flagBit = stack[--sp];
					if (flagBit.Kind != ValBool)
						throw new ActionStoppedException("action stopped: the value is not a bool");
					if (!flagBit.Flag)
					{
						ip += rel;
						if ((uint)ip > (uint)end)
							throw new InvalidOperationException("Rejected record program.");
					}

					break;
				}
				case VmJumpIfNotNull:
				{
					var rel = ReadI16(code, ref ip);
					if (sp == 0)
						throw new InvalidOperationException("Rejected record program.");
					if (!IsNull(stack[sp - 1]))
						ip = Jump(ip, end, rel);
					else
						Pop(stack, ref sp);
					break;
				}
				case VmJumpIfNull:
				{
					var rel = ReadI16(code, ref ip);
					var probed = Pop(stack, ref sp);
					if (IsNull(probed))
						ip = Jump(ip, end, rel);
					break;
				}
				case VmCall:
				{
					var id = TakeU16(code, ref ip, end);
					var argc = code[ip++];
					var args = new Val[argc];
					for (var index = argc - 1; index >= 0; index--)
						args[index] = Pop(stack, ref sp);
					Push(stack, ref sp, Call(id, args));
					break;
				}
				case VmIncProp:
				{
					var name = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					Push(stack, ref sp, Val.FromInt(StepInt(bag.ReadInt(name), code[ip++], out var updated)));
					bag.WriteInt(name, updated);
					break;
				}
				case VmIncLocal:
				{
					if (ip + 2 > end || sp >= stack.Length)
						throw new InvalidOperationException("Rejected record program.");
					var slot = code[ip++];
					var mode = code[ip++];
					if (slot > 7)
						throw new InvalidOperationException("Rejected record program.");
					if (mode == VmIncPost)
					{
						var current = frame.Ints[slot];
						frame.Ints[slot] = current + 1;
						stack[sp++] = Val.FromInt(current);
						break;
					}

					var updated = StepInt(frame.Ints[slot], mode, out var stored);
					frame.Ints[slot] = stored;
					stack[sp++] = Val.FromInt(updated);
					break;
				}
				case VmLoadCount:
					Push(stack, ref sp, Val.FromInt(bag.CountRows(strings[RequireString(TakeU16(code, ref ip, end), strings)])));
					break;
				case VmLoadCell:
				{
					var list = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					var slot = ReadSlot(code, ref ip);
					var field = TakeU16(code, ref ip, end);
					var index = frame.Ints[slot];
					if (frame.SnapList == list && frame.SnapRows is { } snap && (uint)index < (uint)snap.Count)
					{
						var cells = snap[index];
						Push(stack, ref sp, Val.FromStr((uint)field < (uint)cells.Length ? cells[field] : ""));
						break;
					}

					Push(stack, ref sp, Val.FromStr(bag.CellAt(list, index, field)));
					break;
				}
				case VmSnap:
				{
					var list = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					frame.SnapList = list;
					frame.SnapRows = bag.CopyRows(list);
					break;
				}
				case VmSnapCount:
					Push(stack, ref sp, Val.FromInt(frame.SnapRows?.Count ?? 0));
					break;
				case VmSnapEnd:
					frame.SnapList = null;
					frame.SnapRows = null;
					break;
				case VmLoadRow:
				{
					var field = TakeU16(code, ref ip, end);
					var cells = frame.Row;
					if (cells is null || (uint)field >= (uint)cells.Length)
						throw new InvalidOperationException("Rejected record program.");
					Push(stack, ref sp, Val.FromStr(cells[field]));
					break;
				}
				case VmWatch:
				{
					var watched = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					var stamp = bag.ListStamp(watched);
					if (frame.Watching && frame.WatchList == watched && frame.WatchStamp != stamp)
						throw new ActionStoppedException(CollectionModified);
					frame.Watching = true;
					frame.WatchList = watched;
					frame.WatchStamp = stamp;
					break;
				}
				case VmRowRemove:
				case VmRowContains:
				case VmRowIndex:
				{
					var list = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					var slot = ReadSlot(code, ref ip);
					var index = frame.Ints[slot];
					if (frame.SnapList == list && frame.SnapRows is { } snap && (uint)index < (uint)snap.Count)
					{
						var row = snap[index];
						var live = bag.IndexOfRow(list, row);
						if (op == VmRowRemove && live >= 0)
							bag.RemoveAt(list, live);
						if (op == VmRowIndex)
							Push(stack, ref sp, Val.FromInt(live));
						else
							Push(stack, ref sp, Val.FromBool(live >= 0));
						break;
					}

					var present = bag.RowInRange(list, index);
					if (op == VmRowRemove && present)
						bag.RemoveAt(list, index);
					if (op == VmRowIndex)
						Push(stack, ref sp, Val.FromInt(present ? index : -1));
					else
						Push(stack, ref sp, Val.FromBool(present));
					break;
				}
				case VmNest:
				{
					var nested = TakeU16(code, ref ip, end);
					if (ip + nested > end)
						throw new InvalidOperationException("Rejected record program.");
					var start = ip;
					ip += nested;
					RunSteps(code, strings, bag, platform, text, flag, rowId, frame, depth + 1, start, nested);
					break;
				}
				case VmUser:
				{
					var routine = bag.RoutineAt(TakeU16(code, ref ip, end));
					if (routine.Element || routine.Argc > 8)
						throw new InvalidOperationException("Rejected record program.");
					var args = new Val[routine.Argc];
					for (var index = routine.Argc - 1; index >= 0; index--)
						args[index] = Pop(stack, ref sp);
					frame.EnterCall();
					try
					{
						for (var index = 0; index < routine.Argc; index++)
							StoreLocal(frame, routine.ArgKinds[index], index, args[index]);
						var result = RunVm(routine.Code, 0, routine.Code.Length, strings, bag, platform, text, flag, rowId, frame, depth + 1);
						if (routine.ReturnKind != 0)
							Push(stack, ref sp, result);
					}
					finally
					{
						frame.LeaveCall();
					}

					break;
				}
				case VmTryParse:
				{
					var slot = ReadSlot(code, ref ip);
					var parsed = int.TryParse(AsText(Pop(stack, ref sp)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
					frame.Ints[slot] = parsed ? number : 0;
					Push(stack, ref sp, Val.FromBool(parsed));
					break;
				}
				case VmApp:
				{
					var typeName = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					var methodName = strings[RequireString(TakeU16(code, ref ip, end), strings)];
					if (ip >= end)
						throw new InvalidOperationException("Rejected record program.");
					var argc = code[ip++];
					if (argc > 8)
						throw new InvalidOperationException("Rejected record program.");
					var passed = new AppValue[argc];
					for (var index = argc - 1; index >= 0; index--)
						passed[index] = ToApp(Pop(stack, ref sp));
					if (_appCall is null)
						throw new InvalidOperationException("No compiled method " + typeName + "." + methodName + ". The call table is not installed.");
					var result = _appCall(typeName, methodName, passed);
					if (result.Kind != 0)
						Push(stack, ref sp, FromApp(result));
					break;
				}
				case VmTry:
				{
					var catchRel = ReadI16(code, ref ip);
					var finallyRel = ReadI16(code, ref ip);
					var endRel = ReadI16(code, ref ip);
					var type = TakeU16(code, ref ip, end);
					var here = ip;
					frame.EnterVm(
						catchRel == short.MinValue ? -1 : here + catchRel,
						finallyRel == short.MinValue ? -1 : here + finallyRel,
						here + endRel,
						type == 0xFFFF ? -1 : type,
						sp,
						depth);
					break;
				}
				case VmLeave:
					frame.LeaveVm(ref ip, frame.VmEnd(depth), depth);
					break;
				case VmLeaveTo:
				{
					var rel = ReadI16(code, ref ip);
					frame.LeaveVm(ref ip, ip + rel, depth);
					break;
				}
				case VmEndFinally:
					frame.EndVmFinally(strings, ref ip, ref sp, depth);
					break;
				default:
					throw new InvalidOperationException("Rejected record program.");
			}
			}
			catch (Exception ex) when (IsUserFault(ex))
			{
				if (!frame.DispatchVm(strings, ex, ref ip, ref sp, depth))
				{
					if (frame.ActCount > 0 || ex is InvalidOperationException)
						throw;
					throw new ActionStoppedException(ex.Message);
				}
			}
		}

		if (ip != end)
			throw new InvalidOperationException("Rejected record program.");
		return sp == 0 ? default : stack[sp - 1];
	}

	static int StepInt(int current, byte mode, out int stored)
	{
		switch (mode)
		{
			case VmIncPre:
				stored = current + 1;
				return stored;
			case VmIncPost:
				stored = current + 1;
				return current;
			case VmDecPre:
				stored = current - 1;
				return stored;
			case VmDecPost:
				stored = current - 1;
				return current;
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static Val Call(int id, Val[] args)
	{
		switch (id)
		{
			case VmTrim:
				return Val.FromStr(RequireText(args, 1).Trim());
			case VmUpper:
				return Val.FromStr(RequireText(args, 1).ToUpperInvariant());
			case VmLower:
				return Val.FromStr(RequireText(args, 1).ToLowerInvariant());
			case VmContains:
			case VmStarts:
			{
				if (args.Length is < 2 or > 3)
					throw new InvalidOperationException("Rejected record program.");
				if (IsNull(args[0]))
					throw new ActionStoppedException(NullMessage);
				var comparison = StringComparison.Ordinal;
				if (args.Length == 3)
				{
					if (args[2].Kind != ValBool)
						throw new InvalidOperationException("Rejected record program.");
					if (args[2].Flag)
						comparison = StringComparison.OrdinalIgnoreCase;
				}

				var text = AsText(args[0]);
				var part = AsText(args[1]);
				var hit = id == VmContains
					? text.Contains(part, comparison)
					: text.StartsWith(part, comparison);
				return Val.FromBool(hit);
			}
			case VmSubstring:
			{
				var source = RequireText(args, args.Length);
				var start = AsInt(args[1]);
				return Val.FromStr(args.Length == 3 ? source.Substring(start, AsInt(args[2])) : source.Substring(start));
			}
			case VmLength:
				return Val.FromInt(RequireText(args, 1).Length);
			case VmMin:
			case VmMax:
				return Extreme(id == VmMax, args);
			case VmAbs:
				return Absolute(RequireNumber(args, 1));
			case VmRound:
				return Val.FromDouble(Math.Round(AsDouble(RequireNumber(args, 1)), MidpointRounding.ToEven));
			case VmParse:
				return Val.FromInt(int.Parse(RequireText(args, 1), NumberStyles.Integer, CultureInfo.InvariantCulture));
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static Val Extreme(bool max, Val[] args)
	{
		if (args.Length != 2)
			throw new InvalidOperationException("Rejected record program.");
		if (args[0].Kind == ValDouble || args[1].Kind == ValDouble)
		{
			var left = AsDouble(args[0]);
			var right = AsDouble(args[1]);
			return Val.FromDouble(max ? Math.Max(left, right) : Math.Min(left, right));
		}

		var a = AsInt(args[0]);
		var b = AsInt(args[1]);
		return Val.FromInt(max ? Math.Max(a, b) : Math.Min(a, b));
	}

	static Val Absolute(Val value)
	{
		if (value.Kind == ValDouble)
			return Val.FromDouble(Math.Abs(value.Double));
		return Val.FromInt(Math.Abs(AsInt(value)));
	}

	static string RequireText(Val[] args, int count)
	{
		if (args.Length != count)
			throw new InvalidOperationException("Rejected record program.");
		if (IsNull(args[0]))
			throw new ActionStoppedException(NullMessage);
		return AsText(args[0]);
	}

	static Val RequireNumber(Val[] args, int count)
	{
		if (args.Length != count)
			throw new InvalidOperationException("Rejected record program.");
		return args[0];
	}

	static Val Arithmetic(byte op, Val left, Val right)
	{
		if (op == VmAdd && (left.Kind == ValStr || right.Kind == ValStr || IsNull(left) || IsNull(right)))
			return Val.FromStr(AsText(left) + AsText(right));
		if (left.Kind == ValDouble || right.Kind == ValDouble)
		{
			var a = AsDouble(left);
			var b = AsDouble(right);
			var result = op switch
			{
				VmAdd => a + b,
				VmSub => a - b,
				VmMul => a * b,
				VmDiv => Divide(a, b),
				VmRem => Remainder(a, b),
				_ => throw new InvalidOperationException("Rejected record program."),
			};
			return Val.FromDouble(result);
		}

		var x = AsInt(left);
		var y = AsInt(right);
		var whole = op switch
		{
			VmAdd => x + y,
			VmSub => x - y,
			VmMul => x * y,
			VmDiv => Divide(x, y),
			VmRem => Remainder(x, y),
			_ => throw new InvalidOperationException("Rejected record program."),
		};
		return Val.FromInt(whole);
	}

	static double Divide(double left, double right) => left / right;

	static int Divide(int left, int right) => Whole(left, right, true);

	static double Remainder(double left, double right) => left % right;

	static int Remainder(int left, int right) => Whole(left, right, false);

	static int Whole(int left, int right, bool divide) => divide ? left / right : left % right;

	static bool Compare(byte op, Val left, Val right)
	{
		if (IsNull(left) || IsNull(right))
		{
			var same = IsNull(left) && IsNull(right);
			if (op == VmEq)
				return same;
			if (op == VmNe)
				return !same;
			throw new ActionStoppedException("action stopped: the value is null");
		}

		if (left.Kind == ValStr || right.Kind == ValStr)
		{
			var order = string.CompareOrdinal(AsText(left), AsText(right));
			return op switch
			{
				VmEq => order == 0,
				VmNe => order != 0,
				VmLt => order < 0,
				VmLe => order <= 0,
				VmGt => order > 0,
				VmGe => order >= 0,
				_ => throw new InvalidOperationException("Rejected record program."),
			};
		}

		if ((left.Kind == ValBool || right.Kind == ValBool) && op is VmEq or VmNe)
		{
			var same = AsBool(left) == AsBool(right);
			return op == VmEq ? same : !same;
		}

		if (left.Kind == ValDouble || right.Kind == ValDouble)
		{
			var a = AsDouble(left);
			var b = AsDouble(right);
			return op switch
			{
				VmEq => a == b,
				VmNe => a != b,
				VmLt => a < b,
				VmLe => a <= b,
				VmGt => a > b,
				VmGe => a >= b,
				_ => throw new InvalidOperationException("Rejected record program."),
			};
		}

		var x = AsInt(left);
		var y = AsInt(right);
		return op switch
		{
			VmEq => x == y,
			VmNe => x != y,
			VmLt => x < y,
			VmLe => x <= y,
			VmGt => x > y,
			VmGe => x >= y,
			_ => throw new InvalidOperationException("Rejected record program."),
		};
	}

	static Val Negate(Val value)
	{
		if (value.Kind == ValDouble)
			return Val.FromDouble(-value.Double);
		return Val.FromInt(-AsInt(value));
	}

	static Val LoadLocal(HandlerFrame frame, byte kind, int slot) => kind switch
	{
		VmKindInt => Val.FromInt(frame.Ints[slot]),
		VmKindDouble => Val.FromDouble(frame.Doubles[slot]),
		VmKindBool => Val.FromBool(frame.Flags[slot]),
		VmKindStr => frame.Strings[slot] is null ? default : Val.FromStr(frame.Strings[slot]!),
		_ => throw new InvalidOperationException("Rejected record program."),
	};

	static void StoreLocal(HandlerFrame frame, byte kind, int slot, Val value)
	{
		switch (kind)
		{
			case VmKindInt:
				frame.Ints[slot] = AsInt(value);
				break;
			case VmKindDouble:
				frame.Doubles[slot] = AsDouble(value);
				break;
			case VmKindBool:
				frame.Flags[slot] = AsBool(value);
				break;
			case VmKindStr:
				frame.Strings[slot] = IsNull(value) ? null : AsText(value);
				break;
			default:
				throw new InvalidOperationException("Rejected record program.");
		}
	}

	static AppValue ToApp(Val value) => value.Kind switch
	{
		ValInt => AppValue.FromInt(value.Int),
		ValBool => AppValue.FromBool(value.Flag),
		ValStr => AppValue.FromStr(value.Text ?? ""),
		ValNull => AppValue.FromStr(""),
		_ => throw new ActionStoppedException("action stopped: the call table takes int, bool, and string"),
	};

	static Val FromApp(AppValue value) => value.Kind switch
	{
		VmKindInt => Val.FromInt(value.Int),
		VmKindBool => Val.FromBool(value.Flag),
		VmKindStr => Val.FromStr(value.Text ?? ""),
		_ => throw new InvalidOperationException("No compiled method returned a value this program can carry."),
	};

	static int AsInt(Val value)
	{
		if (value.Kind == ValInt)
			return value.Int;
		if (value.Kind == ValDouble)
			return (int)value.Double;

		if (value.Kind == ValBool)
			return value.Flag ? 1 : 0;
		throw new ActionStoppedException("action stopped: the value is not an int");
	}

	static double AsDouble(Val value)
	{
		if (value.Kind == ValDouble)
			return value.Double;
		if (value.Kind == ValInt)
			return value.Int;
		throw new ActionStoppedException("action stopped: the value is not a number");
	}

	static bool AsBool(Val value)
	{
		if (value.Kind == ValBool)
			return value.Flag;
		throw new ActionStoppedException("action stopped: the value is not a bool");
	}

	static string AsText(Val value) => value.Kind switch
	{
		ValNull => "",
		ValStr => value.Text ?? "",
		ValInt => value.Int.ToString(CultureInfo.InvariantCulture),
		ValBool => value.Flag ? "true" : "false",
		ValDouble => value.Double.ToString(CultureInfo.InvariantCulture),
		_ => "",
	};

	static bool IsNull(Val value) => value.Kind == ValNull;

	static void Push(Val[] stack, ref int sp, Val value)
	{
		if (sp >= stack.Length)
			throw new InvalidOperationException("Rejected record program.");
		stack[sp++] = value;
	}

	static Val Pop(Val[] stack, ref int sp)
	{
		if (sp == 0)
			throw new InvalidOperationException("Rejected record program.");
		return stack[--sp];
	}

	static int Jump(int ip, int end, int relative)
	{
		var next = ip + relative;
		if (next < 0 || next > end)
			throw new InvalidOperationException("Rejected record program.");
		return next;
	}

	static byte ReadKind(byte[] code, ref int ip)
	{
		if (ip >= code.Length)
			throw new InvalidOperationException("Rejected record program.");
		var kind = code[ip++];
		if (kind is < VmKindInt or > VmKindStr)
			throw new InvalidOperationException("Rejected record program.");
		return kind;
	}

	static bool ReadBit(byte[] code, ref int ip)
	{
		if (ip >= code.Length)
			throw new InvalidOperationException("Rejected record program.");
		var bit = code[ip++];
		if (bit > 1)
			throw new InvalidOperationException("Rejected record program.");
		return bit == 1;
	}

	static short ReadI16(byte[] code, ref int ip)
	{
		if (ip + 2 > code.Length)
			throw new InvalidOperationException("Rejected record program.");
		var value = (short)(code[ip] | (code[ip + 1] << 8));
		ip += 2;
		return value;
	}

	static double ReadF64(byte[] code, ref int ip)
	{
		if (ip + 8 > code.Length)
			throw new InvalidOperationException("Rejected record program.");
		var value = BitConverter.Int64BitsToDouble(
			code[ip]
			| ((long)code[ip + 1] << 8)
			| ((long)code[ip + 2] << 16)
			| ((long)code[ip + 3] << 24)
			| ((long)code[ip + 4] << 32)
			| ((long)code[ip + 5] << 40)
			| ((long)code[ip + 6] << 48)
			| ((long)code[ip + 7] << 56));
		ip += 8;
		return value;
	}

	readonly struct Val
	{
		public readonly byte Kind;
		public readonly int Int;
		public readonly double Double;
		public readonly bool Flag;
		public readonly string? Text;

		Val(byte kind, int whole, double real, bool flag, string? text)
		{
			Kind = kind;
			Int = whole;
			Double = real;
			Flag = flag;
			Text = text;
		}

		public static Val FromInt(int value) => new(ValInt, value, 0, false, null);

		public static Val FromDouble(double value) => new(ValDouble, 0, value, false, null);

		public static Val FromBool(bool value) => new(ValBool, 0, 0, value, null);

		public static Val FromStr(string value) => new(ValStr, 0, 0, false, value);
	}
}
