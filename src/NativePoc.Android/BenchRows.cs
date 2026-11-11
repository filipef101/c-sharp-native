using System.Globalization;

namespace NativePoc;

static class BenchRows
{
	public static void Fill(RecordProgram.Bag bag)
	{
		var rows = new List<string[]>(1000);
		for (var index = 0; index < 1000; index++)
		{
			var id = index.ToString("0000", CultureInfo.InvariantCulture);
			rows.Add(["n" + id, "Item " + id, index % 5 == 0 ? "true" : "false"]);
		}

		bag.ReplaceRows("Items", rows);
	}
}
