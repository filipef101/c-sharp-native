using System.Threading;

namespace NativePoc;

internal sealed class ActionStoppedException : InvalidOperationException
{
	public ActionStoppedException(string message)
		: base(message)
	{
	}
}

public static class ActionLog
{
	// xUnit runs test classes in parallel. A process-global last result made
	// Recursion_stops_at_the_call_depth read another test's Note. The callbacks
	// stay process-global: the activity sets them once on the UI thread, and a
	// later Java callback does not keep an AsyncLocal from OnCreate.
	static readonly AsyncLocal<Slot> _slot = new();

	sealed class Slot
	{
		public long Micros;
		public int Steps;
		public string? Stop;
	}

	static Slot Current => _slot.Value ??= new Slot();

	public static long LastMicros => _slot.Value?.Micros ?? 0;

	public static int LastSteps => _slot.Value?.Steps ?? 0;

	public static string? LastStop => _slot.Value?.Stop;

	public static Action<long, int, string?>? OnScript { get; set; }

	public static Action<string, string>? OnFault { get; set; }

	public static void Note(long micros, int steps, string? stop)
	{
		var slot = Current;
		slot.Micros = micros;
		slot.Steps = steps;
		slot.Stop = stop;
		OnScript?.Invoke(micros, steps, stop);
	}
}
