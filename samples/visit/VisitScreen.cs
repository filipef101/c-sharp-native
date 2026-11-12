namespace NativePoc.Visit;

public sealed class VisitItem
{
	public string Id { get; set; } = "";

	public string Place { get; set; } = "";

	public string When { get; set; } = "";
}

public sealed class VisitState
{
	public string Place { get; set; } = "";

	public string When { get; set; } = "";

	public string Error { get; set; } = "";

	public string Fix { get; set; } = "idle";

	public string Body { get; set; } = "";

	public List<VisitItem> Stops { get; set; } = new List<VisitItem>();
}

public static class VisitScreen
{
	public const string ScreenId = "visit";

	public static Element Render(VisitState state, IPlatform platform)
	{
		async void Save()
		{
			if (state.Place.Length == 0)
			{
				state.Error = "Place is required";
				platform.RequestRender();
			}
			else
			{
				state.Error = "";
				state.Body = await Http.GetJson(platform, "http://10.0.2.2:8094/visit");
				state.Stops.Add(new VisitItem { Id = state.Place, Place = state.Place, When = state.When });
				platform.RequestRender();
			}
		}

		async void Locate()
		{
			state.Fix = await Location.Read(platform);
			platform.RequestRender();
		}

		var rows = new List<Element>();
		rows.Add(new Text("Visit") { Key = "heading", Style = new Style { FontSize = 22 } });
		rows.Add(new TextInput(
			state.Place,
			value =>
			{
				state.Place = value;
				platform.RequestRender();
			},
			"Place",
			"Place")
		{
			Key = "place",
			Keyboard = Keyboard.Text,
			Submit = Submit.Next,
		});
		rows.Add(new TextInput(
			state.When,
			value =>
			{
				state.When = value;
				platform.RequestRender();
			},
			"When",
			"When")
		{
			Key = "when",
			Keyboard = Keyboard.Text,
			Submit = Submit.Go,
			OnSubmit = () => Save(),
		});
		rows.Add(new Text(state.Error) { Key = "error" });
		rows.Add(new Text(state.Fix) { Key = "fix" });
		rows.Add(new Button("Locate", () => Locate(), "Locate") { Key = "locate" });
		rows.Add(new Button("Save", () => Save(), "Save") { Key = "save" });
		foreach (var item in state.Stops)
		{
			rows.Add(new Column(
			[
				new Text(item.Place) { Key = "title" },
				new Text(item.When) { Key = "when-row" },
				new Button(
					"Open",
					() =>
					{
						platform.Push("stop", item);
						platform.RequestRender();
					},
					"Open")
				{ Key = "open" },
			])
			{ Key = item.Id });
		}

		return new ScrollView(new Column(rows) { Key = "list", Style = new Style { Gap = 8 } }) { Key = "scroll" };
	}
}
