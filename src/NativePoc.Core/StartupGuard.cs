namespace NativePoc;

internal static class StartupGuard
{
	public const string DefaultId = "counter";

	public static bool Rejected(Exception ex) =>
		ex is InvalidOperationException && ex.Message.StartsWith("Rejected ", StringComparison.Ordinal);

	public static string RenderLog(string screen, string reason) =>
		"startup render rejected screen=" + screen + " reason=" + reason + " fallback=" + DefaultId;

	public static string RootLog(string id) =>
		"startup root unknown id=" + id + " fallback=" + DefaultId;

	public static string StateReason(string message) =>
		message.Contains("version", StringComparison.Ordinal) ? "old-version" : "corrupt";

	public static string StateLog(string reason) =>
		"state-load rejected reason=" + reason;

	public static bool Prepare(ScreenNav nav, Action<byte[], RecordProgram.Bag> prepare, out string screen, out string reason)
	{
		ArgumentNullException.ThrowIfNull(nav);
		ArgumentNullException.ThrowIfNull(prepare);
		try
		{
			nav.Prepare(prepare);
			screen = "";
			reason = "";
			return true;
		}
		catch (Exception ex) when (Rejected(ex))
		{
			screen = nav.TopId();
			if (screen.Length == 0)
				screen = DefaultId;
			reason = ex.Message;
			while (nav.DropTop())
			{
			}

			return false;
		}
	}
}

internal static class InstanceUi
{
	public const string CountKey = "nativepoc.count";

	public const string NameKey = "nativepoc.name";

	public const string FlagsKey = "nativepoc.flags";

	public const string NavKey = "nativepoc.nav";

	// The activity Bundle is one Binder parcel. The practical budget for that
	// parcel is about 1 MB, shared with everything else the system saves.
	// This snapshot is the screen stack and bag fields, not program bytes,
	// and it stops at 64 KiB. A larger snapshot is omitted. Count, name, and
	// flags are still saved, and the next launch shows the launch screen.
	public const int NavLimit = 65536;

	public const int Greeting = 1;

	public const int Plain = 2;

	public static int Flags(AppState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		return (state.GreetingFirst ? Greeting : 0) | (state.PlainRoot ? Plain : 0);
	}

	public static void Apply(AppState state, int count, string? name, int flags)
	{
		ArgumentNullException.ThrowIfNull(state);
		state.Count = count;
		state.Name = name ?? "";
		state.GreetingFirst = (flags & Greeting) != 0;
		state.PlainRoot = (flags & Plain) != 0;
	}

	public static byte[]? FitNav(NavCheckpoint nav, out int bytes) =>
		FitNav(nav, NavLimit, out bytes);

	public static byte[]? FitNav(NavCheckpoint nav, int limit, out int bytes)
	{
		ArgumentNullException.ThrowIfNull(nav);
		if (limit < 1)
			throw new ArgumentOutOfRangeException(nameof(limit));
		byte[] raw;
		try
		{
			raw = NavCheckpoint.WriteKept(nav);
		}
		catch (InvalidOperationException)
		{
			bytes = 0;
			return null;
		}

		bytes = raw.Length;
		return raw.Length <= limit ? raw : null;
	}
}
