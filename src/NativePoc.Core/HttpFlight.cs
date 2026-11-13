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

	public static void Attach(string id, int generation)
	{
		lock (Gate)
			AttachCore(id, generation);
	}

	public static bool Cancel(string id)
	{
		lock (Gate)
			return CancelCore(id);
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
