using NativePoc;

namespace NativePoc.Packing.Tests;

public sealed class PackingTests
{
	[Fact]
	public void Seed_renders_the_list_and_one_packed_item()
	{
		var harness = new PackingHarness();

		Assert.Equal(PackingRoute.List, harness.State.Route);
		Assert.Equal(4, harness.State.Items.Count);
		Assert.Equal(1, harness.State.PackedCount);
		Assert.Equal("Packing", harness.Text("title"));
		Assert.Equal("1 of 4 packed", harness.Text("progress"));
		Assert.False(harness.SwitchChecked("tent", "packed"));
		Assert.True(harness.SwitchChecked("stove", "packed"));
	}

	[Fact]
	public void Checking_an_item_updates_state_and_counts_one_delivery()
	{
		var harness = new PackingHarness();
		var before = PackingTelemetry.SwitchDeliveries;

		harness.SetPacked("tent", true);

		Assert.True(harness.State.Items[0].Packed);
		Assert.Equal(2, harness.State.PackedCount);
		Assert.Equal("2 of 4 packed", harness.Text("progress"));
		Assert.Equal(before + 1, PackingTelemetry.SwitchDeliveries);
		Assert.Single(harness.Services.Pulses);
	}

	[Fact]
	public void Add_and_edit_round_trip_through_the_form()
	{
		var harness = new PackingHarness();

		harness.Press("add");
		Assert.Equal(PackingRoute.Editor, harness.State.Route);
		Assert.Equal("New item", harness.Text("title"));
		harness.Type("Lantern");
		harness.SetEditorPacked(true);
		harness.Press("save");

		Assert.Equal(PackingRoute.List, harness.State.Route);
		Assert.Equal("Lantern", harness.State.Items[4].Name);
		Assert.True(harness.State.Items[4].Packed);
		Assert.Equal("2 of 5 packed", harness.Text("progress"));

		harness.PressRow("tent", "edit");
		Assert.Equal("Canvas tent", harness.State.DraftName);
		harness.Type("Canvas tent X");
		harness.Press("save");

		Assert.Equal("Canvas tent X", harness.State.Items[0].Name);
		Assert.Equal("Canvas tent X", harness.TextAt("tent", "name"));
	}

	[Fact]
	public void System_back_returns_from_the_editor_and_finishes_the_list()
	{
		Assert.Equal(SystemBack.ReturnToList, PackingNavigation.SystemBackFor(PackingRoute.Editor));
		Assert.Equal(SystemBack.FinishActivity, PackingNavigation.SystemBackFor(PackingRoute.List));
	}

	[Fact]
	public void Back_leaves_the_draft_unsaved()
	{
		var harness = new PackingHarness();
		harness.PressRow("map", "edit");
		harness.Type("Discarded");
		harness.Press("back");

		Assert.Equal(PackingRoute.List, harness.State.Route);
		Assert.Equal("Ridge map", harness.State.Items[2].Name);
	}

	[Fact]
	public void Empty_save_stays_on_the_editor()
	{
		var harness = new PackingHarness();
		harness.Press("add");
		harness.Type("   ");
		harness.Press("save");

		Assert.Equal(PackingRoute.Editor, harness.State.Route);
		Assert.Equal(4, harness.State.Items.Count);
	}

	[Fact]
	public void Navigation_unmounts_the_list_and_a_remount_has_one_switch_listener()
	{
		var harness = new PackingHarness();
		var first = harness.RowSwitch("tent");
		Assert.Equal(1, harness.Host.Listeners(first));

		harness.PressRow("tent", "edit");

		Assert.True(first.Disposed);
		Assert.Equal(0, harness.Host.Listeners(first));
		Assert.Equal(PackingRoute.Editor, harness.State.Route);

		harness.Press("back");
		var second = harness.RowSwitch("tent");
		Assert.NotEqual(first.Identity, second.Identity);
		Assert.Equal(1, harness.Host.Listeners(second));

		var before = PackingTelemetry.SwitchDeliveries;
		harness.Host.Fire(second);
		Assert.Equal(before + 1, PackingTelemetry.SwitchDeliveries);
		Assert.True(harness.State.Items[0].Packed);
	}

