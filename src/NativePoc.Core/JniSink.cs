namespace NativePoc;

internal static class JniSink
{
	public static Action<string, string, string>? Deliver { get; set; }

	public static void Post(string screen, string field, string value)
	{
		var deliver = Deliver;
		if (deliver is null)
			return;
		deliver(screen, field, value ?? "");
	}
}
