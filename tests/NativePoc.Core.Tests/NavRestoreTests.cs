using System.Text;
using NativePoc;

namespace NativePoc.Tests;

public sealed class NavRestoreTests
{
	[Fact]
	public void A_restored_listener_keeps_the_screen_and_the_program()
	{
		var program = Program("packing", "Pack");
		var nav = new ScreenNav();
		nav.Show(program);
		nav.Bag("packing").WriteInt("Count", 4);
		var checkpoint = nav.Checkpoint();
		Assert.Equal("packing", Assert.Single(checkpoint.Stack));
		Assert.NotEmpty(checkpoint.Screens[0].Program);

		var restored = new ScreenNav();
		restored.Restore(checkpoint);
		Assert.True(restored.DropUnresolved());
		var top = restored.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal("packing", top.ScreenId);
		Assert.Equal(program.Length, checkpoint.Screens[0].Program.Length);
		Assert.Equal("Pack", Assert.IsType<Text>(top.Render(new Quiet())).Value);
		Assert.Equal(4, top.State.ReadInt("Count"));
		Assert.Equal(top.SourceHash, (nav.Top() ?? throw new InvalidOperationException("missing")).SourceHash);
	}

	[Fact]
	public void An_empty_checkpoint_does_not_stay_on_a_missing_program()
	{
		var program = Program("counter", "Hello");
		var nav = new ScreenNav();
		nav.ShowRoot(program);
		nav.Restore(new NavCheckpoint());
		Assert.False(nav.DropUnresolved());
		Assert.Null(nav.Top());
		nav.Stage("counter", program);
		nav.ShowRoot("counter");
		var top = nav.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal("counter", top.ScreenId);
		Assert.Equal("Hello", Assert.IsType<Text>(top.Render(new Quiet())).Value);
	}

	[Fact]
	public void A_version_1_checkpoint_still_reads_and_uses_a_staged_program()
	{
		var payload = Version1("counter");
		var cursor = 0;
		Assert.True(NavCheckpoint.TryRead(payload, ref cursor, out var checkpoint));
		Assert.Equal(payload.Length, cursor);
		Assert.Equal("counter", Assert.Single(checkpoint.Stack));
		Assert.Empty(checkpoint.Screens);

		var program = Program("counter", "Hello");
		var nav = new ScreenNav();
		nav.Stage("counter", program);
		nav.Restore(checkpoint);
		Assert.True(nav.DropUnresolved());
		var top = nav.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal("counter", top.ScreenId);
		Assert.Equal("Hello", Assert.IsType<Text>(top.Render(new Quiet())).Value);
	}

	[Fact]
	public void The_state_reply_keeps_nav_beside_packing()
	{
		var state = new AppState { Count = 4, Name = "Ada" };
		var packing = new PackingCheckpoint
		{
			Editor = true,
			DraftPacked = false,
			NextNumber = 3,
			EditingId = "map",
			DraftName = "Trail map",
			Items = [new PackingCheckpointItem("tent", "Canvas tent", true)],
		};
		var nav = new NavCheckpoint { Stack = ["counter"] };
		var text = ScreenSnapshot.FormatState(state)
			+ packing.Format()
			+ "nav=" + Convert.ToBase64String(NavCheckpoint.Write(nav)) + "\n";
		var read = new AppState();
		Assert.True(ScreenSnapshot.TryReadState(text, read));
		Assert.Equal("Ada", read.Name);
		Assert.True(PackingCheckpoint.TryParse(text, out var parsed));
		Assert.Equal("Trail map", parsed.DraftName);
		Assert.True(ScreenSnapshot.TryReadNavLine(text, out var restored));
		Assert.Equal("counter", Assert.Single(restored.Stack));
	}

	[Fact]
	public void The_second_top_reuses_the_bound_program()
	{
		var nav = new ScreenNav();
		nav.ShowRoot(Program("counter", "Hello"));
		var first = nav.Top() ?? throw new InvalidOperationException("missing");
		var second = nav.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal(first.SourceHash, second.SourceHash);
		Assert.Same(first.Render, second.Render);
	}

	static byte[] Program(string id, string text)
	{
		return RecordProgram.Encode(
			[text, "title", "ScreenId", id],
			[
				new RecordProgram.PropDef(RecordProgram.TypeString, 1, 0, false, 0, [], []),
				new RecordProgram.PropDef(RecordProgram.TypeScreen, 2, 3, false, 0, [], []),
			],
			[
				RecordProgram.OpPushStr, 0, 0,
				RecordProgram.OpMakeText, 1, 0,
				RecordProgram.OpReturn,
			],
			id);
	}

	static byte[] Version1(string id)
	{
		using var buffer = new MemoryStream();
		buffer.WriteByte((byte)'N');
		buffer.WriteByte((byte)'A');
		buffer.WriteByte((byte)'V');
		buffer.WriteByte(1);
		buffer.WriteByte(1);
		var bytes = Encoding.UTF8.GetBytes(id);
		buffer.WriteByte((byte)bytes.Length);
		buffer.WriteByte(0);
		buffer.Write(bytes);
		buffer.WriteByte(0);
		return buffer.ToArray();
	}

	sealed class Quiet : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}
}
