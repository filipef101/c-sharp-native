using System.Text;

namespace NativePoc.Tests;

public sealed class ScreenProgramTests
{
	[Fact]
	public void A_program_is_not_an_assembly_and_a_title_edit_keeps_the_name()
	{
		var state = new AppState { Count = 2, Name = "Ada" };
		var platform = new RecordingPlatform();
		var program = Counter(titlePrefix: "Clicks ");
		Assert.Equal((byte)'N', program[0]);
		Assert.NotEqual((byte)'M', program[0]);
		Assert.False(ScreenProgram.IsProgram(Encoding.ASCII.GetBytes("MZ")));

		var bound = ScreenProgram.Bind(program);
		var tree = bound.Render(state, platform);
		var column = Assert.IsType<Column>(tree);
		var title = Assert.IsType<Text>(column.Children[0]);
		Assert.Equal("Clicks 2", title.Value);
		Assert.Equal("Ada", Assert.IsType<TextField>(column.Children[1]).Value);
		Assert.Equal("Ada", state.Name);

		Assert.IsType<Button>(column.Children[2]).OnPress();
		Assert.Equal(3, state.Count);
		Assert.Equal("poc-3", platform.Nonce);
		Assert.Equal(1, platform.Renders);
		Assert.Equal("Ada", state.Name);
	}

	[Fact]
	public void Plain_root_returns_the_title_and_greeting_first_reorders()
	{
		var plain = ScreenProgram.Bind(Counter("Counter ")).Render(
			new AppState { PlainRoot = true, Count = 4, GreetingFirst = true },
			new RecordingPlatform());
		Assert.Equal("Counter 4", Assert.IsType<Text>(plain).Value);

		var flipped = Assert.IsType<Column>(ScreenProgram.Bind(Counter("Counter ")).Render(
			new AppState { GreetingFirst = true, Name = "Bea" },
			new RecordingPlatform()));
		Assert.Equal("Hello Bea", Assert.IsType<Text>(flipped.Children[0]).Value);
		Assert.Equal("Counter 0", Assert.IsType<Text>(flipped.Children[3]).Value);
	}

	[Fact]
	public void A_field_edit_asks_for_another_frame_on_the_same_state()
	{
		var state = new AppState();
		var platform = new RecordingPlatform();
		var column = Assert.IsType<Column>(ScreenProgram.Bind(Counter("Counter ")).Render(state, platform));
		Assert.Equal("Enter a name", Assert.IsType<Text>(column.Children[3]).Value);
		Assert.IsType<TextField>(column.Children[1]).OnChange("Ada");
		Assert.Equal("Ada", state.Name);
		Assert.Equal(1, platform.Renders);
	}

	[Fact]
	public void A_bad_program_is_refused_before_it_runs()
	{
		var writer = new ScreenProgram.Writer();
		writer.PushCount();
		writer.Return();
		var payload = writer.Finish(null);
		Assert.Throws<InvalidOperationException>(() => ScreenProgram.Bind(payload));
	}

	[Fact]
	public void State_text_and_the_checkpoint_round_trip()
	{
		var state = new AppState { Count = 7, Name = "A\nB", GreetingFirst = true };
		var copy = new AppState();
		Assert.True(ScreenSnapshot.TryReadState(ScreenSnapshot.FormatState(state), copy));
		Assert.Equal(7, copy.Count);
		Assert.Equal("A\nB", copy.Name);
		Assert.True(copy.GreetingFirst);
		Assert.False(copy.PlainRoot);

		var again = new AppState();
		StateCheckpoint.ReadInto(StateCheckpoint.Write(copy), again);
		Assert.Equal(copy.Count, again.Count);
		Assert.Equal(copy.Name, again.Name);
		Assert.Equal(copy.GreetingFirst, again.GreetingFirst);
		Assert.Equal(copy.PlainRoot, again.PlainRoot);
	}

	[Fact]
	public void Freshness_distinguishes_a_compiled_screen_from_a_stale_one()
	{
		Assert.Equal(ScreenFreshness.Compiled, ScreenSnapshot.Freshness("source=\ncolumn\n", "abcd"));
		Assert.Equal(ScreenFreshness.Current, ScreenSnapshot.Freshness("source=abcd\ncolumn\n", "abcd"));
		Assert.Equal(ScreenFreshness.Stale, ScreenSnapshot.Freshness("source=ffff\ncolumn\n", "abcd"));
		Assert.Equal(ScreenFreshness.Unreadable, ScreenSnapshot.Freshness("column\n", "abcd"));
	}

	static byte[] Counter(string titlePrefix)
	{
		var writer = new ScreenProgram.Writer();
		var prefix = writer.Intern(titlePrefix);
		var titleKey = writer.Intern("title");
		var nameKey = writer.Intern("name");
		var placeholder = writer.Intern("Name");
		var nameLabel = writer.Intern("Name");
		var saveTitle = writer.Intern("Save");
		var saveLabel = writer.Intern("Save name");
		var saveKey = writer.Intern("save");
		var nonce = writer.Intern("poc-");
		var empty = writer.Intern("Enter a name");
		var hello = writer.Intern("Hello ");
		var greetingKey = writer.Intern("greeting");

		writer.PushString(prefix);
		writer.PushCount();
		writer.Concat();
		writer.MakeText(titleKey);
		writer.Store(0);
		writer.PushPlainRoot();
		var rest = writer.JumpIfFalse();
		writer.Load(0);
		writer.Return();
		writer.Patch(rest, writer.Here);

		writer.PushName();
		writer.PushString(placeholder);
		writer.PushString(nameLabel);
		writer.MakeField(nameKey);
		writer.Store(1);

		writer.PushString(saveTitle);
		writer.PushString(saveLabel);
		writer.MakeButton(saveKey, nonce);
		writer.Store(2);

		writer.PushName();
		writer.IsEmpty();
		var helloJump = writer.JumpIfFalse();
		writer.PushString(empty);
		var greetingJump = writer.Jump();
		writer.Patch(helloJump, writer.Here);
		writer.PushString(hello);
		writer.PushName();
		writer.Concat();
		writer.Patch(greetingJump, writer.Here);
		writer.MakeText(greetingKey);
		writer.Store(3);

		writer.PushGreetingFirst();
		var second = writer.JumpIfFalse();
		writer.Load(3);
		writer.Load(1);
		writer.Load(2);
		writer.Load(0);
		writer.MakeColumn(4);
		writer.Return();
		writer.Patch(second, writer.Here);
		writer.Load(0);
		writer.Load(1);
		writer.Load(2);
		writer.Load(3);
		writer.MakeColumn(4);
		writer.Return();
		return writer.Finish("counter");
	}

	sealed class RecordingPlatform : IPlatform
	{
		public string? Nonce { get; private set; }

		public int Renders { get; private set; }

		public void CopyNonce(string nonce) => Nonce = nonce;

		public void RequestRender() => Renders++;
	}
}
