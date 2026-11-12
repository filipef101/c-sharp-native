namespace NativePoc.Showcase;

public sealed class Thumb
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public string Picture { get; set; } = "";
}

public sealed class StressState
{
	public string Title { get; set; } = "Stress";

	public List<Thumb> Items => Build();

	static List<Thumb> Build()
	{
		var items = new List<Thumb>(1000);
		for (var index = 0; index < 1000; index++)
		{
			var id = index.ToString("d4", System.Globalization.CultureInfo.InvariantCulture);
			items.Add(new Thumb
			{
				Id = "r" + id,
				Name = "Row " + id,
				Picture = "gen:" + (index % 8).ToString("d2", System.Globalization.CultureInfo.InvariantCulture),
			});
		}

		return items;
	}
}

public static class StressScreen
{
	public const string ScreenId = "stress";

	public static Element Render(StressState state, IPlatform platform)
	{
		var rows = new List<Element>();
		rows.Add(new Text(state.Title) { Key = "heading", Style = new Style { FontSize = 32, Weight = FontWeight.Bold } });
		foreach (var item in state.Items)
		{
			rows.Add(new Column(
			[
				new Text(item.Name) { Key = "name", Style = new Style { FontSize = 16 } },
				new Image(item.Picture) { Key = "thumb", Description = item.Name, Side = 48 },
			])
			{
				Key = item.Id,
			});
		}

		return new ListView(rows) { Key = "stress" };
	}
}
