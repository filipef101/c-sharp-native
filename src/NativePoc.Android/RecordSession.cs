using NativePoc.Packing;

namespace NativePoc;

static class RecordSession
{
	public static void Prepare(ReadOnlySpan<byte> payload, RecordProgram.Bag bag)
	{
		ArgumentNullException.ThrowIfNull(bag);
		var names = RecordProgram.Names(payload);
		if (Has(names, "Count"))
		{
			var state = AppSession.Shared.State;
			bag.SeedInt("Count", state.Count);
			if (Has(names, "Name"))
				bag.SeedString("Name", state.Name);
			if (Has(names, "GreetingFirst"))
				bag.SeedBool("GreetingFirst", state.GreetingFirst);
			if (Has(names, "PlainRoot"))
				bag.SeedBool("PlainRoot", state.PlainRoot);
		}

		var packing = PackingSession.Shared.State;
		if (Has(names, "Editing"))
			bag.SeedBool("Editing", packing.Route == PackingRoute.Editor);
		if (Has(names, "EditingId"))
			bag.SeedString("EditingId", packing.EditingId ?? "");
		if (Has(names, "DraftName"))
			bag.SeedString("DraftName", packing.DraftName);
		if (Has(names, "DraftNote"))
			bag.SeedString("DraftNote", packing.DraftNote);
		if (Has(names, "DraftPacked"))
			bag.SeedBool("DraftPacked", packing.DraftPacked);
		if (Has(names, "NextNumber"))
			bag.SeedInt("NextNumber", packing.NextNumber);
		foreach (var list in RecordProgram.Lists(payload))
		{
			if (list.Name != "Items" || !Has(list.Fields, "Id") || !Has(list.Fields, "Name") || !Has(list.Fields, "Packed"))
				continue;
			bag.SeedList("Items", Rows(packing, list.Fields));
		}
	}

	public static void Publish(RecordProgram.Bound program)
	{
		var bag = program.State;
		if (program.Has("Count"))
		{
			var state = AppSession.Shared.State;
			state.Count = bag.ReadInt("Count");
			if (program.Has("Name"))
				state.Name = bag.ReadString("Name");
			if (program.Has("GreetingFirst"))
				state.GreetingFirst = bag.ReadBool("GreetingFirst");
			if (program.Has("PlainRoot"))
				state.PlainRoot = bag.ReadBool("PlainRoot");
		}

		if (!program.Has("Items") || !program.Has("Editing"))
			return;
		var packing = PackingSession.Shared.State;
		packing.Route = bag.ReadBool("Editing") ? PackingRoute.Editor : PackingRoute.List;
		if (program.Has("DraftName"))
			packing.DraftName = bag.ReadString("DraftName");
		if (program.Has("DraftNote"))
			packing.DraftNote = bag.ReadString("DraftNote");
		if (program.Has("DraftPacked"))
			packing.DraftPacked = bag.ReadBool("DraftPacked");
		if (program.Has("NextNumber"))
			packing.NextNumber = bag.ReadInt("NextNumber");
		if (program.Has("EditingId"))
		{
			var editing = bag.ReadString("EditingId");
			packing.EditingId = editing.Length == 0 ? null : editing;
		}

		var rows = bag.Rows("Items");
		packing.Items.Clear();
		foreach (var row in rows)
		{
			if (row.Length < 3)
				continue;
			packing.Items.Add(new PackingItem(row[0], row[1], row[2] == "true"));
		}
	}

	static List<string[]> Rows(PackingState state, string[] fields)
	{
		var rows = new List<string[]>(state.Items.Count);
		foreach (var item in state.Items)
		{
			var cells = new string[fields.Length];
			for (var index = 0; index < fields.Length; index++)
			{
				cells[index] = fields[index] switch
				{
					"Id" => item.Id,
					"Name" => item.Name,
					"Packed" => item.Packed ? "true" : "false",
					_ => "",
				};
			}

			rows.Add(cells);
		}

		return rows;
	}

	static bool Has(IReadOnlyList<string> names, string name)
	{
		foreach (var item in names)
		{
			if (item == name)
				return true;
		}

		return false;
	}
}