	[Fact]
	public void Repeated_editor_visits_do_not_stack_field_deliveries()
	{
		var harness = new PackingHarness();
		harness.Press("add");
		var first = harness.Field();
		harness.Host.FireText(first, "A");
		Assert.Equal("A", harness.State.DraftName);
		var afterFirst = PackingTelemetry.FieldDeliveries;

		harness.Press("back");
		harness.Press("add");
		var second = harness.Field();
		Assert.True(first.Disposed);
		Assert.NotEqual(first.Identity, second.Identity);
		Assert.Equal(1, harness.Host.Listeners(second));

		harness.Host.FireText(second, "B");
		Assert.Equal("B", harness.State.DraftName);
		Assert.Equal(afterFirst + 1, PackingTelemetry.FieldDeliveries);
	}

	[Fact]
	public void Copy_text_is_one_line_per_item_with_a_packed_mark()
	{
		var state = PackingState.Seed();
		state.Items.Add(new PackingItem("lantern", "Lantern", false));
		var text = state.CopyText();

		Assert.Equal(
			"Canvas tent\nCamp stove [packed]\nRidge map\nWater bladder\nLantern",
			text);
		Assert.Equal(63, text.Length);
	}

	[Fact]
	public void Copy_sends_the_seeded_list_through_the_native_service()
	{
		var harness = new PackingHarness();

		harness.Press("copy");

		Assert.Equal("packing", harness.Services.Label);
		Assert.Contains("Camp stove [packed]", harness.Services.Text);
		Assert.Contains("Canvas tent", harness.Services.Text);
		Assert.Single(harness.Services.Pulses);
	}

	[Fact]
	public void The_static_session_is_the_process_store_and_seed_is_a_new_process()
	{
		var kept = PackingState.Seed();
		kept.Items[0].Name = "Kept tent";
		var recreated = kept;
		var restarted = PackingState.Seed();

		Assert.Same(PackingSession.Shared, PackingSession.Shared);
		Assert.Same(kept, recreated);
		Assert.Equal("Kept tent", recreated.Items[0].Name);
		Assert.NotSame(kept, restarted);
		Assert.Equal("Canvas tent", restarted.Items[0].Name);
		Assert.Equal("Canvas tent", PackingSession.Shared.State.Items[0].Name);
	}

	[Fact]
	public void Release_disposes_the_mounted_switches()
	{
		var harness = new PackingHarness();
		var toggle = harness.RowSwitch("stove");

		harness.Reconciler.Release();

		Assert.Null(harness.Reconciler.Root);
		Assert.True(toggle.Disposed);
		Assert.Equal(0, harness.Host.Listeners(toggle));
	}

	[Fact]
	public void The_program_matches_the_packing_screen_and_its_actions()
	{
		var screen = new PackingHarness();
		var program = new PackingHarness();
		var bound = ScreenProgram.BindPacking(PackingProgram.Bytes);
		Assert.Equal((byte)'N', PackingProgram.Bytes[0]);
		Assert.Equal((byte)1, PackingProgram.Bytes[5]);
		Assert.Equal(Describe(PackingScreen.Render(screen.State, screen)), Describe(bound.Render(new PackingProgramAdapter(program.State, program))));

		Press(bound, program, "scroll/list/tent/packed");
		screen.SetPacked("tent", true);
		Assert.Equal(Describe(PackingScreen.Render(screen.State, screen)), Describe(bound.Render(new PackingProgramAdapter(program.State, program))));
		Assert.True(program.State.Items[0].Packed);

		Press(bound, program, "scroll/list/tent/edit");
		screen.PressRow("tent", "edit");
		Assert.Equal(Describe(PackingScreen.Render(screen.State, screen)), Describe(bound.Render(new PackingProgramAdapter(program.State, program))));
		Enter(bound, program, "editor/name", "Canvas tent X");
		screen.Type("Canvas tent X");
		Press(bound, program, "editor/save");
		screen.Press("save");
		Assert.Equal("Canvas tent X", program.State.Items[0].Name);
		Assert.Equal(Describe(PackingScreen.Render(screen.State, screen)), Describe(bound.Render(new PackingProgramAdapter(program.State, program))));

		Press(bound, program, "scroll/list/add");
		Enter(bound, program, "editor/name", "Lantern");
		Press(bound, program, "editor/packed");
		Press(bound, program, "editor/save");
		Assert.Equal("Lantern", program.State.Items[4].Name);
		Assert.True(program.State.Items[4].Packed);
		Assert.Equal(PackingRoute.List, program.State.Route);
	}

