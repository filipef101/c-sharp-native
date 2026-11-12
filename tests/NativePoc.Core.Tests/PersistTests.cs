namespace NativePoc.Tests;

public sealed class PersistTests
{
	[Fact]
	public void A_record_round_trips_without_a_serializer()
	{
		var encoded = PersistCodec.Encode(new PersistRecord("Pier", "line\n2", "Friday"));
		Assert.True(PersistCodec.TryDecode(encoded, out var record));
		Assert.Equal("Pier", record.Id);
		Assert.Equal("line\n2", record.Title);
		Assert.Equal("Friday", record.Note);
		Assert.False(PersistCodec.TryDecode("nope", out _));
		Assert.False(PersistCodec.TryDecode(encoded + "x", out _));
	}

	[Fact]
	public void Touch_text_heading_and_list_rules_are_the_shared_floor()
	{
		Assert.Equal(48, HostPolicy.TouchFloor(Gestures.LongPress));
		Assert.Equal(0, HostPolicy.TouchFloor(0));
		Assert.True(HostPolicy.TextIsHidden(""));
		Assert.False(HostPolicy.TextIsHidden("Pier"));
		Assert.True(HostPolicy.IsHeading(22));
		Assert.False(HostPolicy.IsHeading(18));
		Assert.Equal("UITextView", HostPolicy.FieldView(3, Keyboard.Text));
		Assert.Equal("UITextField", HostPolicy.FieldView(3, Keyboard.Password));
		Assert.True(HostPolicy.RemoveAllowed(ElementKind.Scroll));
		Assert.True(HostPolicy.InsertHonorsIndex(ElementKind.Scroll));
		var rows = new Element[] { new Column([new Text("Save name")]), new Text("Two") };
		Assert.Equal("Save name", HostPolicy.RowText(rows[0]));
		Assert.Equal("count=2", HostPolicy.ListSummary(rows));
	}
}
