using System.Text;
using NativePoc;

namespace NativePoc.Tests;

public sealed class NavRestoreTests
{
	[Fact]
	public void A_catalog_of_screens_does_not_deepen_a_one_screen_launch()
	{
		var nav = new ScreenNav { LaunchRoot = "notes" };
		nav.Show(Program("feed", "Feed"));
		nav.Show(Program("visit", "Visit"));
		nav.Show(Program("notes", "Notes"));
		nav.Show(Program("stop", "Stop"));
		Assert.Equal(1, nav.Depth);
		Assert.Equal("notes", nav.TopId());
		Assert.True(nav.Push("visit"));
		Assert.Equal(2, nav.Depth);
		Assert.Equal("visit", nav.TopId());
		var restored = new ScreenNav();
		restored.Restore(nav.Checkpoint());
		Assert.Equal(2, restored.Depth);
		Assert.Equal("visit", restored.TopId());
		Assert.Equal("stack 0 notes\nstack 1 visit\n", restored.Format());
		restored.Show(Program("visit", "Visit again"));
		Assert.Equal(2, restored.Depth);
		Assert.Equal("stack 0 notes\nstack 1 visit\n", restored.Format());
		var top = restored.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal("Visit again", Assert.IsType<Text>(top.Render(new Quiet())).Value);
		restored.Show(Program("notes", "Notes again"));
		Assert.Equal("visit", restored.TopId());
		Assert.Equal(2, restored.Depth);
	}

	[Fact]
	public void Pop_keeps_each_screen_bag_and_a_restore_keeps_the_stack()
	{
		var nav = new ScreenNav();
		nav.Show(Program("counter", "Counter"));
		nav.Show(Program("notes", "Notes"));
		Assert.Equal(1, nav.Depth);
		Assert.True(nav.Push("notes"));
		nav.Bag("counter").WriteInt("Count", 3);
		nav.Bag("notes").WriteString("Title", "Ada");
		Assert.Equal(2, nav.Depth);
		var checkpoint = nav.Checkpoint();
		var restored = new ScreenNav();
		restored.Restore(checkpoint);
		Assert.Equal(2, restored.Depth);
		Assert.Equal("notes", restored.TopId());
		Assert.Equal(3, restored.Bag("counter").ReadInt("Count"));
		Assert.Equal("Ada", restored.Bag("notes").ReadString("Title"));
		Assert.True(nav.Pop());
		Assert.Equal(1, nav.Depth);
		Assert.Equal("counter", nav.TopId());
		Assert.Equal(3, nav.Bag("counter").ReadInt("Count"));
		Assert.Equal("Ada", nav.Bag("notes").ReadString("Title"));
	}

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

	[Fact]
	public void A_show_updates_a_screen_already_on_the_stack_and_leaves_the_order()
	{
		var nav = new ScreenNav();
		nav.Show(Program("counter", "Hello"));
		nav.Show(Program("packing", "Pack"));
		nav.Show(Program("notes", "Notes"));
		Assert.Equal(1, nav.Depth);
		Assert.True(nav.Push("packing"));
		Assert.True(nav.Push("notes"));
		Assert.Equal("stack 0 counter\nstack 1 packing\nstack 2 notes\n", nav.Format());
		nav.Show(Program("packing", "Pack again"));
		Assert.Equal("stack 0 counter\nstack 1 packing\nstack 2 notes\n", nav.Format());
		Assert.Equal("notes", nav.TopId());
		nav.Show(Program("notes", "Notes again"));
		Assert.Equal("stack 0 counter\nstack 1 packing\nstack 2 notes\n", nav.Format());
		var top = nav.Top() ?? throw new InvalidOperationException("missing");
		Assert.Equal("Notes again", Assert.IsType<Text>(top.Render.Invoke(new Quiet())).Value);
	}

	[Fact]
	public void A_push_of_an_existing_screen_moves_that_frame_to_the_top()
	{
		var nav = new ScreenNav();
		nav.Show(Program("counter", "Hello"));
		nav.Show(Program("packing", "Pack"));
		nav.Show(Program("notes", "Notes"));
		Assert.True(nav.Push("packing"));
		Assert.True(nav.Push("notes"));
		Assert.True(nav.Push("packing"));
		Assert.Equal("stack 0 counter\nstack 1 notes\nstack 2 packing\n", nav.Format());
		Assert.True(nav.Push("packing"));
		Assert.Equal("stack 0 counter\nstack 1 notes\nstack 2 packing\n", nav.Format());
	}

	[Fact]
	public void A_compiled_screen_with_no_program_can_be_pushed()
	{
		var nav = new ScreenNav();
		nav.Allow("notes");
		Assert.True(nav.Push("notes"));
		Assert.Equal("notes", nav.TopId());
		Assert.False(nav.HasProgram("notes"));
		Assert.Null(nav.Top());
		Assert.True(nav.DropUnresolved());
		Assert.Equal("notes", nav.TopId());
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
