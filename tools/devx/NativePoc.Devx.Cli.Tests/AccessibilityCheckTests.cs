using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class AccessibilityCheckTests
{
	[Fact]
	public void Check_reports_a_small_target_an_unlabeled_control_a_reversal_and_a_clipped_line()
	{
		const string xml = """
			<hierarchy>
			  <node class="android.widget.TextView" text="Title" content-desc="Title" clickable="false" heading="true" bounds="[0,0][400,80]" />
			  <node class="android.widget.Button" text="Save" content-desc="Save" clickable="true" bounds="[0,90][40,120]" />
			  <node class="android.widget.ImageView" text="" content-desc="" clickable="true" bounds="[0,140][80,180]" />
			  <node class="android.widget.TextView" text="Later" content-desc="" clickable="false" bounds="[0,10][400,30]" />
			  <node class="android.widget.TextView" text="Body" content-desc="" clickable="false" bounds="[0,200][400,210]" />
			</hierarchy>
			""";
		var report = AccessibilityDump.Check(xml, 160, 2);
		Assert.Contains("violation touch-target", report.Text, StringComparison.Ordinal);
		Assert.Contains("violation unlabeled", report.Text, StringComparison.Ordinal);
		Assert.Contains("violation order", report.Text, StringComparison.Ordinal);
		Assert.Contains("violation clipped", report.Text, StringComparison.Ordinal);
		Assert.Contains("headings=1", report.Text, StringComparison.Ordinal);
		Assert.True(report.Violations >= 4);
	}

	[Fact]
	public void A_labeled_48dp_control_in_order_is_clean()
	{
		const string xml = """
			<hierarchy>
			  <node class="android.widget.TextView" text="Packing" content-desc="Packing" clickable="false" heading="true" bounds="[0,0][400,80]" />
			  <node class="android.widget.Button" text="Add" content-desc="Add item" clickable="true" bounds="[0,90][200,160]" />
			  <node class="android.widget.Switch" text="" content-desc="Packed tent" clickable="true" bounds="[0,170][200,240]" />
			</hierarchy>
			""";
		var report = AccessibilityDump.Check(xml, 160, 1);
		Assert.Equal(0, report.Violations);
		Assert.Contains("violations=0", report.Text, StringComparison.Ordinal);
		Assert.Contains("headings=1", report.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void A_decor_inset_and_a_row_cut_by_the_scroll_edge_are_not_violations()
	{
		const string xml = """
			<hierarchy>
			  <node class="android.widget.ScrollView" text="" content-desc="" clickable="false" bounds="[0,128][1080,2337]" />
			  <node class="android.widget.TextView" text="Packing" content-desc="Packing" clickable="false" heading="true" bounds="[42,170][1038,293]" />
			  <node class="android.widget.Button" text="Edit" content-desc="Edit Canvas tent" clickable="true" bounds="[73,400][1007,541]" />
			  <node class="android.widget.Switch" text="" content-desc="Packed" clickable="true" bounds="[73,2250][1007,2337]" />
			  <node class="android.view.View" text="" content-desc="" clickable="false" bounds="[0,0][1080,128]" />
			</hierarchy>
			""";
		var report = AccessibilityDump.Check(xml, 420, 1);
		Assert.Equal(0, report.Violations);
		Assert.Contains("headings=1", report.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void A11y_check_is_a_flag_on_the_agent_verb()
	{
		var check = AgentLoop.Parse(["agent", "a11y", "--check", "--device", "emulator-5554"]);
		Assert.Equal("a11y", check.Verb);
		Assert.True(check.Check);
		Assert.Equal("emulator-5554", check.Serial);
		Assert.False(AgentLoop.Parse(["agent", "a11y"]).Check);
		Assert.Throws<UsageException>(() => AgentLoop.Parse(["agent", "tree", "--check"]));
	}
}
