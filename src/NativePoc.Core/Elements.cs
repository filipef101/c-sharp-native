namespace NativePoc;

public enum ElementKind
{
	Column = 1,
	Text = 2,
	Button = 3,
	TextField = 4,
	Switch = 5,
	Scroll = 6,
	List = 7,
	Image = 8,
	Host = 9,
}

public enum Align
{
	Start = 0,
	Center = 1,
	End = 2,
	Stretch = 3,
}

public enum FontWeight
{
	Regular = 0,
	Bold = 1,
}

public enum Keyboard
{
	Text = 0,
	Email = 1,
	Number = 2,
	Phone = 3,
	Password = 4,
}

public enum Submit
{
	None = 0,
	Next = 1,
	Send = 2,
	Go = 3,
	Search = 4,
}

public readonly record struct Style
{
	public int Padding { get; init; }

	public int Margin { get; init; }

	public int Gap { get; init; }

	public int Grow { get; init; }

	public Align Align { get; init; }

	public int FontSize { get; init; }

	public FontWeight Weight { get; init; }

	public int Radius { get; init; }

	public uint Color { get; init; }

	public uint Background { get; init; }
}

public static class StyleCodec
{
	public const int Bytes = 22;

	public static void Write(Style style, Span<byte> dest)
	{
		if (dest.Length < Bytes)
			throw new ArgumentException("Style encoding is 22 bytes.", nameof(dest));
		WriteI16(dest, 0, style.Padding);
		WriteI16(dest, 2, style.Margin);
		WriteI16(dest, 4, style.Gap);
		WriteI16(dest, 6, style.Grow);
		WriteI16(dest, 8, style.FontSize);
		WriteI16(dest, 10, style.Radius);
		dest[12] = (byte)style.Align;
		dest[13] = (byte)style.Weight;
		WriteU32(dest, 14, style.Color);
		WriteU32(dest, 18, style.Background);
	}

	public static Style Read(ReadOnlySpan<byte> src)
	{
		if (src.Length < Bytes)
			throw new InvalidOperationException("Rejected record program.");
		return new Style
		{
			Padding = ReadI16(src, 0),
			Margin = ReadI16(src, 2),
			Gap = ReadI16(src, 4),
			Grow = ReadI16(src, 6),
			FontSize = ReadI16(src, 8),
			Radius = ReadI16(src, 10),
			Align = (Align)src[12],
			Weight = (FontWeight)src[13],
			Color = ReadU32(src, 14),
			Background = ReadU32(src, 18),
		};
	}

	static void WriteI16(Span<byte> dest, int at, int value)
	{
		dest[at] = (byte)value;
		dest[at + 1] = (byte)(value >> 8);
	}

	static void WriteU32(Span<byte> dest, int at, uint value)
	{
		dest[at] = (byte)value;
		dest[at + 1] = (byte)(value >> 8);
		dest[at + 2] = (byte)(value >> 16);
		dest[at + 3] = (byte)(value >> 24);
	}

	static int ReadI16(ReadOnlySpan<byte> src, int at)
	{
		var value = src[at] | (src[at + 1] << 8);
		return (short)value;
	}

	static uint ReadU32(ReadOnlySpan<byte> src, int at) =>
		(uint)(src[at] | (src[at + 1] << 8) | (src[at + 2] << 16) | (src[at + 3] << 24));
}

public readonly record struct Motion
{
	public int FromOpacity { get; init; }

	public int FromX { get; init; }

	public int FromY { get; init; }

	public int FromScale { get; init; }

	public int Opacity { get; init; }

	public int X { get; init; }

	public int Y { get; init; }

	public int Scale { get; init; }

	public int Duration { get; init; }

	public static Motion Rest { get; } = new()
	{
		FromOpacity = 100,
		FromScale = 100,
		Opacity = 100,
		Scale = 100,
	};
}

public static class MotionCodec
{
	public const int Bytes = 18;

	public static bool IsRest(Motion motion) => motion == Motion.Rest;

	public static void Write(Motion motion, Span<byte> dest)
	{
		if (dest.Length < Bytes)
			throw new ArgumentException("Motion encoding is 18 bytes.", nameof(dest));
		WriteI16(dest, 0, motion.FromOpacity);
		WriteI16(dest, 2, motion.FromX);
		WriteI16(dest, 4, motion.FromY);
		WriteI16(dest, 6, motion.FromScale);
		WriteI16(dest, 8, motion.Opacity);
		WriteI16(dest, 10, motion.X);
		WriteI16(dest, 12, motion.Y);
		WriteI16(dest, 14, motion.Scale);
		WriteI16(dest, 16, motion.Duration);
	}

	public static Motion Read(ReadOnlySpan<byte> src)
	{
		if (src.Length < Bytes)
			throw new InvalidOperationException("Rejected record program.");
		return new Motion
		{
			FromOpacity = ReadI16(src, 0),
			FromX = ReadI16(src, 2),
			FromY = ReadI16(src, 4),
			FromScale = ReadI16(src, 6),
			Opacity = ReadI16(src, 8),
			X = ReadI16(src, 10),
			Y = ReadI16(src, 12),
			Scale = ReadI16(src, 14),
			Duration = ReadI16(src, 16),
		};
	}

	static void WriteI16(Span<byte> dest, int at, int value)
	{
		dest[at] = (byte)value;
		dest[at + 1] = (byte)(value >> 8);
	}

	static int ReadI16(ReadOnlySpan<byte> src, int at)
	{
		var value = src[at] | (src[at + 1] << 8);
		return (short)value;
	}
}

public static class MotionMath
{
	public static int Lerp(int from, int to, int elapsed, int duration)
	{
		if (duration <= 0 || elapsed >= duration)
			return to;
		if (elapsed <= 0)
			return from;
		return from + (int)((long)(to - from) * elapsed / duration);
	}
}

