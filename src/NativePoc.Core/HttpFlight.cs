namespace NativePoc;

// One slot per screen. Cancel marks every generation opened so far and leaves
// a later Open on that screen, and every other screen, running.
internal static class HttpFlight
{
	internal static readonly object Gate = new();

	sealed class Slot
	{
		public int Generation;

		public int CancelledThrough;

		public int Connections;

		public List<Action>? Aborts;
	}

	static readonly Dictionary<string, Slot> Slots = new(StringComparer.Ordinal);

	public static int Open(string id)
	{
		lock (Gate)
		{
			var slot = SlotFor(id);
			slot.Generation++;
			return slot.Generation;
		}
	}

	// The image read shares the screen's current generation. Open would start
	// a generation Pop had not cancelled yet.
	public static int Join(string id)
	{
		lock (Gate)
		{
			var slot = SlotFor(id);
			if (slot.Generation == 0)
				slot.Generation = 1;
			return slot.Generation;
		}
	}

	public static void Attach(string id, int generation)
	{
		lock (Gate)
			AttachCore(id, generation);
	}

	public static bool Cancel(string id)
	{
		Action[] pending;
		bool cancelled;
		lock (Gate)
		{
			cancelled = CancelCore(id);
			pending = cancelled && Slots.TryGetValue(id, out var slot) ? TakeAborts(slot) : Array.Empty<Action>();
		}

		RunAborts(pending);
		return cancelled;
	}

	// The managed image read registers the dispose of its response. Cancel
	// runs that dispose off the lock so the socket unblocks. False means the
	// generation is already cancelled and the caller disposes immediately.
	public static bool BindAbort(string id, int generation, Action abort)
	{
		lock (Gate)
		{
			if (CancelledCore(id, generation))
				return false;
			var slot = SlotFor(id);
			slot.Aborts ??= new List<Action>();
			slot.Aborts.Add(abort);
			return true;
		}
	}

	public static void ClearAbort(string id, Action abort)
	{
		lock (Gate)
		{
			if (Slots.TryGetValue(id, out var slot) && slot.Aborts is not null)
				slot.Aborts.Remove(abort);
		}
	}

	public static bool Live(string id)
	{
		lock (Gate)
		{
			return Slots.TryGetValue(id, out var slot)
				&& slot.Generation > slot.CancelledThrough
				&& slot.Connections > 0;
		}
	}

	public static bool WasCancelled(string id, int generation)
	{
		lock (Gate)
			return CancelledCore(id, generation);
	}

	internal static void AttachCore(string id, int generation)
	{
		if (!Slots.TryGetValue(id, out var slot) || generation <= slot.CancelledThrough)
			return;
		slot.Connections++;
	}

	internal static bool CancelCore(string id)
	{
		if (!Slots.TryGetValue(id, out var slot) || slot.CancelledThrough >= slot.Generation)
			return false;
		slot.CancelledThrough = slot.Generation;
		slot.Connections = 0;
		return true;
	}

	internal static bool CancelledCore(string id, int generation) =>
		Slots.TryGetValue(id, out var slot) && generation > 0 && generation <= slot.CancelledThrough;

	static Action[] TakeAborts(Slot slot)
	{
		if (slot.Aborts is null || slot.Aborts.Count == 0)
			return Array.Empty<Action>();
		var copy = slot.Aborts.ToArray();
		slot.Aborts.Clear();
		return copy;
	}

	static void RunAborts(Action[] pending)
	{
		foreach (var abort in pending)
		{
			try
			{
				abort();
			}
			catch (Exception)
			{
			}
		}
	}

	static Slot SlotFor(string id)
	{
		if (!Slots.TryGetValue(id, out var slot))
		{
			slot = new Slot();
			Slots[id] = slot;
		}

		return slot;
	}
}
