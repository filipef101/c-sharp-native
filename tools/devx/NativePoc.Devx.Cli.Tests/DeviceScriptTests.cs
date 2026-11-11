using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class DeviceScriptTests
{
	[Fact]
	public void Counter_and_packing_scripts_parse()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var counter = DeviceScript.Parse(File.ReadAllText(Path.Combine(root, "tests", "device", "counter.app.test")));
		Assert.Equal(DeviceVerb.Screen, counter[0].Verb);
		Assert.Equal("counter", counter[0].Target);
		Assert.Equal(DeviceVerb.Enter, counter[1].Verb);
		Assert.Equal("name", counter[1].Target);
		Assert.Equal("Ada", counter[1].Text);
		Assert.Equal(DeviceVerb.Press, counter[2].Verb);
		Assert.Equal("save", counter[2].Target);
		Assert.Equal("count=1", counter[3].Target);
		Assert.Equal("name=416461", counter[4].Target);
		Assert.Equal("id=\"save\"", counter[5].Target);

		var packing = DeviceScript.Parse(File.ReadAllText(Path.Combine(root, "tests", "device", "packing.app.test")));
		Assert.Equal("packing", packing[0].Target);
		Assert.Equal("scroll/list/tent/packed", packing[1].Target);
		Assert.Equal(DeviceVerb.Press, packing[1].Verb);
		Assert.Contains("74656e74", packing[2].Target, StringComparison.Ordinal);
		Assert.Contains("true", packing[2].Target, StringComparison.Ordinal);
	}

	[Fact]
	public void A_parent_node_stays_in_the_accessibility_summary()
	{
		var xml = """
			<hierarchy>
			<node class="android.widget.ScrollView" content-desc="" text="" bounds="[0,0][1,2]">
			<node class="android.widget.Button" content-desc="Add item" text="Add item" bounds="[0,0][1,1]" />
			</node>
			</hierarchy>
			""";
		var text = AccessibilityDump.Summarize(xml);
		Assert.Contains("android.widget.ScrollView", text, StringComparison.Ordinal);
		Assert.Contains("Add item", text, StringComparison.Ordinal);
	}

	[Fact]
	public void A_step_past_the_script_is_refused()
	{
		var error = Assert.Throws<UsageException>(() => DeviceScript.Parse("click save\n"));
		Assert.Contains("unknown step", error.Message, StringComparison.Ordinal);
		Assert.Throws<UsageException>(() => DeviceScript.Parse("screen ios\n"));
		Assert.Throws<UsageException>(() => DeviceScript.Parse(""));
	}
}
