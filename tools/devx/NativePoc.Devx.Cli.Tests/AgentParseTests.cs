using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class AgentParseTests
{
	[Fact]
	public void Tree_and_state_take_an_optional_device()
	{
		var tree = AgentLoop.Parse(["agent", "tree"]);
		Assert.Equal("tree", tree.Verb);
		Assert.Null(tree.Serial);

		var state = AgentLoop.Parse(["agent", "--device", "emulator-5554", "state"]);
		Assert.Equal("state", state.Verb);
		Assert.Equal("emulator-5554", state.Serial);
	}

	[Fact]
	public void Tap_type_and_screenshot_take_their_arguments()
	{
		var shot = AgentLoop.Parse(["agent", "screenshot", "out.png"]);
		Assert.Equal("out.png", shot.Path);
		var tap = AgentLoop.Parse(["agent", "tap", "12", "40"]);
		Assert.Equal(12, tap.X);
		Assert.Equal(40, tap.Y);
		var typed = AgentLoop.Parse(["agent", "type", "Ada", "Bea"]);
		Assert.Equal("Ada Bea", typed.Text);
		var press = AgentLoop.Parse(["agent", "press", "list/tent/packed"]);
		Assert.Equal("list/tent/packed", press.Path);
		var swipe = AgentLoop.Parse(["agent", "swipe", "scroll/list/tent"]);
		Assert.Equal("swipe", swipe.Verb);
		Assert.Equal("scroll/list/tent", swipe.Path);
		var enter = AgentLoop.Parse(["agent", "enter", "editor/name", "Trail", "map"]);
		Assert.Equal("editor/name", enter.Path);
		Assert.Equal("Trail map", enter.Text);
	}

	[Fact]
	public void A_stale_drive_names_the_rebuild()
	{
		var stale = AgentLoop.Refusal(ScreenFreshness.Stale, "abcd");
		var compiled = AgentLoop.Refusal(ScreenFreshness.Compiled, "abcd");
		Assert.Contains("tools/devx/cnative run android --once", stale);
		Assert.Contains("abcd", stale);
		Assert.Contains(AgentLoop.RebuildWith, compiled);
	}

	[Theory]
	[InlineData("agent")]
	[InlineData("agent", "tap", "x")]
	[InlineData("agent", "nope")]
	public void A_bad_agent_command_is_a_usage_error(params string[] args)
	{
		Assert.Throws<UsageException>(() => AgentLoop.Parse(args));
	}
}
