using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

public static partial class SubsetLowerer
{
	sealed partial class Lower
	{
		readonly Dictionary<string, UserRoutine> _routines = new(StringComparer.Ordinal);
		List<int>? _returnPatches;
		bool _inInstance;

		sealed class UserRoutine
		{
			public UserRoutine(int index, byte kind, bool element)
			{
				Index = index;
				Kind = kind;
				Element = element;
			}

			public int Index { get; }

			public byte Kind { get; }

			public bool Element { get; }
		}

		bool LowerRoutines()
		{
			var screen = new List<MethodDeclarationSyntax>();
			var state = new List<MethodDeclarationSyntax>();
			foreach (var pair in _methods.OrderBy(item => item.Value.SpanStart))
			{
				if (pair.Key == _stateType + ".Commit" || pair.Key == _stateType + ".Drop")
					continue;
				if (pair.Value.TypeParameterList is not null)
				{
					if (!pair.Key.Contains('.', StringComparison.Ordinal))
						return Fail(UnknownMember, "The screen subset does not include a generic method.", pair.Value);
					continue;
				}

				if (pair.Key.Contains('.', StringComparison.Ordinal))
					state.Add(pair.Value);
				else
					screen.Add(pair.Value);
			}

			foreach (var method in screen)
			{
				if (!Prepare(method, method.Identifier.ValueText, required: true))
					return false;
			}

			foreach (var method in screen)
			{
				if (!EmitRoutine(method, method.Identifier.ValueText))
					return false;
			}

			foreach (var method in state)
			{
				var key = _stateType + "." + method.Identifier.ValueText;
				if (!Prepare(method, key, required: false))
					continue;
				var errors = Errors.Count;
				if (!EmitRoutine(method, key))
				{
					if (Errors.Count > errors)
						Errors.RemoveRange(errors, Errors.Count - errors);
					_routines.Remove(key);
				}
			}

			return Errors.Count == 0;
		}

