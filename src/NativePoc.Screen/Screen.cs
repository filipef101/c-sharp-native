namespace NativePoc;

public static class Screen
{
	public const string TitlePrefix = "Counter ";

	public static Element Render(AppState state, IPlatform platform)
	{
		var title = new Text(TitlePrefix + state.Count) { Key = "title" };
		if (state.PlainRoot)
			return title;
		var name = new TextField(
			state.Name,
			value =>
			{
				state.Name = value;
				platform.RequestRender();
			},
			"Name",
			"Name")
		{ Key = "name" };
		var save = new Button(
			"Save",
			() =>
			{
				state.Count++;
				platform.CopyNonce("poc-" + state.Count);
				platform.RequestRender();
			},
			"Save name")
		{ Key = "save" };
		var greeting = new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name) { Key = "greeting" };
		var children = state.GreetingFirst
			? new Element[] { greeting, name, save, title }
			: new Element[] { title, name, save, greeting };
		return new Column(children);
	}
}
