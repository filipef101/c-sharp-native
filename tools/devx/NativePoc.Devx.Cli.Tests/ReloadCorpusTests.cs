namespace NativePoc.Devx.Tests;

public sealed class ReloadCorpusTests
{
	[Fact]
	public void Developer_edits_lower_or_name_the_refusal()
	{
		var hello = NewLoop.Template("hello");
		AssertLower(hello);

		var text = hello.Replace("= \"Hello\";", "= \"Hello again\";", StringComparison.Ordinal);
		Assert.NotEqual(hello, text);
		AssertLower(text);

		var style = text.Replace(
			"new Text(state.Title) { Key = \"title\" }",
			"new Text(state.Title) { Key = \"title\", Style = new Style { FontSize = 28 } }",
			StringComparison.Ordinal);
		Assert.NotEqual(text, style);
		AssertLower(style);

		var handler = style.Replace(
			"state.Count++;",
			"state.Count++; state.Title = \"Added\";",
			StringComparison.Ordinal);
		Assert.NotEqual(style, handler);
		AssertLower(handler);

		var assigned = handler.Replace("state.Count++;", "state.Count += 1;", StringComparison.Ordinal);
		var refused = SubsetLowerer.TryLower(assigned);
		Assert.False(refused.Ok);
		Assert.Equal(SubsetLowerer.BadLambda, refused.Diagnostics[0].Id);
		var line = DeveloperRefusal.Line("samples/hello/HelloScreen.cs", refused.Diagnostics[0]);
		Assert.Contains("samples/hello/HelloScreen.cs:", line, StringComparison.Ordinal);
		Assert.Contains("subset.bad-lambda:", line, StringComparison.Ordinal);
		Assert.Contains("A screen action can assign state, request a render, or copy text.", line, StringComparison.Ordinal);
		Assert.Equal("restarting", DeveloperRefusal.FollowUp(true, false));
		Assert.Equal("pass --restart-on-rude to relaunch and keep state", DeveloperRefusal.FollowUp(false, true));
		Assert.Null(DeveloperRefusal.FollowUp(false, false));

		var renamed = hello.Replace("Count", "Total", StringComparison.Ordinal);
		Assert.DoesNotContain("Count", renamed, StringComparison.Ordinal);
		AssertLower(renamed);

		var half = hello.Replace("public int Count { get; set; }", "public int Total { get; set; }", StringComparison.Ordinal);
		var halfRefused = SubsetLowerer.TryLower(half);
		Assert.False(halfRefused.Ok);
		Assert.Equal(SubsetLowerer.UnknownMember, halfRefused.Diagnostics[0].Id);

		var broken = hello.Replace("public const string ScreenId = \"hello\";", "public const string ScreenId = \"hello\"", StringComparison.Ordinal);
		var parsed = SubsetLowerer.TryLower(broken);
		Assert.False(parsed.Ok);
		Assert.Equal(SubsetLowerer.NotAScreen, parsed.Diagnostics[0].Id);
		AssertLower(hello);

		var pushed = hello.Replace("state.Count++;", "state.Count++; platform.Push(\"settings\");", StringComparison.Ordinal);
		AssertLower(pushed);

		var root = Directory.CreateTempSubdirectory("cnative-corpus-").FullName;
		NewLoop.Write(root, "hello");
		NewLoop.Write(root, "notes");
		Assert.Equal(
			new[] { "samples/hello/HelloScreen.cs", "samples/notes/NotesScreen.cs" },
			NewLoop.AppScreens(root));
		AssertLower(File.ReadAllText(Path.Combine(root, "samples", "notes", "NotesScreen.cs")));

		var packing = RepoFile("samples", "NativePoc.Packing", "PackingScreen.cs");
		var template = packing.Replace("new Text(item.Name)", "new Text(item.Name + \" item\")", StringComparison.Ordinal);
		Assert.NotEqual(packing, template);
		AssertLower(template);
		AssertLower(packing);
	}

	static void AssertLower(string source)
	{
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Id + ": " + lowered.Diagnostics[0].Message);
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
