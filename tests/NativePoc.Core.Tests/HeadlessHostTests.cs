namespace NativePoc.Tests;

public sealed class HeadlessHostTests
{
	[Fact]
	public void A_button_label_sits_in_a_normal_height()
	{
		Assert.Equal(52, HostPolicy.ButtonDp(16, 14, 1f));
		Assert.Equal(56, HostPolicy.ButtonDp(16, 16, 1f));
		Assert.Equal(48, HostPolicy.ButtonDp(22, 8, 1f));
		Assert.Equal(104, HostPolicy.ButtonDp(16, 14, 2f));
		Assert.Contains("88 dp", HostPolicy.ButtonLabel(88, "keep"), StringComparison.Ordinal);
		Assert.Null(HostPolicy.ButtonLabel(52, "keep"));

		var kept = new Column(
		[
			new Text("Piece") { Key = "title", Style = new Style { FontSize = 22 } },
			new Button("Keep", () => { }, "Keep")
			{
				Key = "keep",
				Style = new Style { Padding = 14, FontSize = 16, Align = Align.Center },
			},
		]);
		var keep = HeadlessLayout.Measure(kept).Single(box => box.Id == "keep");
		Assert.InRange(keep.Height, HostPolicy.ButtonMinDp, HostPolicy.ButtonMaxDp);
	}

	[Fact]
	public void The_counter_renders_accessibility_and_layout_and_a_program_replaces_it()
	{
		var state = new AppState();
		var host = new HeadlessHost();
		host.Show(platform => Screen.Render(state, platform));
		Assert.Contains("value=\"Counter 0\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Contains("label=\"Save name\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=title left=0 top=0 width=360 height=30", host.Layout, StringComparison.Ordinal);
		Assert.Contains("id=save ", host.Layout, StringComparison.Ordinal);

		ElementIds.Invoke(host.Tree!, "save", null);
		Assert.Contains("value=\"Counter 1\"", host.Accessibility, StringComparison.Ordinal);
		Assert.Equal("poc-1", Assert.Single(host.Copied));

		var code = new byte[]
		{
			RecordProgram.OpPushStr, 0, 0,
			RecordProgram.OpMakeText, 0xFF, 0xFF,
			RecordProgram.OpMakeColumn, 1, 0xFF, 0xFF,
			RecordProgram.OpReturn,
		};
		var bag = new RecordProgram.Bag();
		host.Apply(RecordProgram.Encode(["Edited"], [], code, "edit"), bag);
		Assert.Contains("value=\"Edited\"", host.Accessibility, StringComparison.Ordinal);
		Assert.DoesNotContain("Save name", host.Accessibility, StringComparison.Ordinal);
		Assert.Contains("id=root left=0 top=0 width=360", host.Layout, StringComparison.Ordinal);
	}

	[Fact]
	public void OnOpen_runs_once_and_a_fresh_host_runs_it_again()
	{
		var encoded = PersistRows.Encode(new List<string[]>
		{
			new[] { "9", "Sweep", "true" },
		});
		var first = new List<string> { "Buy milk" };
		var host = new HeadlessHost
		{
			ModuleReply = (module, method, _) => module == "persist" && method == "Get" ? encoded : "ok",
		};
		var opens = 0;
		host.Show(platform =>
		{
			platform.OnOpen("tasks", () =>
			{
				opens++;
				var text = platform.ModuleAsync("persist", "Get", ["tasks"]).GetAwaiter().GetResult();
				PersistRows.Fill(first, text, static cells => cells[1]);
			});
			return new Text(first[0]) { Key = "heading", Style = new Style { FontSize = 22 } };
		});
		Assert.Equal(1, opens);
		Assert.Equal("Sweep", first[0]);
		Assert.Contains("value=\"Sweep\"", host.Accessibility, StringComparison.Ordinal);
		host.RequestRender();
		Assert.Equal(1, opens);
		host.Apply(RecordProgram.Encode(["Edited"], [], [
			RecordProgram.OpPushStr, 0, 0,
			RecordProgram.OpMakeText, 0xFF, 0xFF,
			RecordProgram.OpMakeColumn, 1, 0xFF, 0xFF,
			RecordProgram.OpReturn,
		], "edit"), new RecordProgram.Bag());
		Assert.Equal(1, opens);

		var again = 0;
		new HeadlessHost().Show(platform =>
		{
			platform.OnOpen("tasks", () => again++);
			return new Text("Tasks") { Key = "heading", Style = new Style { FontSize = 22 } };
		});
		Assert.Equal(1, again);
	}

