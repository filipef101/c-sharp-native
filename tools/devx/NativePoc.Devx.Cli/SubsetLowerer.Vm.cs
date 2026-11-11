using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

public static partial class SubsetLowerer
{
	sealed partial class Lower
	{
		Dictionary<string, RowBind>? _boundRows;

		sealed class RowBind
		{
			public RowBind(string list, byte indexSlot, List<Field> fields)
			{
				List = list;
				IndexSlot = indexSlot;
				Fields = fields;
			}

			public string List { get; }

			public byte IndexSlot { get; }

			public List<Field> Fields { get; }
		}

		sealed class VmBuf
		{
			public readonly List<byte> Bytes = new();

			public int Here => Bytes.Count;

			public void Op(byte op) => Bytes.Add(op);

			public void Raw(byte value) => Bytes.Add(value);

			public void U16(int value)
			{
				Bytes.Add((byte)value);
				Bytes.Add((byte)(value >> 8));
			}

			public void I32(int value)
			{
				Bytes.Add((byte)value);
				Bytes.Add((byte)(value >> 8));
				Bytes.Add((byte)(value >> 16));
				Bytes.Add((byte)(value >> 24));
			}

			public void F64(double value)
			{
				var bits = BitConverter.DoubleToInt64Bits(value);
				for (var shift = 0; shift < 64; shift += 8)
					Bytes.Add((byte)(bits >> shift));
			}

			public int Reserve(byte op)
			{
				Op(op);
				var at = Bytes.Count;
				Bytes.Add(0);
				Bytes.Add(0);
				return at;
			}

			public void Patch(int at)
			{
				var relative = Here - (at + 2);
				if (relative != (short)relative)
					throw new InvalidOperationException("Rejected record program.");
				Bytes[at] = (byte)relative;
				Bytes[at + 1] = (byte)(relative >> 8);
			}

			public void Jump(int target)
			{
				Op(RecordProgram.VmJump);
				var at = Bytes.Count;
				var relative = target - (at + 2);
				if (relative != (short)relative)
					throw new InvalidOperationException("Rejected record program.");
				Bytes.Add((byte)relative);
				Bytes.Add((byte)(relative >> 8));
			}
		}

