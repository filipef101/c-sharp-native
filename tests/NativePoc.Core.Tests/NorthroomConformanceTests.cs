using NativePoc.Showcase;

namespace NativePoc.Tests;

public sealed class NorthroomConformanceTests
{
	[Fact]
	public void Every_Northroom_screen_runs_on_the_headless_host()
	{
		var host = new HeadlessHost();
		var home = new HomeState();
		var shelf = new ShelfState();
		var piece = new PieceState();
		var room = new RoomState();
		host.Show(platform =>
		{
			switch (host.NavTop)
			{
				case "shelf":
					return ShelfScreen.Render(shelf, platform);
				case "piece":
					if (host.NavArgument is Piece row && !string.Equals(piece.Id, row.Id, StringComparison.Ordinal))
					{
						piece.Id = row.Id;
						piece.Title = row.Title;
						piece.Note = row.Note;
						piece.Tone = row.Tone;
						piece.Kept = "";
						piece.Status = "";
					}

					return PieceScreen.Render(piece, platform);
				case "room":
					return RoomScreen.Render(room, platform);
				case "home":
					return HomeScreen.Render(home, platform);
				default:
					throw new InvalidOperationException("Unknown Northroom screen " + host.NavTop + ".");
			}
		});

		Assert.Equal(1, host.NavDepth);
		Assert.Contains("value=\"Northroom\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Contains("value=\"8\"", host.Accessibility, StringComparison.Ordinal);
		Assert.DoesNotContain("label=\"Back\"", host.Accessibility, StringComparison.Ordinal);
		BelowTheBar(host.Tree!, "scroll/page/hero/heading");
		var pieces = Box(host.Tree!, "scroll/page/stats/pieces", 1f);
		var kept = Box(host.Tree!, "scroll/page/stats/kept", 1f);
		var kiln = Box(host.Tree!, "scroll/page/stats/kiln", 1f);
		Assert.True(pieces.Left < kept.Left);
		Assert.True(kept.Left < kiln.Left);
		Assert.Equal(pieces.Top, kept.Top);
		Assert.Equal(kept.Top, kiln.Top);

		Press(host, "scroll/page/shelf");
		Assert.Equal(2, host.NavDepth);
		Assert.Equal("shelf", host.NavTop);
		Assert.Equal("heading", host.NavFocus);
		Assert.Contains("value=\"The shelf\"", host.Accessibility, StringComparison.Ordinal);
		var shelfBack = AssertBack(host.Tree!);
		BelowTheBar(host.Tree!, "scroll/list/head/heading");

		Press(host, "scroll/list/row-0/harbor/swatch");
		Assert.Equal(3, host.NavDepth);
		Assert.Equal("piece", host.NavTop);
		Assert.Equal("title", host.NavFocus);
		Assert.Equal("harbor", piece.Id);
		Assert.Contains("value=\"Harbor bowl\"", host.Accessibility, StringComparison.Ordinal);
		var pieceBack = AssertBack(host.Tree!);
		AssertSameBack(shelfBack, pieceBack);
		BelowTheBar(host.Tree!, "scroll/page/title");
		var keep = KeepButton(host.Tree!);
		Assert.Equal("Keep", keep.Title);
		Assert.Equal("Keep", keep.AccessibilityLabel);
		Press(host, "scroll/page/keep");
		if (!host.Accessibility.Contains("value=\"Kept\"", StringComparison.Ordinal))
			host.RequestRender();
		Assert.Equal("harbor", piece.Kept);
		Assert.Equal(3, host.NavDepth);
		keep = KeepButton(host.Tree!);
		Assert.Equal("Kept", keep.Title);
		Assert.Equal("Unkeep", keep.AccessibilityLabel);
		Assert.Equal(0xFF0F6E56u, keep.Style?.Background);
		Press(host, "scroll/page/keep");
		if (!host.Accessibility.Contains("value=\"Keep\"", StringComparison.Ordinal))
			host.RequestRender();
		Assert.Equal("", piece.Kept);
		Assert.Equal(3, host.NavDepth);
		keep = KeepButton(host.Tree!);
		Assert.Equal("Keep", keep.Title);
		Assert.Equal("Keep", keep.AccessibilityLabel);

		Press(host, "scroll/page/back-row/back");
		Assert.Equal(2, host.NavDepth);
		Assert.Equal("shelf", host.NavTop);
		Press(host, "scroll/list/back-row/back");
		Assert.Equal(1, host.NavDepth);
		Assert.Equal("home", host.NavTop);
		Assert.Contains("value=\"Northroom\"", host.Accessibility, StringComparison.Ordinal);

		Press(host, "scroll/page/room");
		Assert.Equal(2, host.NavDepth);
		Assert.Equal("room", host.NavTop);
		Assert.Equal("heading", host.NavFocus);
		var roomBack = AssertBack(host.Tree!);
		AssertSameBack(shelfBack, roomBack);
		BelowTheBar(host.Tree!, "scroll/page/heading");
		Assert.Contains("value=\"Type is at the usual size.\"", host.Accessibility, StringComparison.Ordinal);
		Press(host, "scroll/page/card/large");
		Assert.True(room.Large);
		Assert.Contains("theme.Scale", host.Calls);
		Assert.Contains("value=\"Large type is on.\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Equal(2, host.NavDepth);
		BelowTheBar(host.Tree!, "scroll/page/heading");
	}

	[Fact]
	public void Northroom_wash_stroke_and_across_remain_on_the_headless_host()
	{
		var host = new HeadlessHost();
		var home = new HomeState();
		host.Show(platform => HomeScreen.Render(home, platform));
		var hero = ElementIds.Require(host.Tree!, "scroll/page/hero");
		Assert.Equal(0xFF0F6E56u, hero.Style?.Background);
		Assert.Equal(0xFF3FA888u, hero.Style?.Wash);
		Assert.Equal(1, ElementIds.Require(host.Tree!, "scroll/page/stats").Style?.Across);
		var pieces = Box(host.Tree!, "scroll/page/stats/pieces", 1f);
		var kept = Box(host.Tree!, "scroll/page/stats/kept", 1f);
		var kiln = Box(host.Tree!, "scroll/page/stats/kiln", 1f);
		Assert.Equal(pieces.Top, kept.Top);
		Assert.Equal(kept.Top, kiln.Top);
		Assert.True(pieces.Left < kept.Left);
		Assert.True(kept.Left < kiln.Left);

		var shelf = new ShelfState();
		host.Show(platform => ShelfScreen.Render(shelf, platform));
		Assert.Equal(1, ElementIds.Require(host.Tree!, "scroll/list/row-0").Style?.Across);
		var harbor = Box(host.Tree!, "scroll/list/row-0/harbor", 1f);
		var door = Box(host.Tree!, "scroll/list/row-0/kiln", 1f);
		Assert.Equal(harbor.Top, door.Top);
		Assert.True(harbor.Left < door.Left);
		var swatch = ElementIds.Require(host.Tree!, "scroll/list/row-0/harbor/swatch");
		Assert.Equal(0xFF6E97A0u, swatch.Style?.Background);
		Assert.Equal(0xFFD5E6EAu, swatch.Style?.Wash);

		var piece = new PieceState { Id = "harbor", Title = "Harbor bowl", Tone = "Mist" };
		host.Show(platform => PieceScreen.Render(piece, platform));
		var keep = ElementIds.Require(host.Tree!, "scroll/page/keep");
		Assert.Equal(0xFF0F6E56u, keep.Style?.Stroke);
		Assert.Equal(24, keep.Style?.Radius);
		var tile = ElementIds.Require(host.Tree!, "scroll/page/swatch");
		Assert.NotEqual(0u, tile.Style?.Wash ?? 0);

		host.Show(platform => FloorScreen.Render(new FloorState(), platform));
		var page = Assert.IsType<Column>(host.Tree);
		Assert.Equal("page", page.Key);
		Assert.Empty(page.Children);
	}

	static void Press(HeadlessHost host, string id)
	{
		Assert.NotNull(host.Tree);
		ElementIds.Invoke(host.Tree, id, null);
	}

	static HeadlessLayout.Box AssertBack(Element tree)
	{
		Button? back = null;
		string? id = null;
		foreach (var node in ElementIds.Flatten(tree))
		{
			if (node.Element is not Button button || button.AccessibilityLabel != "Back")
				continue;
			Assert.Null(back);
			back = button;
			id = node.Id;
		}

		Assert.NotNull(back);
		Assert.Equal("←", back.Title);
		Assert.Equal(22, back.Style?.FontSize);
		Assert.Equal(Align.Center, back.Style?.Align);
		Assert.Equal(8, back.Style?.Padding);
		var box = Box(tree, id!, 1f);
		Assert.Equal(0, box.Left);
		Assert.Equal(HeadlessLayout.SystemBarDp, box.Top);
		return box;
	}

	static void AssertSameBack(HeadlessLayout.Box left, HeadlessLayout.Box right)
	{
		Assert.Equal(left.Left, right.Left);
		Assert.Equal(left.Top, right.Top);
		Assert.Equal(left.Width, right.Width);
		Assert.Equal(left.Height, right.Height);
	}

	static Button KeepButton(Element tree)
	{
		Button? keep = null;
		foreach (var node in ElementIds.Flatten(tree))
		{
			if (node.Element is not Button button || button.Key != "keep")
				continue;
			Assert.Null(keep);
			keep = button;
		}

		Assert.NotNull(keep);
		return keep;
	}

	static void BelowTheBar(Element tree, string headingId)
	{
		var normal = Box(tree, headingId, 1f);
		var scaled = Box(tree, headingId, 2f);
		foreach (var box in HeadlessLayout.MeasureBelowBars(tree, 2f))
			Assert.True(box.Top >= HeadlessLayout.SystemBarDp, box.Id);
		Assert.True(scaled.Height > normal.Height);
		Assert.True(scaled.Top >= HeadlessLayout.SystemBarDp);
	}

	static HeadlessLayout.Box Box(Element tree, string id, float fontScale)
	{
		foreach (var box in HeadlessLayout.MeasureBelowBars(tree, fontScale))
		{
			if (box.Id == id)
				return box;
		}

		throw new InvalidOperationException("No box " + id + ".");
	}
}
