using NativePoc;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ScreenSessionTests
{
	[Fact]
	public void The_counter_script_runs_without_a_device()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var source = File.ReadAllText(Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs"));
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Contains("id=\"save\"", session.Tree(), StringComparison.Ordinal);
		Assert.Equal("entered name", session.Enter("name", "Ada"));
		Assert.Equal("pressed save", session.Tap("save"));
		Assert.Contains("Count=1", session.State(), StringComparison.Ordinal);
		Assert.Contains("Name=416461", session.State(), StringComparison.Ordinal);
		Assert.Contains("id=\"save\"", session.Tree(), StringComparison.Ordinal);
		Assert.Contains("value=\"Ada\"", session.Tree(), StringComparison.Ordinal);
		Assert.Equal("poc-1", Assert.Single(session.Copies));
	}

	[Fact]
	public void The_packing_script_toggles_and_swipes_without_a_device()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var source = File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Packing", "PackingScreen.cs"));
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Contains("id=\"scroll/list/tent/packed\"", session.Tree(), StringComparison.Ordinal);
		Assert.Contains("value=false", session.Tree(), StringComparison.Ordinal);
		Assert.Equal("pressed scroll/list/tent/packed", session.Tap("scroll/list/tent/packed"));
		Assert.Contains("id=\"scroll/list/tent/packed\"", session.Tree(), StringComparison.Ordinal);
		Assert.Contains("value=true", session.Tree(), StringComparison.Ordinal);
		var shape = Assert.Single(RecordProgram.Lists(lowered.Program!), item => item.Name == "Items");
		var packedAt = Array.IndexOf(shape.Fields, "Packed");
		var tent = Assert.Single(session.Bag.Rows("Items"), row => row.Length > 0 && row[0] == "tent");
		Assert.Equal("true", tent[packedAt]);
		Assert.Equal("swipe scroll/list/tent", session.Swipe("scroll/list/tent"));
		Assert.DoesNotContain(session.Bag.Rows("Items"), row => row.Length > 0 && row[0] == "tent");
		Assert.DoesNotContain("id=\"scroll/list/tent\"", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void The_clock_steps_an_armed_int_and_a_motion()
	{
		const string source = """
			namespace NativePoc.Clock;
			public sealed class ClockState
			{
				public int Tick { get; set; }
			}
			public static class ClockScreen
			{
				public static Element Render(ClockState state, IPlatform platform)
				{
					platform.Every("Tick");
					return new Text("t" + state.Tick)
					{
						Key = "label",
						Motion = new Motion { FromOpacity = 0, Opacity = 100, Duration = 200 },
					};
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Equal(0, session.Opacity("label"));
		Assert.Contains("value=\"t0\"", session.Tree(), StringComparison.Ordinal);
		Assert.Equal(1, session.Step());
		Assert.Equal(50, session.Opacity("label"));
		Assert.Contains("Tick=1", session.State(), StringComparison.Ordinal);
		Assert.Contains("value=\"t1\"", session.Tree(), StringComparison.Ordinal);
		Assert.Equal(2, session.Step());
		Assert.Equal(100, session.Opacity("label"));
		Assert.Contains("Tick=2", session.State(), StringComparison.Ordinal);
	}
}