	static void Press(ScreenProgram.PackingBound bound, PackingHarness harness, string id)
	{
		var tree = bound.Render(new PackingProgramAdapter(harness.State, harness));
		ElementIds.Invoke(tree, id, null);
	}

	static void Enter(ScreenProgram.PackingBound bound, PackingHarness harness, string id, string text)
	{
		var tree = bound.Render(new PackingProgramAdapter(harness.State, harness));
		ElementIds.Invoke(tree, id, text);
	}

	static string Describe(Element element)
	{
		var builder = new System.Text.StringBuilder();
		Describe(element, builder);
		return builder.ToString();
	}

	static void Describe(Element element, System.Text.StringBuilder builder)
	{
		builder.Append(element.Key ?? "").Append(':');
		switch (element)
		{
			case Column column:
				builder.Append("column");
				foreach (var child in column.Children)
				{
					builder.Append('{');
					Describe(child, builder);
					builder.Append('}');
				}
				return;
			case Text text:
				builder.Append("text=").Append(text.Value);
				return;
			case Button button:
				builder.Append("button=").Append(button.Title).Append('/').Append(button.AccessibilityLabel);
				return;
			case TextField field:
				builder.Append("field=").Append(field.Value).Append('/').Append(field.Placeholder).Append('/').Append(field.AccessibilityLabel);
				return;
			case Switch toggle:
				builder.Append("switch=").Append(toggle.Value ? "1" : "0").Append('/').Append(toggle.AccessibilityLabel);
				return;
			case ScrollView scroll:
				builder.Append("scroll");
				builder.Append('{');
				Describe(scroll.Child, builder);
				builder.Append('}');
				return;
			default:
				throw new InvalidOperationException("Unknown element.");
		}
	}

	[Fact]
	public void Stable_ids_drive_a_row_and_the_editor()
	{
		var harness = new PackingHarness();
		var tree = harness.Reconciler.Current ?? throw new InvalidOperationException("The list is not mounted.");
		Assert.Equal("pressed scroll/list/tent/packed", ElementIds.Invoke(tree, "scroll/list/tent/packed", null));
		Assert.True(harness.State.Items[0].Packed);
		Assert.Equal("pressed scroll/list/tent/edit", ElementIds.Invoke(harness.Reconciler.Current!, "scroll/list/tent/edit", null));
		Assert.Equal(PackingRoute.Editor, harness.State.Route);
		Assert.Equal("entered editor/name", ElementIds.Invoke(harness.Reconciler.Current!, "editor/name", "Canvas tent X"));
		Assert.Equal("pressed editor/save", ElementIds.Invoke(harness.Reconciler.Current!, "editor/save", null));
		Assert.Equal("Canvas tent X", harness.State.Items[0].Name);
	}

	[Fact]
	public void A_switch_value_change_patches_in_place()
	{
		var harness = new PackingHarness();
		var toggle = harness.RowSwitch("tent");

		harness.SetPacked("tent", true);

		Assert.Same(toggle, harness.RowSwitch("tent"));
		Assert.True(toggle.Checked);
		Assert.False(toggle.Disposed);
	}
}
