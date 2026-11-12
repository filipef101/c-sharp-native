namespace NativePoc;

public static class Screen
{
	public const string TitlePrefix = "Counter ";

	public const string ScreenId = "counter";

	public static Element Render(AppState state, IPlatform platform)
	{
		if (state.PlainRoot)
			return new Text(TitlePrefix + state.Count) { Key = "title", Style = new Style { FontSize = 22 } };
		if (state.GreetingFirst)
			return new Column(
			[
				new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name) { Key = "greeting" },
				new TextField(
					state.Name,
					value =>
					{
						state.Name = value;
						platform.RequestRender();
					},
					"Name",
					"Name")
				{ Key = "name" },
				new Button(
					"Save",
					() =>
					{
						state.Count++;
						platform.CopyNonce("poc-" + state.Count);
						platform.RequestRender();
					},
					"Save name")
				{ Key = "save" },
				new Text(TitlePrefix + state.Count) { Key = "title", Style = new Style { FontSize = 22 } },
				new Button("Packing", () => { platform.Push("packing"); platform.RequestRender(); }, "Packing") { Key = "packing" },
			]);
		return new Column(
		[
			new Text(TitlePrefix + state.Count) { Key = "title", Style = new Style { FontSize = 22 } },
			new TextField(
				state.Name,
				value =>
				{
					state.Name = value;
					platform.RequestRender();
				},
				"Name",
				"Name")
			{ Key = "name" },
			new Button(
				"Save",
				() =>
				{
					state.Count++;
					platform.CopyNonce("poc-" + state.Count);
					platform.RequestRender();
				},
				"Save name")
			{ Key = "save" },
			new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name) { Key = "greeting" },
			new Button("Packing", () => { platform.Push("packing"); platform.RequestRender(); }, "Packing") { Key = "packing" },
		]);
	}
}
