namespace NativePoc.Trips;

public sealed record Trip
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";
}

public sealed class TripListState
{
	public string Weather { get; set; } = "loading";

	public bool Dark { get; set; }

	public string OpenId { get; set; } = "";

	public string Name { get; set; } = "";

	public List<Trip> Trips { get; set; } = new List<Trip>()
	{
		new Trip { Id = "coast", Name = "Coast road" },
		new Trip { Id = "ridge", Name = "Ridge night" },
		new Trip { Id = "lake", Name = "Lake loop" },
	};
}

public static class TripsScreen
{
	public const string ScreenId = "trips";

	public static Element Render(TripListState state, IPlatform platform)
	{
		platform.Effect("Weather", "weather");
		var rows = new List<Element>();
		rows.Add(new Text(L.Get("Trips")) { Key = "title", Style = new Style { FontSize = 22, Weight = FontWeight.Bold } });
		rows.Add(new Text(L.Plural($"{state.Trips.Count}", L.Get("1 trip"), L.Get("{0} trips"))) { Key = "count" });
		rows.Add(new Text(L.Get(state.Weather)) { Key = "weather" });
		rows.Add(new Text(state.Dark ? L.Get("Night") : L.Get("Day")) { Key = "mode" });
		foreach (var item in state.Trips)
		{
			rows.Add(new Column(
			[
				new Text(L.Get(item.Name)) { Key = "name" },
			])
			{
				Key = item.Id,
				OnTap = () =>
				{
					state.OpenId = item.Id;
					state.Name = item.Name;
					Storage.Set(platform, "trip-id", state.OpenId);
					Storage.Set(platform, "trip-name", state.Name);
					platform.Push("trip");
					platform.RequestRender();
				},
			});
		}

		rows.Add(new Switch(
			state.Dark,
			value =>
			{
				state.Dark = value;
				Theme.Mode(platform, state.Dark);
				platform.RequestRender();
			},
			L.Get("Dark"))
		{ Key = "dark" });
		rows.Add(new Button(L.Get("Back"), () => { platform.Pop(); platform.RequestRender(); }, L.Get("Back")) { Key = "back" });
		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } }) { Key = "scroll" };
	}
}
