using NativePoc;

namespace NativePoc.Packing.Tests;

public sealed class PackingTests
{
	[Fact]
	public void Seed_renders_the_list_and_one_packed_item()
	{
		var harness = new PackingHarness();

		Assert.False(harness.State.Editing);
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
		Assert.True(harness.State.Editing);
		Assert.Equal("New item", harness.Text("title"));
		harness.Type("Lantern");
		harness.SetEditorPacked(true);
		harness.Press("save");

		Assert.False(harness.State.Editing);
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

		Assert.False(harness.State.Editing);
		Assert.Equal("Ridge map", harness.State.Items[2].Name);
	}

	[Fact]
	public void Empty_save_stays_on_the_editor()
	{
		var harness = new PackingHarness();
		harness.Press("add");
		harness.Type("   ");
		harness.Press("save");

		Assert.True(harness.State.Editing);
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
		Assert.True(harness.State.Editing);

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
	public void Stable_ids_drive_a_row_and_the_editor()
	{
		var harness = new PackingHarness();
		var tree = harness.Reconciler.Current ?? throw new InvalidOperationException("The list is not mounted.");
		Assert.Equal("pressed scroll/list/tent/packed", ElementIds.Invoke(tree, "scroll/list/tent/packed", null));
		Assert.True(harness.State.Items[0].Packed);
		Assert.Equal("pressed scroll/list/tent/edit", ElementIds.Invoke(harness.Reconciler.Current!, "scroll/list/tent/edit", null));
		Assert.True(harness.State.Editing);
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