public static class ThemeTokens
{
	public static bool ResolveDark(bool systemDark, string mode) =>
		mode switch
		{
			"dark" => true,
			"light" => false,
			"system" => systemDark,
			"true" => true,
			"false" => false,
			_ => throw new InvalidOperationException("Unknown theme mode."),
		};

	public static string Persist(string mode) =>
		mode switch
		{
			"true" => "dark",
			"false" => "light",
			"dark" or "light" or "system" => mode,
			_ => throw new InvalidOperationException("Unknown theme mode."),
		};

	public static uint Text(bool dark) => dark ? 0xFFF4F1EAu : 0xFF1C1917u;

	public static uint Page(bool dark) => dark ? 0xFF1C1917u : 0xFFF4F1EAu;

	public static uint Accent(bool dark) => dark ? 0xFFE7C27Du : 0xFF8A5A2Bu;
}

public static class Gestures
{
	public const byte Tap = 1;

	public const byte LongPress = 2;

	public const byte Swipe = 4;

	public const byte Refresh = 8;
}

public abstract record Element
{
	public string? Key { get; init; }

	public Style? Style { get; init; }

	public Motion? Motion { get; init; }

	public byte Gesture { get; init; }

	public Action? OnTap { get; init; }

	public Action? OnLongPress { get; init; }

	public Action? OnSwipe { get; init; }

	public Action? OnRefresh { get; init; }

	private protected Element()
	{
	}
}

public sealed record Column(IReadOnlyList<Element> Children) : Element;

public sealed record Text(string Value) : Element;

public sealed record Button(
	string Title,
	Action OnPress,
	string AccessibilityLabel,
	[global::System.Runtime.CompilerServices.CallerFilePath] string SourceFile = "",
	[global::System.Runtime.CompilerServices.CallerLineNumber] int SourceLine = 0) : Element;

public sealed record TextField(
	string Value,
	Action<string> OnChange,
	string Placeholder,
	string AccessibilityLabel,
	[global::System.Runtime.CompilerServices.CallerFilePath] string SourceFile = "",
	[global::System.Runtime.CompilerServices.CallerLineNumber] int SourceLine = 0) : Element
{
	public Keyboard Keyboard { get; init; }

	public int Lines { get; init; }

	public Submit Submit { get; init; }

	public Action? OnSubmit { get; init; }
}

public sealed record TextInput(
	string Value,
	Action<string> OnChange,
	string Placeholder,
	string AccessibilityLabel,
	[global::System.Runtime.CompilerServices.CallerFilePath] string SourceFile = "",
	[global::System.Runtime.CompilerServices.CallerLineNumber] int SourceLine = 0) : Element
{
	public Keyboard Keyboard { get; init; }

	public int Lines { get; init; }

	public Submit Submit { get; init; }

	public Action? OnSubmit { get; init; }
}

public sealed record Switch(
	bool Value,
	Action<bool> OnChange,
	string AccessibilityLabel,
	[global::System.Runtime.CompilerServices.CallerFilePath] string SourceFile = "",
	[global::System.Runtime.CompilerServices.CallerLineNumber] int SourceLine = 0) : Element;

public sealed record ScrollView(Element Child) : Element;

public sealed record ListView(IReadOnlyList<Element> Rows) : Element;

public static class FeedRows
{
	public static Element Window(List<Element> rows, int headers)
	{
		ArgumentNullException.ThrowIfNull(rows);
		if (headers < 0)
			headers = 0;
		if (headers > rows.Count)
			headers = rows.Count;
		if (rows.Count - headers <= 24)
			return new Column(rows) { Key = "feed" };
		var head = new List<Element>(headers + 1);
		for (var index = 0; index < headers; index++)
			head.Add(rows[index]);
		var body = new List<Element>(rows.Count - headers);
		for (var index = headers; index < rows.Count; index++)
			body.Add(rows[index]);
		head.Add(new ListView(body) { Key = "window" });
		return new Column(head) { Key = "feed" };
	}
}

public sealed record Image(string Source) : Element;

public sealed record HostView(string Kind, string Prop, string Value) : Element
{
	public string Event { get; init; } = "";

	public Action<string, string>? OnEvent { get; init; }
}

public interface INativeServices : IDisposable
{
	void CopyText(string label, string text);

	void Pulse();
}

public interface IPlatform
{
	void CopyNonce(string nonce);

	void RequestRender();

	void Pulse()
	{
	}

	void CopyText(string label, string text) => CopyNonce(text);

	void Push(string screen)
	{
	}

	void Push(string screen, object row)
	{
	}

	void WriteBack()
	{
	}

	void Pop()
	{
	}

	void Effect(string property, string resource)
	{
	}

	void Every(string property)
	{
	}

	void Module(string module, string method, string[] args)
	{
	}

	Task<string> ModuleAsync(string module, string method, string[] args)
	{
		_ = module;
		_ = method;
		_ = args;
		return Task.FromResult("");
	}

	void Continue(Task<string> task, Action<string> resume)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(resume);
		if (task.IsCompleted)
		{
			resume(AwaitText(task));
			return;
		}

		task.ContinueWith(done => resume(AwaitText(done)));
	}

	static string AwaitText(Task<string> task) =>
		task.IsCompletedSuccessfully ? task.Result ?? "" : "http-error: " + (task.Exception?.GetBaseException().Message ?? "failed");
}

public sealed class AppState
{
	public int Count { get; set; }

	public string Name { get; set; } = "";

	public bool GreetingFirst { get; set; }

	public bool PlainRoot { get; set; }
}

public sealed class AppSession
{
	public static AppSession Shared { get; } = new();

	public AppState State { get; } = new();
}

