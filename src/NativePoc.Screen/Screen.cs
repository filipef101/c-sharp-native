namespace NativePoc;

public static class Screen
{
	public const string TitlePrefix = "Counter ";

	public static Element Render(AppState state, IPlatform platform)
	{
		return new Column(
		[
			new Text(TitlePrefix + state.Count),
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					platform.RequestRender();
				},
				"Name",
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
