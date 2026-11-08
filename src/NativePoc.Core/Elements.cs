namespace NativePoc;

public enum ElementKind
{
	Column = 1,
	Text = 2,
	Button = 3,
	TextField = 4,
}

public abstract record Element
{
	public string? Key { get; init; }

	private protected Element()
	{
	}
}

public sealed record Column(IReadOnlyList<Element> Children) : Element;

public sealed record Text(string Value) : Element;

public sealed record Button(string Title, Action OnPress, string AccessibilityLabel) : Element;

public sealed record TextField(string Value, Action<string> OnChange, string Placeholder, string AccessibilityLabel) : Element;

public interface IPlatform
{
	void CopyNonce(string nonce);

	void RequestRender();
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