		bool Prepare(MethodDeclarationSyntax method, string key, bool required)
		{
			var kind = ReturnKind(method, out var element);
			if (kind == 255 || !ArgKinds(method, element, out _))
			{
				if (!required)
					return false;
				return Fail(UnknownMember, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
			}

			_routines[key] = new UserRoutine(_routines.Count, kind, element);
			return true;
		}

		bool EmitRoutine(MethodDeclarationSyntax method, string key)
		{
			if (!_routines.TryGetValue(key, out var routine))
				return Fail(UnknownMember, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
			ArgKinds(method, routine.Element, out var args);
			var savedLocals = _locals;
			var savedSlots = _localSlots;
			var savedItem = _item;
			var savedFields = _itemFields;
			var savedReturns = _returnPatches;
			_locals = new Dictionary<string, (byte Slot, ValueKind Kind)>(StringComparer.Ordinal);
			_localSlots = 0;
			_returnPatches = new List<int>();
			_inInstance = !method.Modifiers.Any(SyntaxKind.StaticKeyword) && key.Contains('.', StringComparison.Ordinal);
			try
			{
				if (routine.Element)
					return EmitElement(method, routine);
				if (!BindArgs(method, args))
					return false;
				var buf = new VmBuf();
				if (!WriteRoutine(method, buf))
					return false;
				_code.Prop(RoutineProp(key, routine.Kind, args, false, buf.Bytes.ToArray()));
				return true;
			}
			finally
			{
				_locals = savedLocals;
				_localSlots = savedSlots;
				_item = savedItem;
				_itemFields = savedFields;
				_returnPatches = savedReturns;
				_inInstance = false;
			}
		}

		bool EmitElement(MethodDeclarationSyntax method, UserRoutine routine)
		{
			if (method.ParameterList.Parameters.Count != 1
				|| method.ParameterList.Parameters[0].Type is not TypeSyntax type
				|| !_records.TryGetValue(type.ToString(), out var fields))
				return Fail(UnknownElement, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
			_item = method.ParameterList.Parameters[0].Identifier.ValueText;
			_itemFields = fields;
			var at = _code.Here;
			var ok = method.ExpressionBody is not null
				? Element(method.ExpressionBody.Expression, new KeySet()) && Return()
				: method.Body is not null && method.Body.Statements.Count == 1 && method.Body.Statements[0] is ReturnStatementSyntax ret && Returned(ret);
			if (!ok)
			{
				_code.Truncate(at);
				return false;
			}

			var code = new byte[_code.Here - at];
			for (var index = 0; index < code.Length; index++)
				code[index] = _code.ByteAt(at + index);
			_code.Truncate(at);
			_code.Prop(RoutineProp(method.Identifier.ValueText, routine.Kind, Array.Empty<byte>(), true, code));
			return true;
		}

		bool BindArgs(MethodDeclarationSyntax method, byte[] args)
		{
			if (_locals is null)
				return false;
			for (var index = 0; index < method.ParameterList.Parameters.Count; index++)
			{
				var name = method.ParameterList.Parameters[index].Identifier.ValueText;
				_locals[name] = ((byte)_localSlots, KindOf(args[index]));
				_localSlots++;
			}

			return _localSlots <= 8;
		}

		bool WriteRoutine(MethodDeclarationSyntax method, VmBuf buf)
		{
			if (method.ExpressionBody is not null)
				return WriteExpr(method.ExpressionBody.Expression, buf, out _);
			if (method.Body is null || _returnPatches is null)
				return Fail(UnknownMember, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
			foreach (var statement in method.Body.Statements)
			{
				if (!WriteVmStatement(statement, "", buf))
					return false;
			}

			foreach (var at in _returnPatches)
			{
				var relative = buf.Here - (at + 2);
				if (relative != (short)relative)
					return Fail(UnknownMember, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
				buf.Bytes[at] = (byte)relative;
				buf.Bytes[at + 1] = (byte)(relative >> 8);
			}

			return true;
		}

		RecordProgram.PropDef RoutineProp(string name, byte kind, byte[] args, bool element, byte[] code)
		{
			var fields = new int[args.Length];
			for (var index = 0; index < args.Length; index++)
				fields[index] = args[index];
			return new RecordProgram.PropDef(RecordProgram.TypeRoutine, _code.Intern(name), kind, element, args.Length, fields, Array.Empty<int[]>(), code);
		}

		bool UserCall(InvocationExpressionSyntax invocation, out UserRoutine routine)
		{
			routine = null!;
			if (invocation.Expression is IdentifierNameSyntax name
				&& _routines.TryGetValue(name.Identifier.ValueText, out var found)
				&& !found.Element)
			{
				routine = found;
				return true;
			}

			if (invocation.Expression is MemberAccessExpressionSyntax member
				&& member.Expression is IdentifierNameSyntax owner
				&& owner.Identifier.ValueText == _state
				&& _routines.TryGetValue(_stateType + "." + member.Name.Identifier.ValueText, out var state)
				&& !state.Element)
			{
				routine = state;
				return true;
			}

			return false;
		}

		bool WriteUser(UserRoutine routine, InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = routine.Kind;
			if (invocation.ArgumentList.Arguments.Count > 8)
				return Fail(BadLambda, ActionLimit, invocation);
			foreach (var argument in invocation.ArgumentList.Arguments)
			{
				if (!WriteExpr(argument.Expression, buf, out _))
					return false;
			}

			buf.Op(RecordProgram.VmUser);
			buf.U16(routine.Index);
			return true;
		}

		static byte ReturnKind(MethodDeclarationSyntax method, out bool element)
		{
			element = false;
			var text = method.ReturnType.ToString();
			if (text == "void")
				return 0;
			if (text == "int")
				return RecordProgram.VmKindInt;
			if (text == "double")
				return RecordProgram.VmKindDouble;
			if (text == "bool")
				return RecordProgram.VmKindBool;
			if (text == "string")
				return RecordProgram.VmKindStr;
			if (text is "Element" or "NativePoc.Element")
			{
				element = true;
				return 5;
			}

			return 255;
		}

		static bool ArgKinds(MethodDeclarationSyntax method, bool element, out byte[] kinds)
		{
			kinds = Array.Empty<byte>();
			if (element)
				return method.ParameterList.Parameters.Count == 1;
			if (method.ParameterList.Parameters.Count > 8)
				return false;
			kinds = new byte[method.ParameterList.Parameters.Count];
			for (var index = 0; index < kinds.Length; index++)
			{
				kinds[index] = method.ParameterList.Parameters[index].Type?.ToString() switch
				{
					"int" => RecordProgram.VmKindInt,
					"double" => RecordProgram.VmKindDouble,
					"bool" => RecordProgram.VmKindBool,
					"string" => RecordProgram.VmKindStr,
					_ => 0,
				};
				if (kinds[index] == 0)
					return false;
			}

			return true;
		}
	}
}
