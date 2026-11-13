namespace NativePoc.Feed;

public sealed class FeedItem
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";
}

public sealed class FeedState
{
	public string Phase { get; set; } = "idle";

	public string Error { get; set; } = "";

	public string Json { get; set; } = "";

	public string Mark { get; set; } = "";

	public string Query { get; set; } = "";

	public bool Loading { get; set; }

	public List<FeedItem> Items { get; set; } = new List<FeedItem>();

	public List<FeedItem> Generated { get; set; } = new List<FeedItem>();

	FeedItem? _undoItem;

	int _undoAt = -1;

	public bool CanUndo() => _undoItem is not null;

	public void Drop(FeedItem item)
	{
		if (item is null)
			return;
		for (var index = Generated.Count - 1; index >= 0; index--)
		{
			if (Generated[index].Id != item.Id)
				continue;
			_undoItem = Generated[index];
			_undoAt = index;
			Generated.RemoveAt(index);
			return;
		}
	}

	public void Undo()
	{
		if (_undoItem is null)
			return;
		var at = _undoAt;
		if (at < 0 || at > Generated.Count)
			at = Generated.Count;
		Generated.Insert(at, _undoItem);
		_undoItem = null;
		_undoAt = -1;
	}
}
