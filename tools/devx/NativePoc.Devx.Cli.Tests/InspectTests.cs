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
		Assert.Contains("\"code\":\"touch-target\",\"id\":\"scroll/list/tent/name\",\"message\":\"Touch target scroll/list/tent/name is 336 by 24 dp, under 48 dp.\"", packing, StringComparison.Ordinal);
		Assert.Equal(counter, ScreenInspect.FormatFile(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs")));
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
	public void Inspect_fails_a_screen_with_no_heading()
	{
		var json = ScreenInspect.Format(new Text("Hello") { Key = "body" });
		Assert.Contains("\"code\":\"missing-heading\",\"id\":\"\",\"message\":\"The screen has no heading.\"", json, StringComparison.Ordinal);
		Assert.Equal(2, InspectLoop.Run(["inspect"]));
	}
}
