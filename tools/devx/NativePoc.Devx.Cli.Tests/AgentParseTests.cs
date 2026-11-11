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
