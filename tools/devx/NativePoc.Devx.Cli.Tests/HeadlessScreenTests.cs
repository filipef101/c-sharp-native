using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class HeadlessScreenTests
{
	[Fact]
	public void Counter_feed_and_visit_dump_accessibility_and_layout()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var counter = Open(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"));
		Assert.Contains("label=\"Save name\"", counter.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=title ", counter.Layout, StringComparison.Ordinal);
		Assert.Contains("width=360", counter.Layout, StringComparison.Ordinal);

		var feed = Open(Path.Combine(root, "samples", "feed", "FeedScreen.cs"));
		Assert.Contains("id=\"feed/heading\"", feed.Accessibility, StringComparison.Ordinal);
		Assert.Contains("label=\"Refresh\"", feed.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=feed/heading ", feed.Layout, StringComparison.Ordinal);
		Assert.Contains("id=feed/window ", feed.Layout, StringComparison.Ordinal);

		var visit = Open(Path.Combine(root, "samples", "visit", "VisitScreen.cs"));
		Assert.Contains("id=\"scroll/list/heading\"", visit.Accessibility, StringComparison.Ordinal);
		Assert.Contains("label=\"Locate\"", visit.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=scroll/list/place ", visit.Layout, StringComparison.Ordinal);
		Assert.Contains("width=360", visit.Layout, StringComparison.Ordinal);
	}

	[Fact]
	public void A_hot_reload_edit_replaces_the_counter_title()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var path = Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs");
		var source = File.ReadAllText(path);
		var host = new HeadlessHost();
		var bag = new RecordProgram.Bag();
		var first = Lower(path, source);
		host.Apply(first, bag);
		Assert.Contains("value=\"Counter 0\"", host.Accessibility, StringComparison.Ordinal);

		var edited = source.Replace("public const string TitlePrefix = \"Counter \";", "public const string TitlePrefix = \"Tally \";", StringComparison.Ordinal);
		Assert.NotEqual(source, edited);
		host.Apply(Lower(path, edited), bag);
		Assert.Contains("value=\"Tally 0\"", host.Accessibility, StringComparison.Ordinal);
		Assert.DoesNotContain("value=\"Counter 0\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=title ", host.Layout, StringComparison.Ordinal);
	}

	static HeadlessHost Open(string path)
	{
		var host = new HeadlessHost();
		host.Apply(Lower(path, File.ReadAllText(path)), new RecordProgram.Bag());
		return host;
	}

	static byte[] Lower(string path, string source)
	{
		var dir = Path.GetDirectoryName(path) ?? "";
		var companions = Directory.EnumerateFiles(dir, "*.cs")
			.Where(file => !string.Equals(file, path, StringComparison.Ordinal))
			.Select(File.ReadAllText)
			.ToArray();
		var lowered = SubsetLowerer.TryLower(source, companions, originFile: path);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		return lowered.Program!;
	}
}
