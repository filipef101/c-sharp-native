namespace NativePoc.Tasks;

public sealed class TaskState
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public bool Done { get; set; }
}

public static class TaskScreen
{
	public const string ScreenId = "task";

	public static Element Render(TaskState state, IPlatform platform)
	{
		return new Column(
			new Text(state.Title) { Key = "title", Style = new Style { FontSize = 22 } },
			new Text(state.Done ? "Done" : "Open") { Key = "state" },
			new Button("Back", () => { platform.Pop(); }, "Back") { Key = "back" })
		{ Key = "task" };
	}
}
