using NativePoc;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class InspectTests
{
	[Fact]
	public void Inspect_prints_stable_json_for_counter_feed_visit_and_packing()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var counter = ScreenInspect.FormatFile(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"));
		Assert.Contains("\"id\":\"title\"", counter, StringComparison.Ordinal);
		Assert.Contains("\"heading\":true", counter, StringComparison.Ordinal);
		Assert.Contains("\"lint\": [\n  ],", counter, StringComparison.Ordinal);

		var feed = ScreenInspect.FormatFile(Path.Combine(root, "samples", "feed", "FeedScreen.cs"));
		Assert.Contains("\"id\":\"feed/heading\"", feed, StringComparison.Ordinal);
		Assert.Contains("\"label\":\"Refresh\"", feed, StringComparison.Ordinal);
		Assert.Contains("\"lint\": [\n  ],", feed, StringComparison.Ordinal);

		var visit = ScreenInspect.FormatFile(Path.Combine(root, "samples", "visit", "VisitScreen.cs"));
		Assert.Contains("\"id\":\"scroll/list/place\"", visit, StringComparison.Ordinal);
		Assert.Contains("\"label\":\"Locate\"", visit, StringComparison.Ordinal);
		Assert.Contains("\"lint\": [\n  ],", visit, StringComparison.Ordinal);

		var packing = ScreenInspect.FormatFile(Path.Combine(root, "samples", "NativePoc.Packing", "PackingScreen.cs"));
		Assert.Contains("\"id\":\"scroll/list/title\"", packing, StringComparison.Ordinal);
		Assert.Contains("\"text\":\"Packing\"", packing, StringComparison.Ordinal);
		Assert.Contains("\"id\":\"scroll/list/tent/name\"", packing, StringComparison.Ordinal);
		Assert.Contains("\"lint\": [\n  ],", packing, StringComparison.Ordinal);
		Assert.Equal(counter, ScreenInspect.FormatFile(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs")));
	}

	[Fact]
	public void Inspect_is_clean_on_counter_feed_visit_and_packing()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var files = new (string Name, string Path)[]
		{
			("counter", Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs")),
			("feed", Path.Combine(root, "samples", "feed", "FeedScreen.cs")),
			("visit", Path.Combine(root, "samples", "visit", "VisitScreen.cs")),
			("packing", Path.Combine(root, "samples", "NativePoc.Packing", "PackingScreen.cs")),
			("notes", Path.Combine(root, "samples", "notes", "NotesScreen.cs")),
		};
		foreach (var file in files)
		{
			var json = ScreenInspect.FormatFile(file.Path);
			Assert.True(ScreenInspect.Violations(json) == 0, file.Name + "\n" + json);
		}
	}

	[Fact]
	public void Inspect_fails_a_missing_label_a_short_target_and_a_reversed_focus()
	{
		var screen = new Column(
		[
			new Button("", () => { }, "") { Key = "save" },
			new Text("Later") { Key = "later", OnTap = () => { } },
			new Text("Title") { Key = "title", Style = new Style { FontSize = 22 } },
		]);
		var json = ScreenInspect.Format(screen, [
			new HeadlessLayout.Box("save", 0, 0, 48, 48),
			new HeadlessLayout.Box("later", 0, 56, 80, 24),
			new HeadlessLayout.Box("title", 0, 0, 360, 30),
		]);
		Assert.Contains("\"code\":\"missing-label\",\"id\":\"save\",\"message\":\"Missing label on button save.\"", json, StringComparison.Ordinal);
		Assert.Contains("\"code\":\"touch-target\",\"id\":\"later\"", json, StringComparison.Ordinal);
		Assert.Contains("under 48 dp.", json, StringComparison.Ordinal);
		Assert.Contains("\"code\":\"focus-order\",\"id\":\"title\",\"message\":\"Focus reaches title above later.\"", json, StringComparison.Ordinal);
		Assert.Equal(3, ScreenInspect.Violations(json));
	}

	[Fact]
	public void Inspect_flags_a_fixed_height_text_container()
	{
		var clipped = new Column(
		[
			new Text("Notes") { Key = "title", Style = new Style { FontSize = 22 } },
			new Text("Body") { Key = "body", Style = new Style { Grow = 1, FontSize = 16 } },
		]);
		var json = ScreenInspect.Format(clipped);
		Assert.Contains("\"code\":\"fixed-height\",\"id\":\"body\"", json, StringComparison.Ordinal);
		Assert.Contains("fixed-height container", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"id\":\"title\",\"message\":\"Text in title stays", json, StringComparison.Ordinal);

		var open = new Column(
		[
			new Text("Notes") { Key = "title", Style = new Style { FontSize = 22 } },
			new Text("Body") { Key = "body", Style = new Style { FontSize = 16 } },
			new Button("Add", () => { }, "Add") { Key = "add" },
		]);
		var held = ScreenInspect.Format(open);
		Assert.DoesNotContain("fixed-height", held, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_flags_a_child_that_overflows_or_is_clipped()
	{
		var screen = new Column(
		[
			new Text("Title") { Key = "title", Style = new Style { FontSize = 22 } },
			new Text("Wide") { Key = "wide" },
		]);
		var json = ScreenInspect.Format(screen, [
			new HeadlessLayout.Box("root", 0, 0, 360, 80),
			new HeadlessLayout.Box("title", 0, 0, 360, 30),
			new HeadlessLayout.Box("wide", 40, 30, 400, 24),
		]);
		Assert.Contains("\"code\":\"overflow\",\"id\":\"wide\",\"message\":\"Child wide extends outside root and is clipped.\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"id\":\"title\",\"message\":\"Child title", json, StringComparison.Ordinal);

		var held = ScreenInspect.Format(new Column(
		[
			new Text("Title") { Key = "title", Style = new Style { FontSize = 22 } },
			new Column(
			[
				new Text("One") { Key = "one" },
				new Text("Two") { Key = "two" },
			])
			{
				Key = "row",
				Style = new Style { Across = 1, Gap = 16, Padding = 16 },
			},
		]));
		Assert.DoesNotContain("overflow", held, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_flags_an_image_without_a_description_or_decorative_flag()
	{
		var bare = new Column(
		[
			new Text("Pictures") { Key = "title", Style = new Style { FontSize = 22 } },
			new Image("marker") { Key = "bare" },
		]);
		var json = ScreenInspect.Format(bare);
		Assert.Contains("\"code\":\"image-a11y\",\"id\":\"bare\"", json, StringComparison.Ordinal);
		Assert.Contains("needs a description or Decorative = true.", json, StringComparison.Ordinal);

		var described = new Column(
		[
			new Text("Pictures") { Key = "title", Style = new Style { FontSize = 22 } },
			new Image("marker") { Key = "told", Description = "Map marker" },
			new Image("rule") { Key = "quiet", Decorative = true },
		]);
		var held = ScreenInspect.Format(described);
		Assert.DoesNotContain("image-a11y", held, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_fails_a_screen_with_no_heading()
	{
		var json = ScreenInspect.Format(new Text("Hello") { Key = "body" });
		Assert.Contains("\"code\":\"missing-heading\",\"id\":\"\",\"message\":\"The screen has no heading.\"", json, StringComparison.Ordinal);
		Assert.Equal(2, InspectLoop.Run(["inspect"]));
	}
}
