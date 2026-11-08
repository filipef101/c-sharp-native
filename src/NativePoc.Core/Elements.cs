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
	private protected Element()
	{
	}
}

public sealed record Column(IReadOnlyList<Element> Children) : Element;

public sealed record Text(string Value) : Element;

public sealed record Button(string Title, Action OnPress, string AccessibilityLabel) : Element;

public sealed record TextField(string Value, Action<string> OnChange, string Placeholder) : Element;

public interface IPlatform
{
	void CopyNonce(string nonce);

	void RequestRender();
}

public sealed class AppState
{
	public int Count { get; set; }

	public string Name { get; set; } = "";
}

public sealed class AppSession
{
	public static AppSession Shared { get; } = new();

	public AppState State { get; } = new();
}

public static class Screen
{
	public static Element Render(AppState state, IPlatform platform)
	{
		return new Column(
		[
			new Text("Counter " + state.Count),
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					platform.RequestRender();
				},
				"Name"),
			new Button(
				"Save",
				() =>
				{
					state.Count++;
					platform.CopyNonce("poc-" + state.Count);
					platform.RequestRender();
				},
				"Save name"),
			new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name),
		]);
	}
}
