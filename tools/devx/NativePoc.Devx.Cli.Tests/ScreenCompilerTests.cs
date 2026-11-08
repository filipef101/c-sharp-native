using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ScreenCompilerTests
{
	[Fact]
	public void Committed_screen_compiles_without_an_extra_using()
	{
		var repo = RepoRoot.Find(AppContext.BaseDirectory);
		var source = File.ReadAllText(Path.Combine(repo, ScreenCompiler.RelativePath));
		Assert.DoesNotContain("using System.Collections.Generic", source);
		var compiler = ScreenCompiler.Create(repo);

		var compiled = compiler.Compile(source);

		Assert.Empty(compiled.Errors);
		Assert.NotNull(compiled.Assembly);
		Assert.True(compiled.Assembly.Length > 1000);
	}

	[Fact]
	public void A_syntax_error_stays_on_the_screen_file_and_the_compiler_recovers()
	{
		var repo = RepoRoot.Find(AppContext.BaseDirectory);
		var source = File.ReadAllText(Path.Combine(repo, ScreenCompiler.RelativePath));
		var compiler = ScreenCompiler.Create(repo);
		var broken = source.Replace("public const string TitlePrefix = \"Counter \";", "public const string TitlePrefix = ;", StringComparison.Ordinal);

		var failed = compiler.Compile(broken);
		var recovered = compiler.Compile(source);

		Assert.Null(failed.Assembly);
		Assert.NotEmpty(failed.Errors);
		Assert.All(failed.Errors, error => Assert.StartsWith("src/NativePoc.Screen/Screen.cs:", error.Display));
		Assert.Contains(failed.Errors, error => error.Code.StartsWith("CS", StringComparison.Ordinal));
		Assert.Empty(recovered.Errors);
		Assert.NotNull(recovered.Assembly);
	}

	[Fact]
	public void A_screen_can_name_IReadOnlyList_without_its_own_using()
	{
		var repo = RepoRoot.Find(AppContext.BaseDirectory);
		var compiler = ScreenCompiler.Create(repo);
		var source = """
			namespace NativePoc;
			public static class Screen
			{
				public static Element Render(AppState state, IPlatform platform)
				{
					IReadOnlyList<Element> children = new Element[] { new Text("Hi") };
					return new Column(children);
				}
			}
			""";

		var compiled = compiler.Compile(source);

		Assert.Empty(compiled.Errors);
		Assert.NotNull(compiled.Assembly);
	}

	[Fact]
	public void Implicit_usings_match_the_sdk_global_set()
	{
		var source = ScreenCompiler.ImplicitUsingsSource();
		Assert.Contains("global using global::System.Collections.Generic;", source);
		Assert.Equal(7, ScreenCompiler.ImplicitGlobalUsings.Length);
	}
}
