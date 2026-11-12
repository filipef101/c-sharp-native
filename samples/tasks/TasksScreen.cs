namespace NativePoc.Tasks;

public sealed class TaskItem
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public bool Done { get; set; }
}

public sealed class TasksState
{
	public string Draft { get; set; } = "";

	public string Error { get; set; } = "";

	public string Saved { get; set; } = "";

	public bool Dark { get; set; }

	public List<TaskItem> Tasks { get; set; } = new List<TaskItem>
	{
		new TaskItem { Id = "1", Title = "Buy milk", Done = false },
	};
}

public static class TasksScreen
{
	public const string ScreenId = "tasks";

	public static Element Render(TasksState state, IPlatform platform)
	{
		async void Save()
		{
			state.Saved = await Persist.Put(platform, "tasks", PersistRows.Encode(state.Tasks, static item => new[] { item.Id, item.Title, item.Done ? "true" : "false" }));
			platform.RequestRender();
		}

		async void Open()
		{
			state.Saved = await Persist.Get(platform, "tasks");
			PersistRows.Fill(state.Tasks, state.Saved, static cells => new TaskItem
			{
				Id = cells.Length > 0 ? cells[0] : "",
				Title = cells.Length > 1 ? cells[1] : "",
				Done = cells.Length > 2 && cells[2] == "true",
			});
			platform.RequestRender();
		}

		void Add()
		{
			if (state.Draft.Length == 0)
			{
				state.Error = "Need a title";
				platform.RequestRender();
			}
			else
			{
				state.Error = "";
				state.Tasks.Add(new TaskItem { Id = state.Draft, Title = state.Draft, Done = false });
				state.Draft = "";
				Save();
			}
		}

		platform.OnOpen(ScreenId, Open);

		var rows = new List<Element>();
		rows.Add(new Text("Tasks") { Key = "heading", Style = new Style { FontSize = 22 } });
		rows.Add(new TextField(state.Draft, value => { state.Draft = value; platform.RequestRender(); }, "Title", "New task") { Key = "draft" });
		rows.Add(new Text(state.Error) { Key = "error" });
		rows.Add(new Button("Add", () => Add(), "Add") { Key = "add" });
		foreach (var item in state.Tasks)
		{
			rows.Add(new Column(
				new Text(item.Title) { Key = "title" },
				new Text(item.Done ? "Done" : "Open") { Key = "state" },
				new Button("Complete", () => { item.Done = !item.Done; Save(); }, "Complete") { Key = "complete" },
				new Button("Delete", () => { state.Tasks.Remove(item); Save(); }, "Delete") { Key = "delete" },
				new Button("Open", () => { platform.Push("task", item); }, "Open") { Key = "open" })
			{ Key = item.Id });
		}

		rows.Add(new Button("Theme", () => { state.Dark = !state.Dark; Theme.Mode(platform, state.Dark); platform.RequestRender(); }, "Theme") { Key = "theme" });
		rows.Add(new Text(state.Saved) { Key = "saved" });
		return new Column(rows) { Key = "tasks" };
	}
}