	[Fact]
	public void OnOpen_runs_again_when_a_screen_is_shown_after_another()
	{
		var seen = new List<string>();
		var host = new HeadlessHost();
		host.Show(platform =>
		{
			var id = host.NavDepth <= 1 ? "tasks" : host.NavTop;
			platform.OnOpen(id, () => seen.Add(id));
			return new Text(id) { Key = "heading", Style = new Style { FontSize = 22 } };
		});
		Assert.Equal(["tasks"], seen);
		host.RequestRender();
		Assert.Equal(["tasks"], seen);
		host.Push("detail");
		Assert.Equal(["tasks", "detail"], seen);
		host.Pop();
		Assert.Equal(["tasks", "detail", "tasks"], seen);
		host.RequestRender();
		Assert.Equal(["tasks", "detail", "tasks"], seen);
	}

	[Fact]
	public void An_across_column_places_children_side_by_side()
	{
		var host = new HeadlessHost();
		host.Show(_ => new Column(
		[
			new Text("Left") { Key = "left" },
			new Text("Right") { Key = "right" },
		])
		{
			Key = "row",
			Style = new Style { Across = 1, Gap = 16 },
		});
		Assert.Contains("id=row/left left=0 top=0 width=172", host.Layout, StringComparison.Ordinal);
		Assert.Contains("id=row/right left=188 top=0 width=172", host.Layout, StringComparison.Ordinal);
	}

	[Fact]
	public void A_row_and_a_grid_keep_every_child_inside_its_parent_at_1080_and_font_scale_2()
	{
		var row = new Column(
		[
			new Text("One") { Key = "one", Style = new Style { FontSize = 32, Padding = 16, Grow = 1 } },
			new Text("Two") { Key = "two", Style = new Style { FontSize = 32, Padding = 16, Grow = 1 } },
			new Text("Three") { Key = "three", Style = new Style { FontSize = 32, Padding = 16, Grow = 1 } },
		])
		{
			Key = "row",
			Style = new Style { Across = 1, Gap = 16, Padding = 24 },
		};
		var grid = new Column(
		[
			new Column(
			[
				new Button("A", () => { }, "A") { Key = "a", Style = new Style { Grow = 1 } },
				new Button("B", () => { }, "B") { Key = "b", Style = new Style { Grow = 1 } },
			])
			{
				Key = "r0",
				Style = new Style { Across = 1, Gap = 16 },
			},
			new Column(
			[
				new Button("C", () => { }, "C") { Key = "c", Style = new Style { Grow = 1 } },
				new Button("D", () => { }, "D") { Key = "d", Style = new Style { Grow = 1 } },
			])
			{
				Key = "r1",
				Style = new Style { Across = 1, Gap = 16 },
			},
		])
		{
			Key = "grid",
			Style = new Style { Gap = 20, Padding = 16 },
		};
		var boxes = HeadlessLayout.Measure(new Column([row, grid]) { Key = "page" }, 1080, 2f);
		var byId = new Dictionary<string, HeadlessLayout.Box>(boxes.Count);
		foreach (var box in boxes)
			byId[box.Id] = box;

		void Inside(string parentId, string childId)
		{
			var parent = byId[parentId];
			var child = byId[childId];
			Assert.True(child.Left >= parent.Left, childId);
			Assert.True(child.Top >= parent.Top, childId);
			Assert.True(child.Left + child.Width <= parent.Left + parent.Width, childId);
			Assert.True(child.Top + child.Height <= parent.Top + parent.Height, childId);
		}

		Inside("page", "page/row");
		Inside("page", "page/grid");
		Inside("page/row", "page/row/one");
		Inside("page/row", "page/row/two");
		Inside("page/row", "page/row/three");
		Inside("page/grid", "page/grid/r0");
		Inside("page/grid", "page/grid/r1");
		Inside("page/grid/r0", "page/grid/r0/a");
		Inside("page/grid/r0", "page/grid/r0/b");
		Inside("page/grid/r1", "page/grid/r1/c");
		Inside("page/grid/r1", "page/grid/r1/d");

		var placed = byId["page/row"];
		var one = byId["page/row/one"];
		var two = byId["page/row/two"];
		var three = byId["page/row/three"];
		Assert.Equal(placed.Left + 24, one.Left);
		Assert.Equal(placed.Left + placed.Width - 24, three.Left + three.Width);
		Assert.Equal(one.Left + one.Width + 16, two.Left);
		Assert.Equal(two.Left + two.Width + 16, three.Left);
		Assert.Equal(placed.Width - 48, one.Width + two.Width + three.Width + 32);

		var band = byId["page/grid/r0"];
		var left = byId["page/grid/r0/a"];
		var right = byId["page/grid/r0/b"];
		Assert.Equal(band.Left, left.Left);
		Assert.Equal(band.Left + band.Width, right.Left + right.Width);
		Assert.Equal(left.Left + left.Width + 16, right.Left);
		Assert.Equal(band.Width, left.Width + right.Width + 16);
	}
}
