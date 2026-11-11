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

public abstract record Element
{
	public string? Key { get; init; }

	public Style? Style { get; init; }

	private protected Element()
	{
	}
}

public sealed record Column(IReadOnlyList<Element> Children) : Element;

public sealed record Text(string Value) : Element;

public sealed record Button(string Title, Action OnPress, string AccessibilityLabel) : Element;

public sealed record TextField(string Value, Action<string> OnChange, string Placeholder, string AccessibilityLabel) : Element;

public sealed record Switch(bool Value, Action<bool> OnChange, string AccessibilityLabel) : Element;

public sealed record ScrollView(Element Child) : Element;

public sealed record ListView(IReadOnlyList<Element> Rows) : Element;

public sealed record Image(string Source) : Element;

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

	void Pop()
	{
	}

	void Effect(string property, string resource)
	{
	}

	void Every(string property)
	{
	}
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

