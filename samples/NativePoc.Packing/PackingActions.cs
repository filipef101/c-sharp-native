namespace NativePoc.Packing;

public interface IPackingHost
{
	void RequestRender();

	INativeServices Native { get; }
}

public static class PackingActions
{
	public static void BeginAdd(PackingState state, IPackingHost host)
	{
		state.EditingId = null;
		state.DraftName = "";
		state.DraftPacked = false;
		state.Route = PackingRoute.Editor;
		NativePoc.StateEpoch.Bump();
		host.RequestRender();
	}

	public static void BeginEdit(PackingState state, IPackingHost host, string id)
	{
		var item = Find(state, id);
		state.EditingId = item.Id;
		state.DraftName = item.Name;
		state.DraftPacked = item.Packed;
		state.Route = PackingRoute.Editor;
		NativePoc.StateEpoch.Bump();
		host.RequestRender();
	}

	public static void Save(PackingState state, IPackingHost host)
	{
		var name = state.DraftName.Trim();
		if (name.Length == 0)
			return;
		if (state.EditingId is null)
		{
			state.Items.Add(new PackingItem("item-" + state.NextNumber, name, state.DraftPacked));
			state.NextNumber++;
		}
		else
		{
			var item = Find(state, state.EditingId);
			item.Name = name;
			item.Packed = state.DraftPacked;
		}
		state.Route = PackingRoute.List;
		NativePoc.StateEpoch.Bump();
		host.RequestRender();
	}

	public static void Back(PackingState state, IPackingHost host)
	{
		state.Route = PackingRoute.List;
		NativePoc.StateEpoch.Bump();
		host.RequestRender();
	}

	public static void Copy(PackingState state, IPackingHost host)
	{
		host.Native.CopyText("packing", state.CopyText());
		host.Native.Pulse();
	}

	static PackingItem Find(PackingState state, string id)
	{
		foreach (var item in state.Items)
		{
			if (item.Id == id)
				return item;
		}
		throw new InvalidOperationException("Packing item was not found.");
	}
}
