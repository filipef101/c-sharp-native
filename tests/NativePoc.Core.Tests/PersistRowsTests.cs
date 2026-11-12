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
		});
		Assert.True(PersistRows.TryDecode(encoded, out var cells));
		Assert.Equal(new[] { "1", "Buy\nmilk", "false" }, cells[0]);
		Assert.Equal(new[] { "2", "Sweep", "true" }, cells[1]);
		Assert.Equal(encoded, PersistRows.Encode(cells));
		var restored = new List<TaskRow> { new TaskRow { Id = "stale", Title = "stale", Done = true } };
		PersistRows.Fill(restored, encoded);
		Assert.Equal(2, restored.Count);
		Assert.Equal("Buy\nmilk", restored[0].Title);
		Assert.False(restored[0].Done);
		Assert.True(restored[1].Done);
		var kept = new List<TaskRow> { new TaskRow { Id = "keep", Title = "keep" } };
		PersistRows.Fill(kept, "ok");
		Assert.Equal("keep", Assert.Single(kept).Title);
	}
}
