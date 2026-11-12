namespace NativePoc.Devx.Tests;

public sealed class TasksLowerTests
{
	[Fact]
	public void The_template_and_the_tasks_sample_lower()
	{
		AssertLower(NewLoop.Template("hello"));
		AssertLower(RepoFile("templates", "cnative-app", "screens", "HomeScreen.cs"));
		AssertLower(RepoFile("samples", "tasks", "TasksScreen.cs"));
		AssertLower(RepoFile("samples", "tasks", "TaskScreen.cs"));
	}

	[Fact]
	public void Complete_flips_the_row_and_save_persists_the_list()
	{
		var session = ScreenSession.Open(Lowered(Tasks()));
		session.Tap("tasks/1/complete");
		var row = Assert.Single(session.Bag.Rows("Tasks"));
		Assert.Equal("true", row[2]);
		Assert.Contains(session.Modules, call => call.StartsWith("persist.Put tasks ", StringComparison.Ordinal) && call.Contains("true", StringComparison.Ordinal));
	}

	[Fact]
	public void Open_replaces_the_list_from_persist_and_a_second_render_does_not()
	{
		var encoded = PersistRows.Encode(new List<string[]>
		{
			new[] { "9", "Sweep", "true" },
		});
		var session = ScreenSession.Open(Lowered(Tasks()), (module, method, _) =>
			module == "persist" && method == "Get" ? encoded : "ok");
		var row = Assert.Single(session.Bag.Rows("Tasks"));
		Assert.Equal("9", row[0]);
		Assert.Equal("Sweep", row[1]);
		Assert.Equal("true", row[2]);
		session.Bag.WriteString("Saved", "ok");
		session.Tap("tasks/9/complete");
		Assert.Equal("9", Assert.Single(session.Bag.Rows("Tasks"))[0]);
	}

	[Fact]
	public void Push_uses_the_first_string_when_the_row_has_no_Id()
	{
		var lowered = SubsetLowerer.TryLower(PushTitle());
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var refused = SubsetLowerer.TryLower(PushFlag());
		Assert.False(refused.Ok);
		Assert.Contains("string property", refused.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	[Fact]
	public void A_duplicate_key_names_the_key_and_what_to_change()
	{
		var source = NewLoop.Template("hello").Replace("Key = \"count\"", "Key = \"title\"", StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.False(lowered.Ok);
		Assert.Equal(SubsetLowerer.DuplicateKey, lowered.Diagnostics[0].Id);
		Assert.Contains("Duplicate element key \"title\"", lowered.Diagnostics[0].Message, StringComparison.Ordinal);
		Assert.Contains("different Key", lowered.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	static string PushTitle() => PushScreen("public string Title { get; set; } = \"\";", "new Note { Title = \"Milk\" }");

	static string PushFlag() => PushScreen("public bool Done { get; set; }", "new Note { Done = false }");

	static string PushScreen(string field, string seed) =>
		$$"""
		namespace NativePoc.Tasks;

		public sealed class Note
		{
			{{field}}
		}

		public sealed class NotesState
		{
			public List<Note> Notes { get; set; } = new List<Note>
			{
				{{seed}},
			};
		}

		public static class NotesScreen
		{
			public const string ScreenId = "notes";

			public static Element Render(NotesState state, IPlatform platform)
			{
				var rows = new List<Element>();
				foreach (var item in state.Notes)
					rows.Add(new Button("Open", () => { platform.Push("note", item); }, "Open") { Key = "open" });
				return new Column(rows) { Key = "notes" };
			}
		}
		""";

	static string Tasks() =>
		"""
		namespace NativePoc.Tasks;

		public sealed class TaskItem
		{
			public string Id { get; set; } = "";
			public string Title { get; set; } = "";
			public bool Done { get; set; }
		}

		public sealed class TasksState
		{
			public string Saved { get; set; } = "";
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
					PersistRows.Fill(state.Tasks, state.Saved, static cells => new TaskItem { Id = cells[0], Title = cells[1], Done = cells[2] == "true" });
					platform.RequestRender();
				}

				platform.OnOpen(ScreenId, Open);

				var rows = new List<Element>();
				rows.Add(new Text("Tasks") { Key = "heading", Style = new Style { FontSize = 22 } });
				foreach (var item in state.Tasks)
				{
					rows.Add(new Column(
						new Text(item.Title) { Key = "title" },
						new Button("Complete", () => { item.Done = !item.Done; Save(); }, "Complete") { Key = "complete" })
					{ Key = item.Id });
				}

				return new Column(rows) { Key = "tasks" };
			}
		}
		""";

	[Fact]
	public void An_unknown_call_names_the_expression()
	{
		var source = NewLoop.Template("hello").Replace(
			"new Text(state.Title)",
			"new Text(state.Title.Normalize())",
			StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.False(lowered.Ok);
		var diagnostic = Assert.Single(lowered.Diagnostics);
		Assert.Equal(SubsetLowerer.UnknownExpression, diagnostic.Id);
		Assert.Contains("Normalize()", diagnostic.Message);
		Assert.Contains("list Add/Remove", diagnostic.Message);
	}

	static void AssertLower(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
	}

	static byte[] Lowered(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
		return lowered.Program!;
	}

	static string RepoFile(params string[] parts)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
			{
				var path = dir.FullName;
				foreach (var part in parts)
					path = Path.Combine(path, part);
				return File.ReadAllText(path);
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}
}