		bool VmExprStatement(ExpressionSyntax expression, string parameter, List<byte> script)
		{
			var buf = new VmBuf();
			if (!WriteStatementExpr(expression, parameter, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmStore(Field field, ExpressionSyntax expression, List<byte> script)
		{
			var buf = new VmBuf();
			if (!WriteExpr(expression, buf, out var kind) || !StoreField(field, kind, buf, expression))
				return false;
			return EmitVm(script, buf);
		}

		bool VmLocal(VariableDeclaratorSyntax variable, List<byte> script)
		{
			var buf = new VmBuf();
			if (!VmLocalInto(variable, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmIf(IfStatementSyntax branch, string parameter, List<byte> script)
		{
			var buf = new VmBuf();
			if (!VmIfInto(branch, parameter, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmWhile(WhileStatementSyntax loop, string parameter, List<byte> script)
		{
			var buf = new VmBuf();
			if (!VmWhileInto(loop, parameter, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmFor(ForStatementSyntax loop, string parameter, List<byte> script)
		{
			var buf = new VmBuf();
			if (!VmForInto(loop, parameter, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmForeach(ForEachStatementSyntax loop, string parameter, List<byte> script)
		{
			var buf = new VmBuf();
			if (!VmForeachInto(loop, parameter, buf))
				return false;
			return EmitVm(script, buf);
		}

		bool VmList(Field list, string method, InvocationExpressionSyntax invocation, List<byte> script)
		{
			var buf = new VmBuf();
			if (!WriteListCall(list, method, invocation, buf, out _))
				return false;
			buf.Op(RecordProgram.VmPop);
			return EmitVm(script, buf);
		}

		bool EmitVm(List<byte> script, VmBuf buf)
		{
			if (buf.Bytes.Count > ushort.MaxValue)
				return Fail(BadLambda, ActionLimit, "vm");
			script.Add(RecordProgram.ActVm);
			U16(script, buf.Bytes.Count);
			script.AddRange(buf.Bytes);
			return true;
		}

		bool WriteVmStatement(StatementSyntax statement, string parameter, VmBuf buf)
		{
			switch (statement)
			{
				case BlockSyntax block:
					foreach (var item in block.Statements)
					{
						if (!WriteVmStatement(item, parameter, buf))
							return false;
					}

					return true;
				case IfStatementSyntax branch:
					return VmIfInto(branch, parameter, buf);
				case WhileStatementSyntax loop:
					return VmWhileInto(loop, parameter, buf);
				case ForStatementSyntax loop:
					return VmForInto(loop, parameter, buf);
				case ForEachStatementSyntax loop:
					return VmForeachInto(loop, parameter, buf);
				case LocalDeclarationStatementSyntax local:
					if (local.Declaration.Variables.Count != 1)
						return Fail(BadLambda, ActionLimit, local);
					return VmLocalInto(local.Declaration.Variables[0], buf);
				case ExpressionStatementSyntax expression:
				{
					var nested = new List<byte>();
					if (TryOld(expression, parameter, nested))
						return Nest(buf, nested);
					return WriteStatementExpr(expression.Expression, parameter, buf);
				}
				case EmptyStatementSyntax:
					return true;
				default:
					return Fail(BadLambda, ActionLimit, statement);
			}
		}

		bool TryOld(StatementSyntax statement, string parameter, List<byte> into)
		{
			var errors = Errors.Count;
			var start = into.Count;
			if (Statement(statement, parameter, into))
				return true;
			if (into.Count > start)
				into.RemoveRange(start, into.Count - start);
			if (Errors.Count > errors)
				Errors.RemoveRange(errors, Errors.Count - errors);
			return false;
		}

		bool Nest(VmBuf buf, List<byte> nested)
		{
			if (nested.Count > ushort.MaxValue)
				return Fail(BadLambda, ActionLimit, "vm");
			buf.Op(RecordProgram.VmNest);
			buf.U16(nested.Count);
			buf.Bytes.AddRange(nested);
			return true;
		}

		bool VmIfInto(IfStatementSyntax branch, string parameter, VmBuf buf)
		{
			if (!WriteExpr(branch.Condition, buf, out var kind) || kind != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, branch.Condition);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			if (!WriteVmStatement(branch.Statement, parameter, buf))
				return false;
			if (branch.Else is null)
			{
				buf.Patch(skip);
				return true;
			}

			var end = buf.Reserve(RecordProgram.VmJump);
			buf.Patch(skip);
			if (!WriteVmStatement(branch.Else.Statement, parameter, buf))
				return false;
			buf.Patch(end);
			return true;
		}

		bool VmWhileInto(WhileStatementSyntax loop, string parameter, VmBuf buf)
		{
			var head = buf.Here;
			if (!WriteExpr(loop.Condition, buf, out var kind) || kind != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, loop.Condition);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			if (!WriteVmStatement(loop.Statement, parameter, buf))
				return false;
			buf.Jump(head);
			buf.Patch(skip);
			return true;
		}

		bool VmForInto(ForStatementSyntax loop, string parameter, VmBuf buf)
		{
			if (loop.Declaration is not null)
			{
				if (loop.Declaration.Variables.Count != 1)
					return Fail(BadLambda, ActionLimit, loop.Declaration);
				if (!VmLocalInto(loop.Declaration.Variables[0], buf))
					return false;
			}

			foreach (var initial in loop.Initializers)
			{
				if (!WriteStatementExpr(initial, parameter, buf))
					return false;
			}

			var head = buf.Here;
			var skip = -1;
			if (loop.Condition is not null)
			{
				if (!WriteExpr(loop.Condition, buf, out var kind) || kind != RecordProgram.VmKindBool)
					return Fail(BadLambda, ActionLimit, loop.Condition);
				skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			}

			if (!WriteVmStatement(loop.Statement, parameter, buf))
				return false;
			foreach (var step in loop.Incrementors)
			{
				if (!WriteStatementExpr(step, parameter, buf))
					return false;
			}

			buf.Jump(head);
			if (skip >= 0)
				buf.Patch(skip);
			return true;
		}

		bool VmForeachInto(ForEachStatementSyntax loop, string parameter, VmBuf buf)
		{
			if (loop.Expression is not MemberAccessExpressionSyntax member || !Member(member, out var list) || list.Kind != ValueKind.List)
				return Fail(BadLambda, ActionLimit, loop.Expression);
			if (!_records.TryGetValue(list.Item, out var fields))
				return Fail(BadLambda, ActionLimit, loop.Expression);
			if (_locals is null || _localSlots > 7)
				return Fail(BadLambda, ActionLimit, loop);
			var indexSlot = (byte)_localSlots++;
			var name = loop.Identifier.ValueText;
			_boundRows ??= new Dictionary<string, RowBind>(StringComparer.Ordinal);
			if (_boundRows.ContainsKey(name))
				return Fail(BadLambda, ActionLimit, loop);
			_boundRows[name] = new RowBind(list.Name, indexSlot, fields);
			buf.Op(RecordProgram.VmLitI);
			buf.I32(0);
			buf.Op(RecordProgram.VmStoreLocal);
			buf.Raw(RecordProgram.VmKindInt);
			buf.Raw(indexSlot);
			var head = buf.Here;
			buf.Op(RecordProgram.VmLoadLocal);
			buf.Raw(RecordProgram.VmKindInt);
			buf.Raw(indexSlot);
			buf.Op(RecordProgram.VmLoadCount);
			buf.U16(_code.Intern(list.Name));
			buf.Op(RecordProgram.VmLt);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			var wrote = WriteVmStatement(loop.Statement, parameter, buf);
			_boundRows.Remove(name);
			if (!wrote)
				return false;
			buf.Op(RecordProgram.VmIncLocal);
			buf.Raw(indexSlot);
			buf.Raw(RecordProgram.VmIncPost);
			buf.Op(RecordProgram.VmPop);
			buf.Jump(head);
			buf.Patch(skip);
			return true;
		}

		bool VmLocalInto(VariableDeclaratorSyntax variable, VmBuf buf)
		{
			if (variable.Initializer is null || _locals is null || _locals.ContainsKey(variable.Identifier.ValueText) || _localSlots > 7)
				return Fail(BadLambda, ActionLimit, variable);
			if (!WriteExpr(variable.Initializer.Value, buf, out var kind))
				return false;
			var slot = (byte)_localSlots++;
			buf.Op(RecordProgram.VmStoreLocal);
			buf.Raw(kind);
			buf.Raw(slot);
			_locals[variable.Identifier.ValueText] = (slot, KindOf(kind));
			return true;
		}

		bool WriteStatementExpr(ExpressionSyntax expression, string parameter, VmBuf buf)
		{
			_ = parameter;
			expression = Bare(expression);
			switch (expression)
			{
				case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
					return WriteAssign(assignment, buf);
				case AssignmentExpressionSyntax assignment when CompoundOp(assignment, out var op):
					return WriteCompound(assignment, op, buf);
				case PrefixUnaryExpressionSyntax prefix when IncMode(prefix, out var prefixMode):
					if (!WriteInc(prefix.Operand, prefixMode, buf))
						return false;
					buf.Op(RecordProgram.VmPop);
					return true;
				case PostfixUnaryExpressionSyntax postfix when IncMode(postfix, out var postfixMode):
					if (!WriteInc(postfix.Operand, postfixMode, buf))
						return false;
					buf.Op(RecordProgram.VmPop);
					return true;
				default:
					if (!WriteExpr(expression, buf, out _))
						return false;
					buf.Op(RecordProgram.VmPop);
					return true;
			}
		}

		bool WriteAssign(AssignmentExpressionSyntax assignment, VmBuf buf)
		{
			if (!WriteExpr(assignment.Right, buf, out var kind))
				return false;
			if (assignment.Left is MemberAccessExpressionSyntax member && Member(member, out var field))
				return StoreField(field, kind, buf, assignment.Left);
			if (assignment.Left is IdentifierNameSyntax local)
				return StoreLocal(local.Identifier.ValueText, kind, buf, assignment.Left);
			return Fail(BadLambda, ActionLimit, assignment);
		}

		bool WriteCompound(AssignmentExpressionSyntax assignment, byte op, VmBuf buf)
		{
			if (!LoadTarget(assignment.Left, buf, out var leftKind, assignment))
				return false;
			if (!WriteExpr(assignment.Right, buf, out var rightKind))
				return false;
			buf.Op(op);
			var kind = ResultKind(op, leftKind, rightKind);
			if (assignment.Left is MemberAccessExpressionSyntax member && Member(member, out var field))
				return StoreField(field, kind, buf, assignment.Left);
			if (assignment.Left is IdentifierNameSyntax local)
				return StoreLocal(local.Identifier.ValueText, kind, buf, assignment.Left);
			return Fail(BadLambda, ActionLimit, assignment);
		}

		bool LoadTarget(ExpressionSyntax target, VmBuf buf, out byte kind, SyntaxNode blame)
		{
			kind = 0;
			target = Bare(target);
			if (target is MemberAccessExpressionSyntax member && Member(member, out var field))
				return LoadField(field, buf, out kind, blame);
			if (target is IdentifierNameSyntax local && _locals is not null && _locals.TryGetValue(local.Identifier.ValueText, out var slot))
			{
				kind = VmKind(slot.Kind);
				buf.Op(RecordProgram.VmLoadLocal);
				buf.Raw(kind);
				buf.Raw(slot.Slot);
				return true;
			}

			return Fail(BadLambda, ActionLimit, blame);
		}

		bool LoadField(Field field, VmBuf buf, out byte kind, SyntaxNode blame)
		{
			kind = 0;
			if (field.Kind == ValueKind.Int)
			{
				kind = RecordProgram.VmKindInt;
				buf.Op(RecordProgram.VmLoadI);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.Bool)
			{
				kind = RecordProgram.VmKindBool;
				buf.Op(RecordProgram.VmLoadB);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.Str)
			{
				kind = RecordProgram.VmKindStr;
				buf.Op(RecordProgram.VmLoadS);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.List)
			{
				kind = RecordProgram.VmKindInt;
				buf.Op(RecordProgram.VmLoadCount);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			return Fail(BadLambda, ActionLimit, blame);
		}

		bool StoreField(Field field, byte kind, VmBuf buf, SyntaxNode blame)
		{
			if (field.Kind == ValueKind.Int && kind == RecordProgram.VmKindInt)
			{
				buf.Op(RecordProgram.VmStoreI);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.Bool && kind == RecordProgram.VmKindBool)
			{
				buf.Op(RecordProgram.VmStoreB);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			if (field.Kind == ValueKind.Str && kind == RecordProgram.VmKindStr)
			{
				buf.Op(RecordProgram.VmStoreS);
				buf.U16(_code.Intern(field.Name));
				return true;
			}

			return Fail(BadLambda, ActionLimit, blame);
		}

		bool StoreLocal(string name, byte kind, VmBuf buf, SyntaxNode blame)
		{
			if (_locals is null || !_locals.TryGetValue(name, out var local) || VmKind(local.Kind) != kind)
				return Fail(BadLambda, ActionLimit, blame);
			buf.Op(RecordProgram.VmStoreLocal);
			buf.Raw(kind);
			buf.Raw(local.Slot);
			return true;
		}

		bool WriteInc(ExpressionSyntax operand, byte mode, VmBuf buf)
		{
			operand = Bare(operand);
			if (operand is IdentifierNameSyntax local && Local(local.Identifier.ValueText, ValueKind.Int, out var slot))
			{
				buf.Op(RecordProgram.VmIncLocal);
				buf.Raw(slot);
				buf.Raw(mode);
				return true;
			}

			if (operand is MemberAccessExpressionSyntax member && Member(member, out var field) && field.Kind == ValueKind.Int)
			{
				buf.Op(RecordProgram.VmIncProp);
				buf.U16(_code.Intern(field.Name));
				buf.Raw(mode);
				return true;
			}

			return Fail(BadLambda, ActionLimit, operand);
		}

		bool WriteExpr(ExpressionSyntax expression, VmBuf buf, out byte kind)
		{
			kind = 0;
			expression = Bare(expression);
			switch (expression)
			{
				case LiteralExpressionSyntax literal:
					return WriteLiteral(literal, buf, out kind);
				case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression):
					if (!WriteExpr(prefix.Operand, buf, out kind) || kind is not (RecordProgram.VmKindInt or RecordProgram.VmKindDouble))
						return Fail(BadLambda, ActionLimit, prefix);
					buf.Op(RecordProgram.VmNeg);
					return true;
				case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression):
					if (!WriteExpr(prefix.Operand, buf, out kind) || kind != RecordProgram.VmKindBool)
						return Fail(BadLambda, ActionLimit, prefix);
					buf.Op(RecordProgram.VmNot);
					kind = RecordProgram.VmKindBool;
					return true;
				case PrefixUnaryExpressionSyntax prefix when IncMode(prefix, out var prefixMode):
					kind = RecordProgram.VmKindInt;
					return WriteInc(prefix.Operand, prefixMode, buf);
				case PostfixUnaryExpressionSyntax postfix when IncMode(postfix, out var postfixMode):
					kind = RecordProgram.VmKindInt;
					return WriteInc(postfix.Operand, postfixMode, buf);
				case CastExpressionSyntax cast:
					return WriteCast(cast, buf, out kind);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
					return WriteAnd(binary, buf, out kind);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression):
					return WriteOr(binary, buf, out kind);
				case BinaryExpressionSyntax binary when BinaryOp(binary, out var op):
					return WriteBinary(binary, op, buf, out kind);
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
					return WriteCoalesce(binary, buf, out kind);
				case ConditionalExpressionSyntax choice:
					return WriteChoice(choice, buf, out kind);
				case InterpolatedStringExpressionSyntax interpolated:
					return WriteInterpolation(interpolated, buf, out kind);
				case ConditionalAccessExpressionSyntax access:
					return WriteAccess(access, buf, out kind);
				case InvocationExpressionSyntax invocation:
					return WriteInvocation(invocation, buf, out kind);
				case IdentifierNameSyntax name:
					return WriteName(name, buf, out kind);
				case MemberAccessExpressionSyntax member:
					return WriteMember(member, buf, out kind);
				default:
					return Fail(BadLambda, ActionLimit, expression);
			}
		}

		bool WriteLiteral(LiteralExpressionSyntax literal, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int number)
			{
				kind = RecordProgram.VmKindInt;
				buf.Op(RecordProgram.VmLitI);
				buf.I32(number);
				return true;
			}

			if (literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is double real)
			{
				kind = RecordProgram.VmKindDouble;
				buf.Op(RecordProgram.VmLitD);
				buf.F64(real);
				return true;
			}

			if (literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				kind = RecordProgram.VmKindStr;
				buf.Op(RecordProgram.VmLitS);
				buf.U16(_code.Intern(literal.Token.ValueText));
				return true;
			}

			if (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression))
			{
				kind = RecordProgram.VmKindBool;
				buf.Op(RecordProgram.VmLitB);
				buf.Raw(literal.IsKind(SyntaxKind.TrueLiteralExpression) ? (byte)1 : (byte)0);
				return true;
			}

			if (literal.IsKind(SyntaxKind.NullLiteralExpression))
			{
				kind = RecordProgram.VmKindStr;
				buf.Op(RecordProgram.VmLitNull);
				return true;
			}

			return Fail(BadLambda, ActionLimit, literal);
		}

		bool WriteCast(CastExpressionSyntax cast, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (!WriteExpr(cast.Expression, buf, out _))
				return false;
			if (cast.Type is PredefinedTypeSyntax type && type.Keyword.IsKind(SyntaxKind.IntKeyword))
			{
				buf.Op(RecordProgram.VmToInt);
				kind = RecordProgram.VmKindInt;
				return true;
			}

			if (cast.Type is PredefinedTypeSyntax real && real.Keyword.IsKind(SyntaxKind.DoubleKeyword))
			{
				buf.Op(RecordProgram.VmToDouble);
				kind = RecordProgram.VmKindDouble;
				return true;
			}

			return Fail(BadLambda, ActionLimit, cast);
		}

		bool WriteAnd(BinaryExpressionSyntax binary, VmBuf buf, out byte kind)
		{
			kind = RecordProgram.VmKindBool;
			if (!WriteExpr(binary.Left, buf, out var left) || left != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, binary.Left);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			if (!WriteExpr(binary.Right, buf, out var right) || right != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, binary.Right);
			var end = buf.Reserve(RecordProgram.VmJump);
			buf.Patch(skip);
			buf.Op(RecordProgram.VmLitB);
			buf.Raw(0);
			buf.Patch(end);
			return true;
		}

		bool WriteOr(BinaryExpressionSyntax binary, VmBuf buf, out byte kind)
		{
			kind = RecordProgram.VmKindBool;
			if (!WriteExpr(binary.Left, buf, out var left) || left != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, binary.Left);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			buf.Op(RecordProgram.VmLitB);
			buf.Raw(1);
			var end = buf.Reserve(RecordProgram.VmJump);
			buf.Patch(skip);
			if (!WriteExpr(binary.Right, buf, out var right) || right != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, binary.Right);
			buf.Patch(end);
			return true;
		}

		bool WriteBinary(BinaryExpressionSyntax binary, byte op, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (!WriteExpr(binary.Left, buf, out var left) || !WriteExpr(binary.Right, buf, out var right))
				return false;
			buf.Op(op);
			kind = ResultKind(op, left, right);
			return true;
		}

		bool WriteCoalesce(BinaryExpressionSyntax binary, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (!WriteExpr(binary.Left, buf, out _))
				return false;
			var skip = buf.Reserve(RecordProgram.VmJumpIfNotNull);
			if (!WriteExpr(binary.Right, buf, out kind))
				return false;
			buf.Patch(skip);
			return true;
		}

		bool WriteChoice(ConditionalExpressionSyntax choice, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (!WriteExpr(choice.Condition, buf, out var cond) || cond != RecordProgram.VmKindBool)
				return Fail(BadLambda, ActionLimit, choice.Condition);
			var skip = buf.Reserve(RecordProgram.VmJumpIfFalse);
			if (!WriteExpr(choice.WhenTrue, buf, out var thenKind))
				return false;
			var end = buf.Reserve(RecordProgram.VmJump);
			buf.Patch(skip);
			if (!WriteExpr(choice.WhenFalse, buf, out var elseKind))
				return false;
			buf.Patch(end);
			if (thenKind != elseKind)
				return Fail(BadLambda, ActionLimit, choice);
			kind = thenKind;
			return true;
		}

		bool WriteInterpolation(InterpolatedStringExpressionSyntax interpolated, VmBuf buf, out byte kind)
		{
			kind = RecordProgram.VmKindStr;
			var any = false;
			foreach (var content in interpolated.Contents)
			{
				if (content is InterpolatedStringTextSyntax text)
				{
					buf.Op(RecordProgram.VmLitS);
					buf.U16(_code.Intern(text.TextToken.ValueText));
				}
				else if (content is InterpolationSyntax hole)
				{
					if (!WriteExpr(hole.Expression, buf, out _))
						return false;
					buf.Op(RecordProgram.VmText);
				}
				else
					return Fail(BadLambda, ActionLimit, interpolated);

				if (any)
					buf.Op(RecordProgram.VmAdd);
				any = true;
			}

			if (!any)
			{
				buf.Op(RecordProgram.VmLitS);
				buf.U16(_code.Intern(""));
			}

			return true;
		}

		bool WriteAccess(ConditionalAccessExpressionSyntax access, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (!WriteExpr(access.Expression, buf, out _))
				return false;
			buf.Op(RecordProgram.VmDup);
			var skip = buf.Reserve(RecordProgram.VmJumpIfNull);
			if (access.WhenNotNull is InvocationExpressionSyntax invocation && invocation.Expression is MemberBindingExpressionSyntax binding)
			{
				if (!WriteBoundCall(binding.Name.Identifier.ValueText, invocation.ArgumentList, buf, out kind))
					return false;
			}
			else if (access.WhenNotNull is MemberBindingExpressionSyntax length && length.Name.Identifier.ValueText == "Length")
			{
				buf.Op(RecordProgram.VmCall);
				buf.U16(RecordProgram.VmLength);
				buf.Raw(1);
				kind = RecordProgram.VmKindInt;
			}
			else
				return Fail(BadLambda, ActionLimit, access);

			buf.Patch(skip);
			return true;
		}

		bool WriteInvocation(InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (invocation.Expression is MemberAccessExpressionSyntax member)
			{
				if (member.Expression is IdentifierNameSyntax owner && owner.Identifier.ValueText == "Math")
					return WriteMath(member.Name.Identifier.ValueText, invocation, buf, out kind);
				if (member.Expression is PredefinedTypeSyntax type && type.Keyword.IsKind(SyntaxKind.IntKeyword))
					return WriteIntCall(member.Name.Identifier.ValueText, invocation, buf, out kind);
				if (member.Expression is MemberAccessExpressionSyntax listTarget
					&& listTarget.Expression is IdentifierNameSyntax listOwner
					&& listOwner.Identifier.ValueText == _state
					&& ListField(listTarget.Name.Identifier.ValueText, out var list))
					return WriteListCall(list, member.Name.Identifier.ValueText, invocation, buf, out kind);
				if (!WriteExpr(member.Expression, buf, out var receiver) || receiver != RecordProgram.VmKindStr)
					return Fail(BadLambda, ActionLimit, member.Expression);
				return WriteBoundCall(member.Name.Identifier.ValueText, invocation.ArgumentList, buf, out kind);
			}

			return Fail(BadLambda, ActionLimit, invocation);
		}

		bool WriteBoundCall(string name, ArgumentListSyntax arguments, VmBuf buf, out byte kind)
		{
			kind = 0;
			var id = name switch
			{
				"Trim" => RecordProgram.VmTrim,
				"ToUpper" => RecordProgram.VmUpper,
				"ToLower" => RecordProgram.VmLower,
				"Contains" => RecordProgram.VmContains,
				"StartsWith" => RecordProgram.VmStarts,
				"Substring" => RecordProgram.VmSubstring,
				_ => (ushort)0,
			};
			if (id == 0)
				return Fail(BadLambda, ActionLimit, arguments);
			foreach (var argument in arguments.Arguments)
			{
				if (!WriteExpr(argument.Expression, buf, out _))
					return false;
			}

			var argc = arguments.Arguments.Count + 1;
			if (argc > 255)
				return Fail(BadLambda, ActionLimit, arguments);
			buf.Op(RecordProgram.VmCall);
			buf.U16(id);
			buf.Raw((byte)argc);
			kind = id is RecordProgram.VmContains or RecordProgram.VmStarts ? RecordProgram.VmKindBool : RecordProgram.VmKindStr;
			if (id == RecordProgram.VmSubstring)
				kind = RecordProgram.VmKindStr;
			return true;
		}

		bool WriteMath(string name, InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = 0;
			foreach (var argument in invocation.ArgumentList.Arguments)
			{
				if (!WriteExpr(argument.Expression, buf, out _))
					return false;
			}

			var argc = invocation.ArgumentList.Arguments.Count;
			var id = name switch
			{
				"Min" when argc == 2 => RecordProgram.VmMin,
				"Max" when argc == 2 => RecordProgram.VmMax,
				"Abs" when argc == 1 => RecordProgram.VmAbs,
				"Round" when argc == 1 => RecordProgram.VmRound,
				_ => (ushort)0,
			};
			if (id == 0)
				return Fail(BadLambda, ActionLimit, invocation);
			buf.Op(RecordProgram.VmCall);
			buf.U16(id);
			buf.Raw((byte)argc);
			kind = id == RecordProgram.VmRound ? RecordProgram.VmKindDouble : RecordProgram.VmKindInt;
			if (id is RecordProgram.VmMin or RecordProgram.VmMax && invocation.ArgumentList.Arguments.Any(argument => argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is double))
				kind = RecordProgram.VmKindDouble;
			return true;
		}

		bool WriteIntCall(string name, InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = 0;
			var args = invocation.ArgumentList.Arguments;
			if (name == "Parse" && args.Count == 1)
			{
				if (!WriteExpr(args[0].Expression, buf, out _))
					return false;
				buf.Op(RecordProgram.VmCall);
				buf.U16(RecordProgram.VmParse);
				buf.Raw(1);
				kind = RecordProgram.VmKindInt;
				return true;
			}

			if (name == "TryParse" && args.Count == 2 && args[1].RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
				return WriteTryParse(args[0].Expression, args[1].Expression, buf, out kind);
			return Fail(BadLambda, ActionLimit, invocation);
		}

		bool WriteTryParse(ExpressionSyntax text, ExpressionSyntax destination, VmBuf buf, out byte kind)
		{
			kind = RecordProgram.VmKindBool;
			string? name = destination switch
			{
				DeclarationExpressionSyntax declaration when declaration.Designation is SingleVariableDesignationSyntax single => single.Identifier.ValueText,
				IdentifierNameSyntax existing => existing.Identifier.ValueText,
				_ => null,
			};
			if (name is null || _locals is null)
				return Fail(BadLambda, ActionLimit, destination);
			byte slot;
			if (_locals.TryGetValue(name, out var local))
			{
				if (local.Kind != ValueKind.Int)
					return Fail(BadLambda, ActionLimit, destination);
				slot = local.Slot;
			}
			else
			{
				if (_localSlots > 7)
					return Fail(BadLambda, ActionLimit, destination);
				slot = (byte)_localSlots++;
				_locals[name] = (slot, ValueKind.Int);
			}

			if (!WriteExpr(text, buf, out _))
				return false;
			buf.Op(RecordProgram.VmTryParse);
			buf.Raw(slot);
			return true;
		}

		bool WriteListCall(Field list, string method, InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = 0;
			var args = invocation.ArgumentList.Arguments;
			if (args.Count != 1 || args[0].Expression is not IdentifierNameSyntax name || _boundRows is null || !_boundRows.TryGetValue(name.Identifier.ValueText, out var row) || row.List != list.Name)
				return Fail(BadLambda, ActionLimit, invocation);
			var op = method switch
			{
				"Remove" => RecordProgram.VmRowRemove,
				"Contains" => RecordProgram.VmRowContains,
				"IndexOf" => RecordProgram.VmRowIndex,
				_ => (byte)0,
			};
			if (op == 0)
				return Fail(BadLambda, ActionLimit, invocation);
			buf.Op(op);
			buf.U16(_code.Intern(list.Name));
			buf.Raw(row.IndexSlot);
			kind = method == "IndexOf" ? RecordProgram.VmKindInt : RecordProgram.VmKindBool;
			return true;
		}

		bool WriteName(IdentifierNameSyntax name, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (_locals is not null && _locals.TryGetValue(name.Identifier.ValueText, out var local))
			{
				kind = VmKind(local.Kind);
				buf.Op(RecordProgram.VmLoadLocal);
				buf.Raw(kind);
				buf.Raw(local.Slot);
				return true;
			}

			return Fail(BadLambda, ActionLimit, name);
		}

		bool WriteMember(MemberAccessExpressionSyntax member, VmBuf buf, out byte kind)
		{
			kind = 0;
			if (member.Expression is IdentifierNameSyntax row && _boundRows is not null && _boundRows.TryGetValue(row.Identifier.ValueText, out var bind))
			{
				var index = bind.Fields.FindIndex(field => field.Name == member.Name.Identifier.ValueText);
				if (index < 0)
					return Fail(BadLambda, ActionLimit, member);
				buf.Op(RecordProgram.VmLoadCell);
				buf.U16(_code.Intern(bind.List));
				buf.Raw(bind.IndexSlot);
				buf.U16(index);
				if (bind.Fields[index].Kind == ValueKind.Bool)
				{
					buf.Op(RecordProgram.VmLitS);
					buf.U16(_code.Intern("true"));
					buf.Op(RecordProgram.VmEq);
					kind = RecordProgram.VmKindBool;
					return true;
				}

				kind = RecordProgram.VmKindStr;
				return true;
			}

			if (member.Name.Identifier.ValueText == "Count"
				&& member.Expression is MemberAccessExpressionSyntax countOf
				&& Member(countOf, out var list)
				&& list.Kind == ValueKind.List)
			{
				kind = RecordProgram.VmKindInt;
				buf.Op(RecordProgram.VmLoadCount);
				buf.U16(_code.Intern(list.Name));
				return true;
			}

			if (member.Name.Identifier.ValueText == "Length")
			{
				if (!WriteExpr(member.Expression, buf, out var receiver) || receiver != RecordProgram.VmKindStr)
					return Fail(BadLambda, ActionLimit, member);
				buf.Op(RecordProgram.VmCall);
				buf.U16(RecordProgram.VmLength);
				buf.Raw(1);
				kind = RecordProgram.VmKindInt;
				return true;
			}

			if (Member(member, out var field))
				return LoadField(field, buf, out kind, member);
			return Fail(BadLambda, ActionLimit, member);
		}

		static bool CompoundOp(AssignmentExpressionSyntax assignment, out byte op)
		{
			op = assignment.Kind() switch
			{
				SyntaxKind.AddAssignmentExpression => RecordProgram.VmAdd,
				SyntaxKind.SubtractAssignmentExpression => RecordProgram.VmSub,
				SyntaxKind.MultiplyAssignmentExpression => RecordProgram.VmMul,
				SyntaxKind.DivideAssignmentExpression => RecordProgram.VmDiv,
				SyntaxKind.ModuloAssignmentExpression => RecordProgram.VmRem,
				_ => 0,
			};
			return op != 0;
		}

		static bool BinaryOp(BinaryExpressionSyntax binary, out byte op)
		{
			op = binary.Kind() switch
			{
				SyntaxKind.AddExpression => RecordProgram.VmAdd,
				SyntaxKind.SubtractExpression => RecordProgram.VmSub,
				SyntaxKind.MultiplyExpression => RecordProgram.VmMul,
				SyntaxKind.DivideExpression => RecordProgram.VmDiv,
				SyntaxKind.ModuloExpression => RecordProgram.VmRem,
				SyntaxKind.EqualsExpression => RecordProgram.VmEq,
				SyntaxKind.NotEqualsExpression => RecordProgram.VmNe,
				SyntaxKind.LessThanExpression => RecordProgram.VmLt,
				SyntaxKind.LessThanOrEqualExpression => RecordProgram.VmLe,
				SyntaxKind.GreaterThanExpression => RecordProgram.VmGt,
				SyntaxKind.GreaterThanOrEqualExpression => RecordProgram.VmGe,
				_ => 0,
			};
			return op != 0;
		}

		static bool IncMode(SyntaxNode node, out byte mode)
		{
			mode = node.Kind() switch
			{
				SyntaxKind.PreIncrementExpression => RecordProgram.VmIncPre,
				SyntaxKind.PostIncrementExpression => RecordProgram.VmIncPost,
				SyntaxKind.PreDecrementExpression => RecordProgram.VmDecPre,
				SyntaxKind.PostDecrementExpression => RecordProgram.VmDecPost,
				_ => 0,
			};
			return mode != 0;
		}

		static byte ResultKind(byte op, byte left, byte right)
		{
			if (op is RecordProgram.VmEq or RecordProgram.VmNe or RecordProgram.VmLt or RecordProgram.VmLe or RecordProgram.VmGt or RecordProgram.VmGe)
				return RecordProgram.VmKindBool;
			if (op == RecordProgram.VmAdd && (left == RecordProgram.VmKindStr || right == RecordProgram.VmKindStr))
				return RecordProgram.VmKindStr;
			if (left == RecordProgram.VmKindDouble || right == RecordProgram.VmKindDouble)
				return RecordProgram.VmKindDouble;
			return RecordProgram.VmKindInt;
		}

		static byte VmKind(ValueKind kind) => kind switch
		{
			ValueKind.Int => RecordProgram.VmKindInt,
			ValueKind.Double => RecordProgram.VmKindDouble,
			ValueKind.Bool => RecordProgram.VmKindBool,
			ValueKind.Str => RecordProgram.VmKindStr,
			_ => 0,
		};

		static ValueKind KindOf(byte kind) => kind switch
		{
			RecordProgram.VmKindInt => ValueKind.Int,
			RecordProgram.VmKindDouble => ValueKind.Double,
			RecordProgram.VmKindBool => ValueKind.Bool,
			_ => ValueKind.Str,
		};
	}
}
