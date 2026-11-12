namespace NativePoc;

public static partial class RecordProgram
{
	internal sealed partial class HandlerFrame
	{
		readonly TryGuard[] _act = new TryGuard[8];
		readonly TryGuard[] _vm = new TryGuard[8];
		int _actCount;
		int _vmCount;

		internal int ActCount => _actCount;

		internal int VmEnd(int depth)
		{
			if (_vmCount == 0 || _vm[_vmCount - 1].Depth != depth)
				throw new InvalidOperationException("Rejected record program.");
			return _vm[_vmCount - 1].EndIp;
		}

		internal void EnterAct(byte[] script, int catchIp, int finallyIp, int endIp, int type)
		{
			if (_actCount >= _act.Length)
				throw new InvalidOperationException("Rejected record program.");
			_act[_actCount++] = new TryGuard
			{
				Script = script,
				CatchIp = catchIp,
				FinallyIp = finallyIp,
				EndIp = endIp,
				Type = type,
			};
		}

		internal void EnterVm(int catchIp, int finallyIp, int endIp, int type, int savedSp, int depth)
		{
			if (_vmCount >= _vm.Length)
				throw new InvalidOperationException("Rejected record program.");
			_vm[_vmCount++] = new TryGuard
			{
				CatchIp = catchIp,
				FinallyIp = finallyIp,
				EndIp = endIp,
				Type = type,
				SavedSp = savedSp,
				Depth = depth,
			};
		}

		internal bool HasOuterAct(byte[] script)
		{
			for (var index = 0; index < _actCount; index++)
			{
				if (!ReferenceEquals(_act[index].Script, script))
					return true;
			}

			return false;
		}

		internal bool DispatchAct(byte[] script, string[] strings, Exception ex, ref int ip)
		{
			while (_actCount > 0)
			{
				var guard = _act[_actCount - 1];
				if (!ReferenceEquals(guard.Script, script))
					return false;
				if (guard.Phase == 0 && guard.CatchIp >= 0 && Matches(ex, guard.Type, strings))
				{
					guard.Phase = 1;
					guard.Pending = null;
					ip = guard.CatchIp;
					return true;
				}

				if (guard.Phase != 2 && guard.FinallyIp >= 0)
				{
					guard.Phase = 2;
					guard.Pending = ex;
					guard.LeaveIp = -1;
					ip = guard.FinallyIp;
					return true;
				}

				_actCount--;
			}

			return false;
		}

		internal void LeaveAct(byte[] script, ref int ip)
		{
			if (_actCount == 0 || !ReferenceEquals(_act[_actCount - 1].Script, script))
				throw new InvalidOperationException("Rejected record program.");
			LeaveAct(script, _act[_actCount - 1].EndIp, ref ip);
		}

		internal void EndActFinally(byte[] script, string[] strings, ref int ip)
		{
			if (_actCount == 0 || !ReferenceEquals(_act[_actCount - 1].Script, script))
				throw new InvalidOperationException("Rejected record program.");
			var guard = _act[--_actCount];
			if (guard.Pending is not null)
			{
				if (!DispatchAct(script, strings, guard.Pending, ref ip))
				{
					if (HasOuterAct(script))
						throw guard.Pending;
					throw new ActionStoppedException(guard.Pending.Message);
				}

				return;
			}

			LeaveAct(script, guard.LeaveIp, ref ip);
		}

		void LeaveAct(byte[] script, int target, ref int ip)
		{
			while (_actCount > 0 && ReferenceEquals(_act[_actCount - 1].Script, script))
			{
				var guard = _act[_actCount - 1];
				if (guard.FinallyIp >= 0 && guard.Phase != 2)
				{
					guard.Phase = 2;
					guard.Pending = null;
					guard.LeaveIp = target;
					ip = guard.FinallyIp;
					return;
				}

				_actCount--;
			}

			ip = target;
		}

		internal bool DispatchVm(string[] strings, Exception ex, ref int ip, ref int sp, int depth)
		{
			while (_vmCount > 0 && _vm[_vmCount - 1].Depth == depth)
			{
				var guard = _vm[_vmCount - 1];
				if (guard.Phase == 0 && guard.CatchIp >= 0 && Matches(ex, guard.Type, strings))
				{
					guard.Phase = 1;
					guard.Pending = null;
					sp = guard.SavedSp;
					ip = guard.CatchIp;
					return true;
				}

				if (guard.Phase != 2 && guard.FinallyIp >= 0)
				{
					guard.Phase = 2;
					guard.Pending = ex;
					guard.LeaveIp = -1;
					sp = guard.SavedSp;
					ip = guard.FinallyIp;
					return true;
				}

				_vmCount--;
			}

			return false;
		}

		internal void LeaveVm(ref int ip, int target, int depth)
		{
			while (_vmCount > 0 && _vm[_vmCount - 1].Depth == depth)
			{
				var guard = _vm[_vmCount - 1];
				if (guard.FinallyIp >= 0 && guard.Phase != 2)
				{
					guard.Phase = 2;
					guard.Pending = null;
					guard.LeaveIp = target;
					ip = guard.FinallyIp;
					return;
				}

				_vmCount--;
			}

			ip = target;
		}

		internal void EndVmFinally(string[] strings, ref int ip, ref int sp, int depth)
		{
			if (_vmCount == 0 || _vm[_vmCount - 1].Depth != depth)
				throw new InvalidOperationException("Rejected record program.");
			var guard = _vm[--_vmCount];
			if (guard.Pending is not null)
			{
				if (!DispatchVm(strings, guard.Pending, ref ip, ref sp, depth))
					throw guard.Pending;
				return;
			}

			LeaveVm(ref ip, guard.LeaveIp, depth);
		}
	}

	sealed class TryGuard
	{
		public byte[]? Script;
		public int CatchIp = -1;
		public int FinallyIp = -1;
		public int EndIp;
		public int Type = -1;
		public int LeaveIp;
		public int Phase;
		public int SavedSp;
		public int Depth;
		public Exception? Pending;
	}

	static bool IsUserFault(Exception ex)
	{
		if (ex is ActionStoppedException or ActionSuspendException)
			return false;
		if (ex is InvalidOperationException && ex.Message.StartsWith("Rejected", StringComparison.Ordinal))
			return false;
		return true;
	}

	static bool Matches(Exception ex, int type, string[] strings)
	{
		if (type < 0)
			return type == -1;
		var want = strings[type];
		for (var current = ex.GetType(); current is not null && current != typeof(object); current = current.BaseType)
		{
			if (current.Name == want || current.FullName == want)
				return true;
		}

		return false;
	}
}
