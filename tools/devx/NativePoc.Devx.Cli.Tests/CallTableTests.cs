using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class CallTableTests
{
	[Fact]
	public void A_handler_calls_an_app_service_and_a_library_method()
	{
		var service = """
			namespace NativePoc;
			[ReloadCallable]
			public static class CounterService
			{
				public static int Next(int count) => count + 5;
			}
			""";
		var library = """
			using NativePoc;
			[assembly: ReloadCallable("NativePoc.Library")]
			namespace NativePoc.Library;
			public static class LibraryFormat
			{
				public static string Label(string name) => name.Length == 0 ? "empty" : name.Trim();
			}
			""";
		var source = """
			namespace NativePoc.Hello;
			public sealed class HelloState
			{
				public int Count { get; set; }
				public string Title { get; set; } = "  Ada  ";
			}
			public static class HelloScreen
			{
				public const string ScreenId = "hello";
				public static Element Render(HelloState state, IPlatform platform)
				{
					return new Column(
						new Text("Count " + state.Count) { Key = "count" },
						new Text(state.Title) { Key = "title" },
						new Button("Add", () => { state.Count = CounterService.Next(state.Count); state.Title = LibraryFormat.Label(state.Title); platform.RequestRender(); }, "Add") { Key = "add" }
					) { Key = "hello" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, [], [service, library]);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		RecordProgram.BindAppCall((type, method, args) =>
		{
			if ((type == "CounterService" || type == "NativePoc.CounterService") && method == "Next")
				return RecordProgram.AppValue.FromInt(args[0].Int + 5);
			if ((type == "LibraryFormat" || type == "NativePoc.Library.LibraryFormat") && method == "Label")
			{
				var name = args[0].Text ?? "";
				return RecordProgram.AppValue.FromStr(name.Length == 0 ? "empty" : name.Trim());
			}

			throw new InvalidOperationException("No compiled method " + type + "." + method + ". The call table has no arm. Relaunch to compile a new method.");
		});
		try
		{
			var session = ScreenSession.Open(lowered.Program!);
			Assert.Contains("value=\"  Ada  \"", session.Tree(), StringComparison.Ordinal);
			session.Tap("hello/add");
			Assert.Contains("value=\"Count 5\"", session.Tree(), StringComparison.Ordinal);
			Assert.Contains("value=\"Ada\"", session.Tree(), StringComparison.Ordinal);
			Assert.Null(ActionLog.LastStop);
		}
		finally
		{
			RecordProgram.BindAppCall(null);
		}
	}

	[Fact]
	public void An_unknown_compiled_method_is_refused()
	{
		var service = """
			namespace NativePoc;
			[ReloadCallable]
			public static class CounterService
			{
				public static int Next(int count) => count + 5;
			}
			""";
		var source = """
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
					return new Column(
						new Button("Add", () => { state.Count = CounterService.Missing(state.Count); platform.RequestRender(); }, "Add") { Key = "add" }
					) { Key = "hello" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, [], [service]);
		Assert.False(lowered.Ok);
		Assert.Contains("CounterService.Missing is not in the compiled call table", lowered.Diagnostics[0].Message, StringComparison.Ordinal);
		Assert.Contains("relaunch", lowered.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	[Fact]
	public void A_shared_component_is_inlined_into_every_screen()
	{
		var banner = """
			namespace NativePoc.Board;
			public static class Banner
			{
				public static Element Line(BoardState state) => new Text("banner-v1") { Key = "banner" };
			}
			""";
		var home = Screen("home", "Home");
		var other = Screen("other", "Other");
		var first = SubsetLowerer.TryLower(home, [banner], []);
		var second = SubsetLowerer.TryLower(other, [banner], []);
		Assert.True(first.Ok, first.Diagnostics.Count == 0 ? "" : first.Diagnostics[0].Message);
		Assert.True(second.Ok, second.Diagnostics.Count == 0 ? "" : second.Diagnostics[0].Message);
		Assert.Contains("value=\"banner-v1\"", ScreenSession.Open(first.Program!).Tree(), StringComparison.Ordinal);
		Assert.Contains("value=\"banner-v1\"", ScreenSession.Open(second.Program!).Tree(), StringComparison.Ordinal);
		var edited = banner.Replace("banner-v1", "banner-v2", StringComparison.Ordinal);
		var again = SubsetLowerer.TryLower(home, [edited], []);
		Assert.True(again.Ok, again.Diagnostics.Count == 0 ? "" : again.Diagnostics[0].Message);
		Assert.Contains("value=\"banner-v2\"", ScreenSession.Open(again.Program!).Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void A_companion_reloads_and_a_compiled_model_restarts()
	{
		Assert.True(ChangeEffectRules.IsAppScreen("samples/board/HomeScreen.cs"));
		Assert.True(ChangeEffectRules.IsAppScreen("samples/board/OtherScreen.cs"));
		Assert.True(ChangeEffectRules.IsSampleCompanion("samples/board/Banner.cs"));
		Assert.Equal(ChangeEffect.ReloadScreen, ChangeEffectRules.Classify(["samples/board/Banner.cs"]));
		Assert.Equal(ChangeEffect.RestartRequired, ChangeEffectRules.Classify(["samples/NativePoc.Library/LibraryFormat.cs"]));
		Assert.Equal(ChangeEffect.RestartRequired, ChangeEffectRules.Classify(["src/NativePoc.Screen/CounterService.cs"]));
		Assert.Equal(
			ChangeEffect.RestartRequired,
			ChangeEffectRules.Classify(["samples/board/HomeScreen.cs", "samples/NativePoc.Library/LibraryFormat.cs"]));
		Assert.True(ChangeEffectRules.IsHostEdit("src/NativePoc.Core/RecordProgram.cs"));
		Assert.False(ChangeEffectRules.IsHostEdit("src/NativePoc.Android/obj/Debug/generated.cs"));
		Assert.False(ChangeEffectRules.IsHostEdit("src/NativePoc.Screen/CounterService.cs"));
	}

	static string Screen(string id, string title) =>
		$$"""
		namespace NativePoc.Board;
		public sealed class BoardState
		{
			public int Count { get; set; }
			public string Title { get; set; } = "{{title}}";
		}
		public static class {{title}}Screen
		{
			public const string ScreenId = "{{id}}";
			public static Element Render(BoardState state, IPlatform platform)
			{
				return new Column(
					Banner.Line(state),
					new Text(state.Title) { Key = "title" }
				) { Key = "{{id}}" };
			}
		}
		""";
}
