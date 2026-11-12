namespace NativePoc;

internal static class JniRegistry
{
	static readonly Dictionary<string, Action<string[]>> Calls = new(StringComparer.Ordinal);

	public static void Add(string name, Action<string[]> call)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(call);
		Calls[name] = call;
	}

	public static void Call(string name, string[] args)
	{
		if (!Calls.TryGetValue(name, out var call))
			throw new InvalidOperationException("jni-bind miss " + name);
		call(args);
	}
}
