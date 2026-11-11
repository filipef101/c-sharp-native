using NativePoc;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class SubsetLowererTests
{
	[Fact]
	public void A_settings_screen_lowers_and_keeps_its_record_across_a_reload()
	{
		var path = Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Settings", "SettingsScreen.cs");
		var source = File.ReadAllText(path);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok);
		Assert.Equal(2, lowered.Program![5]);
		Assert.True(ScreenProgram.IsRecord(lowered.Program));
		Assert.Equal(ScreenProgram.SourceHash(source), RecordProgram.Bind(lowered.Program, new RecordProgram.Bag()).SourceHash);
		var json = SubsetLowerer.ToJson(lowered);
		Assert.Contains("\"ok\":true", json, StringComparison.Ordinal);
		Assert.Contains(ScreenProgram.SourceHash(source), json, StringComparison.Ordinal);

		var bag = new RecordProgram.Bag();
		var platform = new Platform();
		var first = RecordProgram.Bind(lowered.Program, bag).Render(platform);
		Assert.Equal("Day", TextValue(first, "settings/mode"));
		ElementIds.Invoke(first, "settings/name", "Ada");
		ElementIds.Invoke(first, "settings/note", "hem");
		ElementIds.Invoke(RecordProgram.Bind(lowered.Program, bag).Render(platform), "settings/dark", null);
		Assert.Equal(3, platform.Renders);
		var edited = source.Replace("\"Night\"", "\"Late\"", StringComparison.Ordinal);
		var again = SubsetLowerer.TryLower(edited);
		Assert.True(again.Ok);
		var tree = RecordProgram.Bind(again.Program!, bag).Render(platform);
		Assert.Equal("Late", TextValue(tree, "settings/mode"));
		Assert.Equal("Ada", FieldValue(tree, "settings/name"));
		Assert.Equal("hem", FieldValue(tree, "settings/note"));
		Assert.Contains("Name=416461", bag.Format(), StringComparison.Ordinal);
		Assert.Contains("Dark=true", bag.Format(), StringComparison.Ordinal);
	}

	[Fact]
	public void A_foreach_and_a_scroll_lower_and_a_call_is_refused_with_a_span()
	{
		var source = """
			namespace Demo;
			public sealed record Line
			{
				public string Id { get; set; } = "";
				public string Label { get; set; } = "";
			}
			public sealed record Board
			{
				public List<Line> Lines { get; } =
				[
					new() { Id = "a", Label = "Alpha" },
					new() { Id = "b", Label = "Beta" },
				];
			}
			public static class BoardScreen
			{
				public static Element Render(Board state, IPlatform platform)
				{
					var rows = new List<Element>();
					foreach (var item in state.Lines)
						rows.Add(new Text(item.Label) { Key = item.Id });
					return new ScrollView(new Column(rows) { Key = "lines" }) { Key = "scroll" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok);
		var tree = RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).Render(new Platform());
		var scroll = Assert.IsType<ScrollView>(tree);
		Assert.Equal("scroll", scroll.Key);
		var column = Assert.IsType<Column>(scroll.Child);
		Assert.Equal("Alpha", Assert.IsType<Text>(column.Children[0]).Value);
		Assert.Equal("a", column.Children[0].Key);
		Assert.Equal("Beta", Assert.IsType<Text>(column.Children[1]).Value);

		var refused = source.Replace("item.Label", "item.Label.Trim()", StringComparison.Ordinal);
		var bad = SubsetLowerer.TryLower(refused);
		Assert.False(bad.Ok);
		Assert.Equal(SubsetLowerer.UnknownExpression, bad.Diagnostics[0].Id);
		Assert.True(bad.Diagnostics[0].End > bad.Diagnostics[0].Start);
		Assert.Contains("Trim()", refused.AsSpan(bad.Diagnostics[0].Start, bad.Diagnostics[0].End - bad.Diagnostics[0].Start).ToString(), StringComparison.Ordinal);
		var json = SubsetLowerer.ToJson(bad);
		Assert.Contains("\"ok\":false", json, StringComparison.Ordinal);
		Assert.Contains("\"id\":\"subset.unknown-expression\"", json, StringComparison.Ordinal);
		Assert.Contains("\"line\":", json, StringComparison.Ordinal);
		Assert.Contains("\"column\":", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Duplicate_keys_and_the_counter_screen_are_refused()
	{
		var duplicate = """
			namespace Demo;
			public sealed record Pair
			{
				public string Name { get; set; } = "";
			}
			public static class PairScreen
			{
				public static Element Render(Pair state, IPlatform platform) =>
					new Column(
					[
						new Text(state.Name) { Key = "name" },
						new Text("x") { Key = "name" },
					]);
			}
			""";
		var lowered = SubsetLowerer.TryLower(duplicate);
		Assert.False(lowered.Ok);
		Assert.Equal(SubsetLowerer.DuplicateKey, lowered.Diagnostics[0].Id);

		var screen = File.ReadAllText(Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "src", "NativePoc.Screen", "Screen.cs"));
		var counter = SubsetLowerer.TryLower(screen);
		Assert.False(counter.Ok);
		Assert.Equal(SubsetLowerer.UnknownMember, counter.Diagnostics[0].Id);
		Assert.Contains("TitlePrefix", counter.Diagnostics[0].Message, StringComparison.Ordinal);
	}

	static string TextValue(Element root, string id) =>
		Assert.IsType<Text>(ElementIds.Require(root, id)).Value;

	static string FieldValue(Element root, string id) =>
		Assert.IsType<TextField>(ElementIds.Require(root, id)).Value;

	sealed class Platform : IPlatform
	{
		public int Renders { get; private set; }

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender() => Renders++;
	}
}
