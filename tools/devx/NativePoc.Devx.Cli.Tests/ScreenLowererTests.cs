using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class ScreenLowererTests
{
	[Fact]
	public void The_committed_screen_lowers_to_the_same_tree_and_the_same_actions()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), ScreenCompiler.RelativePath));
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, Message(lowered));
		Assert.True(ScreenProgram.IsRecord(lowered.Program));
		Assert.Equal(ScreenProgram.SourceHash(source), RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).SourceHash);

		foreach (var state in States())
		{
			var compiled = Screen.Render(Copy(state), new RecordingPlatform());
			var programmed = Render(lowered.Program!, state, new RecordingPlatform());
			AssertSame(compiled, programmed);
		}
	}

	[Fact]
	public void A_title_prefix_edit_is_a_new_program_and_keeps_the_typed_name()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), ScreenCompiler.RelativePath));
		var edited = source.Replace("public const string TitlePrefix = \"Counter \";", "public const string TitlePrefix = \"Clicks \";", StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(edited);
		Assert.True(lowered.Ok, Message(lowered));
		var tree = Render(lowered.Program!, new AppState { Name = "Ada", Count = 1 }, new RecordingPlatform());
		var column = Assert.IsType<Column>(tree);
		Assert.Equal("Clicks 1", Assert.IsType<Text>(column.Children[0]).Value);
		Assert.Equal("Ada", Assert.IsType<TextField>(column.Children[1]).Value);
		Assert.Equal("Hello Ada", Assert.IsType<Text>(column.Children[3]).Value);
		Assert.NotEqual(ScreenProgram.SourceHash(source), RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).SourceHash);
	}

	[Fact]
	public void A_new_member_is_refused_and_no_assembly_is_produced()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), ScreenCompiler.RelativePath));
		var extra = source.Replace(
			"public const string TitlePrefix",
			"public static int Extra => 1;\n\tpublic const string TitlePrefix",
			StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(extra);
		Assert.False(lowered.Ok);
		Assert.Contains("Extra", lowered.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Save_on_the_program_matches_save_on_the_compiled_screen()
	{
		var source = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), ScreenCompiler.RelativePath));
		var program = SubsetLowerer.TryLower(source).Program;
		Assert.NotNull(program);
		var compiled = new AppState { Name = "Ada" };
		var bag = Seed(new AppState { Name = "Ada" });
		var programPlatform = new RecordingPlatform();
		var compiledPlatform = new RecordingPlatform();
		PressSave(RecordProgram.Bind(program, bag).Render(programPlatform), programPlatform);
		PressSave(Screen.Render(compiled, compiledPlatform), compiledPlatform);
		Assert.Equal(compiled.Count, bag.ReadInt("Count"));
		Assert.Equal(compiledPlatform.Nonce, programPlatform.Nonce);
		Assert.Equal(compiled.Name, bag.ReadString("Name"));
	}

	static Element Render(byte[] program, AppState state, IPlatform platform) =>
		RecordProgram.Bind(program, Seed(state)).Render(platform);

	static RecordProgram.Bag Seed(AppState state)
	{
		var bag = new RecordProgram.Bag();
		bag.SeedInt("Count", state.Count);
		bag.SeedString("Name", state.Name);
		bag.SeedBool("GreetingFirst", state.GreetingFirst);
		bag.SeedBool("PlainRoot", state.PlainRoot);
		return bag;
	}

	static AppState Copy(AppState state) =>
		new()
		{
			Count = state.Count,
			Name = state.Name,
			GreetingFirst = state.GreetingFirst,
			PlainRoot = state.PlainRoot,
		};

	static string Message(SubsetResult result) =>
		result.Diagnostics.Count == 0 ? "" : result.Diagnostics[0].Message;

	static void PressSave(Element tree, RecordingPlatform platform)
	{
		var column = Assert.IsType<Column>(tree);
		Assert.IsType<Button>(column.Children[2]).OnPress();
		Assert.Equal(1, platform.Renders);
	}

	static IEnumerable<AppState> States()
	{
		yield return new AppState();
		yield return new AppState { Name = "Ada" };
		yield return new AppState { Count = 3, Name = "Ada" };
		yield return new AppState { GreetingFirst = true, Name = "Bea", Count = 2 };
		yield return new AppState { PlainRoot = true, Count = 5, GreetingFirst = true, Name = "Ada" };
	}

	static void AssertSame(Element expected, Element actual)
	{
		Assert.Equal(expected.Key, actual.Key);
		switch (expected)
		{
			case Text text:
				Assert.Equal(text.Value, Assert.IsType<Text>(actual).Value);
				return;
			case TextField field:
				var otherField = Assert.IsType<TextField>(actual);
				Assert.Equal(field.Value, otherField.Value);
				Assert.Equal(field.Placeholder, otherField.Placeholder);
				Assert.Equal(field.AccessibilityLabel, otherField.AccessibilityLabel);
				return;
			case Button button:
				var otherButton = Assert.IsType<Button>(actual);
				Assert.Equal(button.Title, otherButton.Title);
				Assert.Equal(button.AccessibilityLabel, otherButton.AccessibilityLabel);
				return;
			case Column column:
				var otherColumn = Assert.IsType<Column>(actual);
				Assert.Equal(column.Children.Count, otherColumn.Children.Count);
				for (var index = 0; index < column.Children.Count; index++)
					AssertSame(column.Children[index], otherColumn.Children[index]);
				return;
			default:
				throw new InvalidOperationException(expected.GetType().Name);
		}
	}

	sealed class RecordingPlatform : IPlatform
	{
		public string? Nonce { get; private set; }

		public int Renders { get; private set; }

		public void CopyNonce(string nonce) => Nonce = nonce;

		public void RequestRender() => Renders++;
	}
}
