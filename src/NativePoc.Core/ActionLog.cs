namespace NativePoc;

public sealed class ActionStoppedException : InvalidOperationException
{
	public ActionStoppedException(string message)
		: base(message)
	{
	}
}

public static class ActionLog
{
	public static long LastMicros { get; private set; }

	public static int LastSteps { get; private set; }

	public static string? LastStop { get; private set; }

	public static Action<long, int, string?>? OnScript { get; set; }

	public static void Note(long micros, int steps, string? stop)
	{
		LastMicros = micros;
		LastSteps = steps;
		LastStop = stop;
		OnScript?.Invoke(micros, steps, stop);
	}
}
