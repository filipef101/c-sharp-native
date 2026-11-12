namespace NativePoc.Tests;

public sealed class PersistRowsTests
{
	sealed class TaskRow
	{
		public string Id { get; set; } = "";

		public string Title { get; set; } = "";

		public bool Done { get; set; }
	}

	[Fact]
	public void A_list_round_trips_including_a_newline_in_the_title()
	{
		var encoded = PersistRows.Encode(new List<TaskRow>
		{
			new TaskRow { Id = "1", Title = "Buy\nmilk", Done = false },
			new TaskRow { Id = "2", Title = "Sweep", Done = true },
		}, static item => new[] { item.Id, item.Title, item.Done ? "true" : "false" });
		Assert.True(PersistRows.TryDecode(encoded, out var cells));
		Assert.Equal(new[] { "1", "Buy\nmilk", "false" }, cells[0]);
		Assert.Equal(new[] { "2", "Sweep", "true" }, cells[1]);
		Assert.Equal(encoded, PersistRows.Encode(cells));
		var restored = new List<TaskRow> { new TaskRow { Id = "stale", Title = "stale", Done = true } };
		PersistRows.Fill(restored, encoded, static line => new TaskRow
		{
			Id = line[0],
			Title = line[1],
			Done = line[2] == "true",
		});
		Assert.Equal(2, restored.Count);
		Assert.Equal("Buy\nmilk", restored[0].Title);
		Assert.False(restored[0].Done);
		Assert.True(restored[1].Done);
		var kept = new List<TaskRow> { new TaskRow { Id = "keep", Title = "keep" } };
		PersistRows.Fill(kept, "ok", static line => new TaskRow { Id = line[0], Title = line[1] });
		Assert.Equal("keep", Assert.Single(kept).Title);
	}

	[Fact]
	public async Task A_record_of_any_width_round_trips_and_the_three_field_record_still_does()
	{
		var encoded = PersistRows.EncodeFields("9", "Sweep\nfloor", "true", "kitchen");
		Assert.True(PersistRows.TryDecodeFields(encoded, out var fields));
		Assert.Equal(new[] { "9", "Sweep\nfloor", "true", "kitchen" }, fields);
		Assert.False(PersistRows.TryDecode(encoded, out _));
		Assert.False(PersistRows.TryDecodeFields(PersistCodec.Encode(new PersistRecord("a", "b", "c")), out _));
		var note = PersistCodec.Encode(new PersistRecord("a", "Buy\nmilk", "c"));
		Assert.True(PersistCodec.TryDecode(note, out var record));
		Assert.Equal("Buy\nmilk", record.Title);
		var host = new HeadlessHost();
		Assert.Equal("ok", await Persist.PutFields(host, "place", "9", "Sweep"));
		var call = Assert.Single(host.Calls);
		Assert.StartsWith("persist.Put place ", call, StringComparison.Ordinal);
		Assert.True(PersistRows.TryDecodeFields(call["persist.Put place ".Length..], out var stored));
		Assert.Equal(new[] { "9", "Sweep" }, stored);
	}
}
