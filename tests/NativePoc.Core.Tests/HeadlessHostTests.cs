namespace NativePoc.Tests;

public sealed class HeadlessHostTests
{
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
}
