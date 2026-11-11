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
	public void Motion_storage_and_theme_lower_into_the_record()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var motion = SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Motion", "MotionScreen.cs")));
		Assert.True(motion.Ok);
		var tree = RecordProgram.Bind(motion.Program!, new RecordProgram.Bag()).Render(new Platform());
		var text = Assert.IsType<Text>(Assert.IsType<Column>(tree).Children[0]);
		Assert.Equal(900, text.Motion!.Value.Duration);
		Assert.Equal(0, text.Motion.Value.FromOpacity);
		Assert.Equal(100, text.Motion.Value.Opacity);

		var settings = File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Settings", "SettingsScreen.cs"));
		var lowered = SubsetLowerer.TryLower(settings);
		Assert.True(lowered.Ok);
		var platform = new ModulePlatform();
		var bag = new RecordProgram.Bag();
		var first = RecordProgram.Bind(lowered.Program!, bag).Render(platform);
		ElementIds.Invoke(first, "settings/name", "Ada");
		ElementIds.Invoke(first, "settings/dark", null);
		ElementIds.Invoke(RecordProgram.Bind(lowered.Program!, bag).Render(platform), "settings/system", null);
		Assert.Contains(platform.Calls, call => call == "storage.Set");
		Assert.Contains(platform.Calls, call => call == "theme.Mode");
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
		Assert.True(counter.Ok, counter.Diagnostics.Count == 0 ? "" : counter.Diagnostics[0].Id + " " + counter.Diagnostics[0].Message);
		var bag = new RecordProgram.Bag();
		bag.SeedInt("Count", 3);
		bag.SeedString("Name", "Ada");
		bag.SeedBool("GreetingFirst", true);
		var tree = RecordProgram.Bind(counter.Program!, bag).Render(new Platform());
		var column = Assert.IsType<Column>(tree);
		Assert.Equal("greeting", column.Children[0].Key);
		Assert.Equal("Hello Ada", Assert.IsType<Text>(column.Children[0]).Value);
		Assert.Equal("Counter 3", Assert.IsType<Text>(column.Children[3]).Value);
		var platform = new Platform();
		Assert.IsType<Button>(column.Children[2]).OnPress();
		Assert.Equal(4, bag.ReadInt("Count"));

		var packingPath = Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Packing", "PackingScreen.cs");
		var packing = SubsetLowerer.TryLower(File.ReadAllText(packingPath));
		Assert.True(packing.Ok, packing.Diagnostics.Count == 0 ? "" : packing.Diagnostics[0].Id + " " + packing.Diagnostics[0].Message + " line " + packing.Diagnostics[0].Line);
		var packed = RecordProgram.Bind(packing.Program!, new RecordProgram.Bag()).Render(platform);
		Assert.Equal("scroll", packed.Key);
		Assert.Equal("Canvas tent", TextValue(packed, "scroll/list/tent/name"));
		_ = platform;
	}

	[Fact]
	public void A_screen_id_pushes_and_pops_without_dropping_the_other_bag()
	{
		const string counter = """
			namespace NativePoc.Nav;
			public sealed record CounterState
			{
				public int Count { get; set; }
				public string Name { get; set; } = "";
			}
			public static class CounterScreen
			{
				public const string ScreenId = "counter";
				public static Element Render(CounterState state, IPlatform platform) =>
					new Column(
					[
						new Text(state.Name) { Key = "name" },
						new Button("Packing", () => { platform.Push("packing"); platform.RequestRender(); }, "Packing") { Key = "packing" },
						new Button("Back", () => { platform.Pop(); platform.RequestRender(); }, "Back") { Key = "back" },
					]);
			}
			""";
		const string packing = """
			namespace NativePoc.Nav;
			public sealed record PackState
			{
				public string Title { get; set; } = "Pack";
			}
			public static class PackScreen
			{
				public const string ScreenId = "packing";
				public static Element Render(PackState state, IPlatform platform) =>
					new Text(state.Title) { Key = "title" };
			}
			""";
		var loweredCounter = SubsetLowerer.TryLower(counter);
		var loweredPacking = SubsetLowerer.TryLower(packing);
		Assert.True(loweredCounter.Ok, loweredCounter.Diagnostics.Count == 0 ? "" : loweredCounter.Diagnostics[0].Message);
		Assert.True(loweredPacking.Ok, loweredPacking.Diagnostics.Count == 0 ? "" : loweredPacking.Diagnostics[0].Message);
		Assert.Equal("counter", RecordProgram.ScreenId(loweredCounter.Program!));
		Assert.Equal("packing", RecordProgram.ScreenId(loweredPacking.Program!));
		var nav = new ScreenNav();
		nav.Show(loweredCounter.Program!);
		nav.Show(loweredPacking.Program!);
		nav.Bag("counter").WriteString("Name", "Ada");
		var host = new NavPlatform(nav);
		var top = nav.Top() ?? throw new InvalidOperationException("The stack is empty.");
		Assert.Equal("packing", RecordProgram.ScreenId(loweredPacking.Program!));
		Assert.Equal("Pack", Assert.IsType<Text>(top.Render(host)).Value);
		nav.Pop();
		var back = nav.Top() ?? throw new InvalidOperationException("The stack is empty.");
		var column = Assert.IsType<Column>(back.Render(host));
		Assert.Equal("Ada", Assert.IsType<Text>(column.Children[0]).Value);
		ElementIds.Invoke(back.Render(host), "packing", null);
		Assert.Equal("packing\n", host.Pushed);
		Assert.True(nav.Push("packing"));
		Assert.Equal("stack 0 counter\nstack 1 packing\n", nav.Format());
		var image = nav.Checkpoint();
		var again = new ScreenNav();
		again.Show(loweredCounter.Program!);
		again.Show(loweredPacking.Program!);
		again.Restore(image);
		Assert.Equal("Ada", again.Bag("counter").ReadString("Name"));
		Assert.Equal("stack 0 counter\nstack 1 packing\n", again.Format());
	}

	[Fact]
	public void A_recycled_list_keeps_a_row_key_without_mounting_every_row()
	{
		const string source = """
			namespace NativePoc.LongList;
			public sealed record Line
			{
				public string Id { get; set; } = "";
				public string Name { get; set; } = "";
			}
			public sealed record LongState
			{
				public List<Line> Items { get; set; } = new List<Line>()
				{
					new Line { Id = "a", Name = "Alpha" },
					new Line { Id = "b", Name = "Beta" },
					new Line { Id = "c", Name = "Gamma" },
				};
			}
			public static class LongListScreen
			{
				public const string ScreenId = "long-list";
				public static Element Render(LongState state, IPlatform platform)
				{
					var rows = new List<Element>();
					foreach (var item in state.Items)
						rows.Add(new Text(item.Name) { Key = item.Id });
					return new ListView(rows) { Key = "list" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var tree = RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).Render(new Platform());
		var list = Assert.IsType<ListView>(tree);
		Assert.Equal(3, list.Rows.Count);
		Assert.Equal("Alpha", Assert.IsType<Text>(list.Rows[0]).Value);
		Assert.Equal("c", list.Rows[2].Key);
		Assert.Equal("list", tree.Key);
	}

	[Fact]
	public void A_styled_text_and_an_image_lower_and_a_font_change_is_set_style()
	{
		const string source = """
			namespace Demo;
			public sealed class Board
			{
				public string Banner { get; set; } = "loading";
				public int Tick { get; set; }
			}
			public static class BoardScreen
			{
				public const string ScreenId = "places";
				public static Element Render(Board state, IPlatform platform)
				{
					platform.Effect("Banner", "places");
					platform.Every("Tick");
					return new Column(
						new Text(state.Banner) { Key = "banner", Style = new Style { FontSize = 22, Weight = FontWeight.Bold, Color = 0xFF1B3A2F } },
						new Image("marker") { Key = "marker" }
					)
					{ Key = "places", Style = new Style { Gap = 8, Padding = 12 } };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var platform = new EffectPlatform();
		var tree = RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag()).Render(platform);
		Assert.Equal(["Banner", "Tick"], platform.Calls);
		var column = Assert.IsType<Column>(tree);
		Assert.Equal(8, column.Style!.Value.Gap);
		Assert.Equal(12, column.Style.Value.Padding);
		var banner = Assert.IsType<Text>(column.Children[0]);
		Assert.Equal(22, banner.Style!.Value.FontSize);
		Assert.Equal(FontWeight.Bold, banner.Style.Value.Weight);
		Assert.Equal(0xFF1B3A2Fu, banner.Style.Value.Color);
		Assert.Equal("marker", Assert.IsType<Image>(column.Children[1]).Source);

		var edited = source.Replace("FontSize = 22", "FontSize = 18", StringComparison.Ordinal);
		var again = SubsetLowerer.TryLower(edited);
		Assert.True(again.Ok);
		var next = RecordProgram.Bind(again.Program!, new RecordProgram.Bag()).Render(new EffectPlatform());
		var patches = TreeDiff.Diff(tree, next);
		Assert.Contains(patches, patch => patch is SetStyle set && set.Style.FontSize == 18);
		Assert.DoesNotContain(patches, patch => patch is Replace);
	}

	[Fact]
	public void A_form_keeps_its_keyboard_and_validates_on_submit()
	{
		var path = Path.Combine(RepoRoot.Find(AppContext.BaseDirectory), "samples", "NativePoc.Form", "FormScreen.cs");
		var lowered = SubsetLowerer.TryLower(File.ReadAllText(path));
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var platform = new ModulePlatform();
		var bag = new RecordProgram.Bag();
		var tree = RecordProgram.Bind(lowered.Program!, bag).Render(platform);
		var email = Assert.IsType<TextField>(ElementIds.Require(tree, "form/email"));
		var note = Assert.IsType<TextField>(ElementIds.Require(tree, "form/note"));
		Assert.Equal(Keyboard.Email, email.Keyboard);
		Assert.Equal(Submit.Next, email.Submit);
		Assert.Equal(4, note.Lines);
		Assert.Equal(Submit.Send, note.Submit);
		ElementIds.Submit(tree, "form/note");
		Assert.Equal("Email is required", bag.ReadString("Error"));
		Assert.False(bag.ReadBool("Sent"));
		Assert.Equal(["haptic", "share-skipped"], platform.Calls);
		bag.WriteString("Email", "ada@camp.test");
		var again = RecordProgram.Bind(lowered.Program!, bag).Render(platform);
		ElementIds.Submit(again, "form/note");
		Assert.Equal("", bag.ReadString("Error"));
		Assert.True(bag.ReadBool("Sent"));
		Assert.Equal(["haptic", "share-skipped", "haptic", "share:ada@camp.test"], platform.Calls);
	}

	[Fact]
	public void Trips_lower_through_the_subset_and_a_tap_stores_the_open_trip()
	{
		var root = RepoRoot.Find(AppContext.BaseDirectory);
		var list = SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Trips", "TripsScreen.cs")));
		var detail = SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Trips", "TripScreen.cs")));
		var kit = SubsetLowerer.TryLower(File.ReadAllText(Path.Combine(root, "samples", "NativePoc.Trips", "KitScreen.cs")));
		Assert.True(list.Ok, list.Diagnostics.Count == 0 ? "" : list.Diagnostics[0].Message);
		Assert.True(detail.Ok, detail.Diagnostics.Count == 0 ? "" : detail.Diagnostics[0].Message);
		Assert.True(kit.Ok, kit.Diagnostics.Count == 0 ? "" : kit.Diagnostics[0].Message);
		var platform = new TripPlatform();
		var bag = new RecordProgram.Bag();
		var tree = RecordProgram.Bind(list.Program!, bag).Render(platform);
		Assert.Equal(Gestures.Tap, ElementIds.Require(tree, "scroll/list/coast").Gesture);
		Assert.Contains("loading", TextValue(tree, "scroll/list/weather"));
		ElementIds.Tap(tree, "scroll/list/coast");
		Assert.Equal("coast", bag.ReadString("OpenId"));
		Assert.Equal("Coast road", bag.ReadString("Name"));
		Assert.Equal("trip", platform.Pushed);
		Assert.Contains(platform.Modules, call => call.Contains("storage.Set", StringComparison.Ordinal) && call.Contains("trip-id", StringComparison.Ordinal));
		var tripBag = new RecordProgram.Bag();
		tripBag.WriteString("Name", "Coast road");
		tripBag.WriteString("OpenId", "coast");
		var trip = RecordProgram.Bind(detail.Program!, tripBag).Render(platform);
		Assert.Equal("Coast road", TextValue(trip, "trip/title"));
		ElementIds.Invoke(trip, "trip/note", "fly");
		Assert.Equal("fly", tripBag.ReadString("Note"));
		ElementIds.Invoke(trip, "trip/pack", null);
		Assert.Equal("kit", platform.Pushed);
		var kitBag = new RecordProgram.Bag();
		var kitTree = RecordProgram.Bind(kit.Program!, kitBag).Render(platform);
		ElementIds.Swipe(kitTree, "scroll/list/tent");
		Assert.Equal(1, kitBag.CountRows("Items"));
		Assert.Equal(Gestures.Refresh, kitTree.Gesture);
	}

	static string TextValue(Element root, string id) =>
		Assert.IsType<Text>(ElementIds.Require(root, id)).Value;

	static string FieldValue(Element root, string id) =>
		Assert.IsType<TextField>(ElementIds.Require(root, id)).Value;

	sealed class ModulePlatform : IPlatform
	{
		public List<string> Calls { get; } = new();

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Module(string module, string method, string[] args)
		{
			if (module == "share" && args.Length == 1 && args[0].Length == 0)
				Calls.Add("share-skipped");
			else if (module == "share")
				Calls.Add("share:" + args[0]);
			else if (module == "haptics")
				Calls.Add("haptic");
			else
				Calls.Add(module + "." + method);
		}
	}

	sealed class EffectPlatform : IPlatform
	{
		public List<string> Calls { get; } = new();

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Effect(string property, string resource) => Calls.Add(property);

		public void Every(string property) => Calls.Add(property);
	}

	sealed class Platform : IPlatform
	{
		public int Renders { get; private set; }

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender() => Renders++;
	}

	sealed class TripPlatform : IPlatform
	{
		public string Pushed { get; private set; } = "";

		public List<string> Modules { get; } = new();

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Push(string screen) => Pushed = screen;

		public void Effect(string property, string resource)
		{
		}

		public void Module(string module, string method, string[] args) =>
			Modules.Add(module + "." + method + " " + string.Join(' ', args));
	}

	sealed class NavPlatform : IPlatform
	{
		readonly ScreenNav _nav;

		public NavPlatform(ScreenNav nav) => _nav = nav;

		public string Pushed { get; private set; } = "";

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public void Push(string screen)
		{
			Pushed += screen + "\n";
			_nav.Push(screen);
		}

		public void Pop() => _nav.Pop();
	}
}
