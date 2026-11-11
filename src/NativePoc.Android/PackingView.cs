using NativePoc.Packing;

namespace NativePoc;

static class PackingView
{
	public static PackingCheckpoint Capture()
	{
		var state = PackingSession.Shared.State;
		var items = new PackingCheckpointItem[state.Items.Count];
		for (var index = 0; index < state.Items.Count; index++)
		{
			var item = state.Items[index];
			items[index] = new PackingCheckpointItem(item.Id, item.Name, item.Packed);
		}

		return new PackingCheckpoint
		{
			Editor = state.Route == PackingRoute.Editor,
			DraftPacked = state.DraftPacked,
			NextNumber = state.NextNumber,
			EditingId = state.EditingId,
			DraftName = state.DraftName,
			DraftNote = state.DraftNote,
			Items = items,
		};
	}

	public static void Apply(PackingCheckpoint checkpoint)
	{
		ArgumentNullException.ThrowIfNull(checkpoint);
		var state = PackingSession.Shared.State;
		state.Items.Clear();
		foreach (var item in checkpoint.Items)
			state.Items.Add(new PackingItem(item.Id, item.Name, item.Packed));
		state.Route = checkpoint.Editor ? PackingRoute.Editor : PackingRoute.List;
		state.DraftPacked = checkpoint.DraftPacked;
		state.NextNumber = checkpoint.NextNumber;
		state.EditingId = checkpoint.EditingId;
		state.DraftName = checkpoint.DraftName;
		state.DraftNote = checkpoint.DraftNote;
	}
}
