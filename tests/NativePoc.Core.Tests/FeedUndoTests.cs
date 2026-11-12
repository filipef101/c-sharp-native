using NativePoc.Feed;

namespace NativePoc.Tests;

public sealed class FeedUndoTests
{
	[Fact]
	public void Dismiss_removes_the_row_and_undo_puts_it_back_in_order()
	{
		var state = new FeedState();
		state.Generated.Add(new FeedItem { Id = "0000", Title = "Row 0000" });
		state.Generated.Add(new FeedItem { Id = "0001", Title = "Row 0001" });
		state.Generated.Add(new FeedItem { Id = "0002", Title = "Row 0002" });

		state.Drop(state.Generated[0]);

		Assert.Equal(["0001", "0002"], state.Generated.Select(item => item.Id).ToArray());
		Assert.True(state.CanUndo());
		state.Undo();
		Assert.Equal(["0000", "0001", "0002"], state.Generated.Select(item => item.Id).ToArray());
		Assert.False(state.CanUndo());
	}

	[Fact]
	public void Undo_without_a_dismiss_leaves_the_list()
	{
		var state = new FeedState();
		state.Generated.Add(new FeedItem { Id = "0001", Title = "Row 0001" });
		state.Undo();
		Assert.Equal("0001", state.Generated[0].Id);
	}
}
