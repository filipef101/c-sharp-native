namespace NativePoc.Devx.Tests;

public sealed class SampleLowerTests
{
	[Fact]
	public void Every_sample_screen_lowers()
	{
		var root = Repo();
		var files = Directory.EnumerateFiles(Path.Combine(root, "samples"), "*Screen.cs", SearchOption.AllDirectories)
			.Append(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"))
			.OrderBy(path => path, StringComparer.Ordinal);
		foreach (var file in files)
		{
			var source = File.ReadAllText(file);
			var dir = Path.GetDirectoryName(file)!;
			var companions = Directory.EnumerateFiles(dir, "*.cs")
				.Where(item => !string.Equals(item, file, StringComparison.Ordinal))
				.Select(File.ReadAllText)
				.ToArray();
			var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
			var lowered = SubsetLowerer.TryLower(source, companions, originFile: relative);
			if (!lowered.Ok)
				lowered = SubsetLowerer.TryLower(source, originFile: relative);
			var detail = lowered.Diagnostics.Count == 0
				? ""
				: lowered.Diagnostics[0].Id + " " + lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line + ":" + lowered.Diagnostics[0].Column;
			Assert.True(lowered.Ok, relative + " " + detail);
		}
	}

	[Fact]
	public void A_bare_local_call_runs_while_the_screen_renders()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					void Load()
					{
						state.Title = "loaded";
					}
					Load();
					return new Text(state.Title) { Key = "title" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/notes/NotesScreen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Equal("loaded", session.Bag.ReadString("Title"));
		Assert.Contains("loaded", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void An_unknown_call_fails_on_that_call()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class NotesState
			{
				public string Title { get; set; } = "";
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					Nope();
					return new Text(state.Title) { Key = "title" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "samples/notes/NotesScreen.cs");
		Assert.False(lowered.Ok);
		var diagnostic = Assert.Single(lowered.Diagnostics);
		Assert.Contains("Nope()", diagnostic.Message, StringComparison.Ordinal);
		Assert.True(diagnostic.Line > 0);
		Assert.True(diagnostic.Column > 0);
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}
