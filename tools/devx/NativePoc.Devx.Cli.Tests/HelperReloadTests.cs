using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class HelperReloadTests
{
	[Fact]
	public void A_thousand_iterations_finish_and_record_steps()
	{
		var source = NewLoop.Template("hello").Replace(
			"state.Count++;",
			"var n = 0; for (var i = 0; i < 1000; i++) n++; state.Count = n;",
			StringComparison.Ordinal);
		var session = Open(source);
		for (var index = 0; index < 3; index++)
			session.Tap("hello/add");

		Assert.Contains("value=\"Count 1000\"", session.Tree(), StringComparison.Ordinal);
		Assert.Null(ActionLog.LastStop);
		Assert.Equal(11010, ActionLog.LastSteps);
	}

	[Fact]
	public void Static_helpers_call_each_other_and_recurse()
	{
		var source = Screen(
			"""
			static int Down(int n) => n <= 0 ? 0 : 1 + Down(n - 1);
			static int Twice(int n) => n * 2;
			""",
			"state.Count = Twice(Down(4));");
		var session = Open(source);
		session.Tap("hello/add");
		Assert.Contains("value=\"Count 8\"", session.Tree(), StringComparison.Ordinal);
		Assert.Null(ActionLog.LastStop);
	}

	[Fact]
	public void An_instance_helper_reads_state()
	{
		var source = """
			namespace NativePoc.Hello;

			public sealed class HelloState
			{
				public int Count { get; set; }
				public string Title { get; set; } = "Hello";
				public int Twice() => Count * 2;
			}

			public static class HelloScreen
			{
				public const string ScreenId = "hello";

				public static Element Render(HelloState state, IPlatform platform)
				{
					return new Column(
						new Text("Count " + state.Count) { Key = "count" },
						new Button("Add", () => { state.Count = 4; state.Count = state.Twice(); platform.RequestRender(); }, "Add") { Key = "add" }
					) { Key = "hello" };
				}
			}
			""";
		var session = Open(source);
		session.Tap("hello/add");
		Assert.Contains("value=\"Count 8\"", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void A_block_helper_returns_from_the_middle()
	{
		var source = Screen(
			"""
			static int Down(int n)
			{
				if (n <= 0)
					return 0;
				return 1 + Down(n - 1);
			}
			""",
			"state.Count = Down(3);");
		var session = Open(source);
		session.Tap("hello/add");
		Assert.Contains("value=\"Count 3\"", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void Recursion_stops_at_the_call_depth()
	{
		var source = Screen(
			"""
			static void Spin()
			{
				Spin();
			}
			""",
			"Spin();");
		var session = Open(source);
		session.Tap("hello/add");
		Assert.Equal(RecordProgram.CallDepthStop, ActionLog.LastStop);
		Assert.True(ActionLog.LastSteps < RecordProgram.ActionStepBudget);
	}

	[Fact]
	public void An_element_helper_draws_a_list_row()
	{
		var source = """
			namespace NativePoc.Notes;

			public sealed class Note
			{
				public string Name { get; set; } = "";
				public bool Packed { get; set; }
			}

			public sealed class NotesState
			{
				public List<Note> Items { get; } = [];
				public int Count { get; set; }
			}

			public static class NotesScreen
			{
				public const string ScreenId = "notes";

				static Element Row(Note item) => new Text(item.Name) { Key = item.Name };

				public static Element Render(NotesState state, IPlatform platform)
				{
					var rows = new List<Element>();
					foreach (var item in state.Items)
						rows.Add(Row(item));
					rows.Add(new Button("Add", () => { state.Items.Add(new Note { Name = "Lantern" }); state.Items.Add(new Note { Name = "Rope" }); platform.RequestRender(); }, "Add") { Key = "add" });
					return new Column(rows) { Key = "notes" };
				}
			}
			""";
		var session = Open(source);
		session.Tap("notes/add");
		Assert.Contains("value=\"Lantern\"", session.Tree(), StringComparison.Ordinal);
		Assert.Contains("value=\"Rope\"", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void Editing_a_helper_changes_the_next_tap()
	{
		var first = Open(Screen("static int Twice(int n) => n * 2;", "state.Count = Twice(3);"));
		first.Tap("hello/add");
		Assert.Contains("value=\"Count 6\"", first.Tree(), StringComparison.Ordinal);
		var edited = SubsetLowerer.TryLower(Screen("static int Twice(int n) => n * 3;", "state.Count = Twice(3);"));
		Assert.True(edited.Ok);
		var again = ScreenSession.Open(edited.Program!);
		again.Tap("hello/add");
		Assert.Contains("value=\"Count 9\"", again.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void Render_if_else_ternary_and_early_return_follow_state()
	{
		var early = Open(Render(
			"""
			if (state.Count > 0)
				return new Text("yes") { Key = "count" };
			return new Column(
				new Text("no") { Key = "count" },
				new Button("Add", () => { state.Count = 1; platform.RequestRender(); }, "Add") { Key = "add" }
			) { Key = "hello" };
			"""));
		Assert.Contains("value=\"no\"", early.Tree(), StringComparison.Ordinal);
		early.Tap("hello/add");
		Assert.Contains("value=\"yes\"", early.Tree(), StringComparison.Ordinal);
		Assert.DoesNotContain("hello/add", early.Tree(), StringComparison.Ordinal);

		var choice = Open(Render(
			"""
			return new Column(
				state.Count > 0 ? new Text("yes") { Key = "flag" } : new Text("no") { Key = "flag" },
				new Button("Add", () => { state.Count = 1; platform.RequestRender(); }, "Add") { Key = "add" }
			) { Key = "hello" };
			"""));
		Assert.Contains("value=\"no\"", choice.Tree(), StringComparison.Ordinal);
		choice.Tap("hello/add");
		Assert.Contains("value=\"yes\"", choice.Tree(), StringComparison.Ordinal);

		var either = Open(Render(
			"""
			if (state.Count > 0)
				return new Text("more") { Key = "count" };
			else
				return new Text("zero") { Key = "count" };
			"""));
		Assert.Contains("value=\"zero\"", either.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void Keyed_children_keep_their_text_when_reordered()
	{
		var first = """
			namespace NativePoc.Hello;

			public sealed class HelloState
			{
				public string Title { get; set; } = "One";
				public string Note { get; set; } = "Two";
			}

			public static class HelloScreen
			{
				public const string ScreenId = "hello";

				public static Element Render(HelloState state, IPlatform platform)
				{
					return new Column(
						new Text(state.Title) { Key = "a" },
						new Text(state.Note) { Key = "b" }
					) { Key = "hello" };
				}
			}
			""";
		var swapped = first.Replace(
			"""
					return new Column(
						new Text(state.Title) { Key = "a" },
						new Text(state.Note) { Key = "b" }
					) { Key = "hello" };
			""",
			"""
					return new Column(
						new Text(state.Note) { Key = "b" },
						new Text(state.Title) { Key = "a" }
					) { Key = "hello" };
			""",
			StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(first);
		var next = SubsetLowerer.TryLower(swapped);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.True(next.Ok, next.Diagnostics.Count == 0 ? "" : next.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		var before = RecordProgram.Bind(lowered.Program!, bag).Render(new Quiet());
		bag.WriteString("Title", "Kept");
		bag.WriteString("Note", "Moved");
		var after = RecordProgram.Bind(next.Program!, bag).Render(new Quiet());
		var firstTree = ScreenSnapshot.FormatTree(before);
		var secondTree = ScreenSnapshot.FormatTree(after);
		Assert.Contains("id=\"hello/a\"", secondTree, StringComparison.Ordinal);
		Assert.Contains("value=\"Kept\"", secondTree, StringComparison.Ordinal);
		Assert.Contains("value=\"Moved\"", secondTree, StringComparison.Ordinal);
		Assert.True(secondTree.IndexOf("id=\"hello/b\"", StringComparison.Ordinal) < secondTree.IndexOf("id=\"hello/a\"", StringComparison.Ordinal));
		Assert.True(firstTree.IndexOf("id=\"hello/a\"", StringComparison.Ordinal) < firstTree.IndexOf("id=\"hello/b\"", StringComparison.Ordinal));
	}

	[Fact]
	public void A_new_type_and_a_generic_method_stay_refused()
	{
		var generic = SubsetLowerer.TryLower(Screen("static int Id<T>(int n) => n;", "state.Count = 1;"));
		Assert.False(generic.Ok);
		Assert.Contains("generic", generic.Diagnostics[0].Message, StringComparison.Ordinal);
		var extra = """
			namespace NativePoc.Hello;
			public interface Extra { }
			public sealed class HelloState
			{
				public int Count { get; set; }
				public string Title { get; set; } = "Hello";
			}
			public static class HelloScreen
			{
				public const string ScreenId = "hello";
				public static Element Render(HelloState state, IPlatform platform)
				{
					return new Text(state.Title) { Key = "title" };
				}
			}
			""";
		var refused = SubsetLowerer.TryLower(extra);
		Assert.False(refused.Ok);
	}

	static ScreenSession Open(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line);
		return ScreenSession.Open(lowered.Program!);
	}

	static string Screen(string helpers, string handler) =>
		$$"""
		namespace NativePoc.Hello;

		public sealed class HelloState
		{
			public int Count { get; set; }
			public string Title { get; set; } = "Hello";
		}

		public static class HelloScreen
		{
			public const string ScreenId = "hello";

			{{helpers}}

			public static Element Render(HelloState state, IPlatform platform)
			{
				return new Column(
					new Text("Count " + state.Count) { Key = "count" },
					new Button("Add", () => { {{handler}} platform.RequestRender(); }, "Add") { Key = "add" }
				) { Key = "hello" };
			}
		}
		""";

	static string Render(string body) =>
		$$"""
		namespace NativePoc.Hello;

		public sealed class HelloState
		{
			public int Count { get; set; }
			public string Title { get; set; } = "Hello";
		}

		public static class HelloScreen
		{
			public const string ScreenId = "hello";

			public static Element Render(HelloState state, IPlatform platform)
			{
				{{body}}
			}
		}
		""";

	sealed class Quiet : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Push(string screen)
		{
		}

		public void Pop()
		{
		}

		public void Effect(string property, string resource)
		{
		}

		public void Module(string module, string method, string[] args)
		{
		}
	}
}
