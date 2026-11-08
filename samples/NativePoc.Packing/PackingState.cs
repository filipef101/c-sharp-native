namespace NativePoc.Packing;

public enum PackingRoute
{
	List,
	Editor,
}

public sealed class PackingItem
{
	public PackingItem(string id, string name, bool packed)
	{
		Id = id;
		Name = name;
		Packed = packed;
	}

	public string Id { get; }

	public string Name { get; set; }

	public bool Packed { get; set; }
}

public sealed class PackingState
{
	public List<PackingItem> Items { get; } = new();

	public PackingRoute Route { get; set; } = PackingRoute.List;

	public string? EditingId { get; set; }

	public string DraftName { get; set; } = "";

	public bool DraftPacked { get; set; }

	public int NextNumber { get; set; } = 1;

	public static PackingState Seed()
	{
		var state = new PackingState();
		state.Items.Add(new PackingItem("tent", "Canvas tent", false));
		state.Items.Add(new PackingItem("stove", "Camp stove", true));
		state.Items.Add(new PackingItem("map", "Ridge map", false));
		state.Items.Add(new PackingItem("bladder", "Water bladder", false));
		return state;
	}

	public int PackedCount
	{
		get
		{
			var count = 0;
			foreach (var item in Items)
			{
				if (item.Packed)
					count++;
			}
			return count;
		}
	}

	public string CopyText()
	{
		var lines = new string[Items.Count];
		for (var i = 0; i < Items.Count; i++)
		{
			var item = Items[i];
			lines[i] = item.Packed ? item.Name + " [packed]" : item.Name;
		}
		return string.Join('\n', lines);
	}
}

public sealed class PackingSession
{
	public static PackingSession Shared { get; } = new();

	public PackingState State { get; } = PackingState.Seed();
}

public static class PackingTelemetry
{
	public static int SwitchDeliveries { get; set; }

	public static int FieldDeliveries { get; set; }
}
