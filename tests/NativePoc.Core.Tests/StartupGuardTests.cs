using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NativePoc.Tests;

public sealed class StartupGuardTests
{
	[Fact]
	public void The_baked_catalog_programs_render()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Android", "NavCatalog.cs"));
		Assert.Null(CatalogVersion.Problem(source));
		foreach (var name in CatalogVersion.Names)
		{
			var bytes = Catalog(name);
			Assert.True(CatalogVersion.Current(bytes), name);
			var host = new HeadlessHost();
			host.Apply(bytes, new RecordProgram.Bag());
			Assert.True(host.Accessibility.Length > 0, name);
		}
	}

	[Fact]
	public void A_planted_old_catalog_program_fails_the_check()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Android", "NavCatalog.cs"));
		var current = RecordProgram.OpcodeVersion;
		var plantedStamp = current == 0 ? 1u : current - 1;
		var header = "78,80,67,49," + RecordProgram.RecordFormat.ToString(CultureInfo.InvariantCulture) + ",2," + StampText(current) + ",";
		var older = "78,80,67,49," + RecordProgram.RecordFormat.ToString(CultureInfo.InvariantCulture) + ",2," + StampText(plantedStamp) + ",";
		var index = source.IndexOf(header, StringComparison.Ordinal);
		Assert.True(index >= 0, header);
		var planted = source.Remove(index, header.Length).Insert(index, older);
		var problem = CatalogVersion.Problem(planted);
		Assert.Equal("Counter opcode " + plantedStamp.ToString(CultureInfo.InvariantCulture), problem);
		var bytes = CatalogVersion.Read(planted, "Counter");
		Assert.NotNull(bytes);
		var thrown = Assert.Throws<InvalidOperationException>(() => new HeadlessHost().Apply(bytes, new RecordProgram.Bag()));
		Assert.Equal("Rejected record program version.", thrown.Message);
		Assert.True(StartupGuard.Rejected(thrown));
	}

	[Fact]
	public void A_mismatched_stamp_renders_the_counter_fallback()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Android", "NavCatalog.cs"));
		var current = RecordProgram.OpcodeVersion;
		var plantedStamp = current ^ 0x01000000;
		Assert.Equal((byte)current, (byte)plantedStamp);
		Assert.NotEqual(current, plantedStamp);
		var baked = Catalog("Counter");
		Assert.True(baked.Length > 9);
		var stored = baked[6] | ((uint)baked[7] << 8) | ((uint)baked[8] << 16) | ((uint)baked[9] << 24);
		Assert.Equal(current, stored);
		var header = "78,80,67,49," + RecordProgram.RecordFormat.ToString(CultureInfo.InvariantCulture) + ",2," + StampText(current) + ",";
		var older = "78,80,67,49," + RecordProgram.RecordFormat.ToString(CultureInfo.InvariantCulture) + ",2," + StampText(plantedStamp) + ",";
		var index = source.IndexOf(header, StringComparison.Ordinal);
		Assert.True(index >= 0, header);
		var planted = source.Remove(index, header.Length).Insert(index, older);
		var bytes = CatalogVersion.Read(planted, "Counter");
		Assert.NotNull(bytes);
		var thrown = Assert.Throws<InvalidOperationException>(() => new HeadlessHost().Apply(bytes, new RecordProgram.Bag()));
		Assert.Equal("Rejected record program version.", thrown.Message);
		Assert.True(StartupGuard.Rejected(thrown));
		var tree = Screen.Render(new AppState(), new HeadlessHost());
		Assert.Contains(ElementIds.Flatten(tree), node => node.Id == "title");
	}

	[Fact]
	public void A_rejected_program_falls_back_to_the_compiled_counter()
	{
		var host = new HeadlessHost();
		var thrown = Assert.Throws<InvalidOperationException>(() => host.Apply([(byte)'N', (byte)'P', (byte)'C', (byte)'1'], new RecordProgram.Bag()));
		Assert.True(StartupGuard.Rejected(thrown));
		Assert.Equal(
			"startup render rejected screen=counter reason=Rejected record program fallback=counter",
			StartupGuard.RenderLog("counter", "Rejected record program"));
		var tree = Screen.Render(new AppState(), new HeadlessHost());
		Assert.Contains(ElementIds.Flatten(tree), node => node.Id == "title");
	}

	[Fact]
	public void A_bad_checkpoint_names_old_version_or_corrupt()
	{
		var old = new byte[12];
		old[0] = (byte)'S';
		old[1] = (byte)'T';
		old[2] = (byte)'C';
		old[3] = (byte)'0';
		var version = Assert.Throws<InvalidOperationException>(() => StateCheckpoint.ReadInto(old, new AppState()));
		Assert.Equal("old-version", StartupGuard.StateReason(version.Message));
		Assert.Equal("state-load rejected reason=old-version", StartupGuard.StateLog("old-version"));
		var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
		var corrupt = Assert.Throws<InvalidOperationException>(() => StateCheckpoint.ReadInto(junk, new AppState()));
		Assert.Equal("corrupt", StartupGuard.StateReason(corrupt.Message));
		var nav = new ScreenNav();
		nav.ShowRoot(Catalog("Counter"));
		Assert.Equal(1, nav.Depth);
		Assert.True(nav.DropTop());
		Assert.Equal(0, nav.Depth);
		Assert.Null(nav.Top());
	}

	[Fact]
	public void An_older_program_inside_a_checkpoint_is_discarded()
	{
		var program = Catalog("Counter");
		Assert.True(RecordProgram.CurrentStamp(program));
		var oldProgram = program.ToArray();
		var stamp = PreviousStamp();
		Assert.NotEqual(RecordProgram.OpcodeVersion, stamp);
		oldProgram[6] = (byte)stamp;
		oldProgram[7] = (byte)(stamp >> 8);
		oldProgram[8] = (byte)(stamp >> 16);
		oldProgram[9] = (byte)(stamp >> 24);
		var nav = new NavCheckpoint
		{
			Stack = ["counter"],
			Screens = [new NavScreen { Id = "counter", Program = oldProgram }],
		};
		var checkpoint = StateCheckpoint.Write(new AppState { Count = 4, Name = "Ada" }, null, nav);
		var state = new AppState { Count = 1, Name = "kept" };
		var thrown = Assert.Throws<InvalidOperationException>(() => StateCheckpoint.ReadInto(checkpoint, state));
		Assert.Equal("Rejected state checkpoint version.", thrown.Message);
		Assert.Equal("old-version", StartupGuard.StateReason(thrown.Message));
		Assert.Equal("state-load rejected reason=old-version", StartupGuard.StateLog("old-version"));
		Assert.Equal(1, state.Count);
		Assert.Equal("kept", state.Name);
		var tree = Screen.Render(new AppState(), new HeadlessHost());
		Assert.Contains(ElementIds.Flatten(tree), node => node.Id == "title");

		var current = StateCheckpoint.Write(
			new AppState { Count = 4, Name = "Ada" },
			null,
			new NavCheckpoint
			{
				Stack = ["counter"],
				Screens = [new NavScreen { Id = "counter", Program = program }],
			});
		var loaded = new AppState();
		StateCheckpoint.ReadInto(current, loaded);
		Assert.Equal(4, loaded.Count);
		Assert.Equal("Ada", loaded.Name);
		Assert.True(StateCheckpoint.TryReadNav(current, out var restored));
		Assert.Equal("counter", Assert.Single(restored.Stack));
		Assert.Equal(program, Assert.Single(restored.Screens).Program);
	}

	[Fact]
	public void A_checkpoint_pulled_from_36cad64_is_discarded()
	{
		var hex = File.ReadAllText(Path.Combine(Repo(), "tests", "NativePoc.Core.Tests", "Fixtures", "state-36cad64.hex"));
		var compact = new char[hex.Length];
		var used = 0;
		foreach (var character in hex)
		{
			if (char.IsWhiteSpace(character))
				continue;
			compact[used++] = character;
		}

		var bytes = Convert.FromHexString(compact.AsSpan(0, used));
		Assert.Equal(1390, bytes.Length);
		Assert.Equal("d89cef2d77774dcd9eb21fe9c9ae1d823b20e541cfec10bec0af41eb31f1ac66", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
		Assert.Equal((byte)'S', bytes[0]);
		Assert.Equal((byte)'T', bytes[1]);
		Assert.Equal((byte)'C', bytes[2]);
		Assert.Equal((byte)'1', bytes[3]);
		Assert.Equal(1, bytes[5] | (bytes[6] << 8) | (bytes[7] << 16) | (bytes[8] << 24));
		Assert.Equal("Ada", Encoding.UTF8.GetString(bytes.AsSpan(12, 3)));
		var programAt = bytes.AsSpan().IndexOf("NPC1"u8);
		Assert.True(programAt >= 10);
		var program = bytes.AsSpan(programAt);
		Assert.False(RecordProgram.CurrentStamp(program));
		var stamp = bytes[programAt + 6]
			| ((uint)bytes[programAt + 7] << 8)
			| ((uint)bytes[programAt + 8] << 16)
			| ((uint)bytes[programAt + 9] << 24);
		Assert.NotEqual(RecordProgram.OpcodeVersion, stamp);
		Assert.Equal(203426465u, stamp);
		var state = new AppState { Count = 7, Name = "kept" };
		var thrown = Assert.Throws<InvalidOperationException>(() => StateCheckpoint.ReadInto(bytes, state));
		Assert.Equal("Rejected state checkpoint version.", thrown.Message);
		Assert.Equal("old-version", StartupGuard.StateReason(thrown.Message));
		Assert.Equal(7, state.Count);
		Assert.Equal("kept", state.Name);
		var tree = Screen.Render(new AppState(), new HeadlessHost());
		Assert.Contains(ElementIds.Flatten(tree), node => node.Id == "title");
	}

	[Fact]
	public void A_reset_with_a_bad_program_still_renders()
	{
		var program = Catalog("Counter");
		var oldProgram = program.ToArray();
		var stamp = PreviousStamp();
		oldProgram[6] = (byte)stamp;
		oldProgram[7] = (byte)(stamp >> 8);
		oldProgram[8] = (byte)(stamp >> 16);
		oldProgram[9] = (byte)(stamp >> 24);
		var nav = new ScreenNav();
		nav.Restore(new NavCheckpoint
		{
			Stack = ["counter"],
			Screens = [new NavScreen { Id = "counter", Program = oldProgram }],
		});
		Assert.Equal(1, nav.Depth);
		var seeded = false;
		var prepared = StartupGuard.Prepare(
			nav,
			(bytes, bag) =>
			{
				RecordProgram.SeedAppCounter(bytes, bag, 7, "Ada", false, false);
				seeded = true;
			},
			out var screen,
			out var reason);
		Assert.False(prepared);
		Assert.False(seeded);
		Assert.Equal(0, nav.Depth);
		Assert.Null(nav.Top());
		Assert.Equal("counter", screen);
		Assert.Equal("Rejected record program version.", reason);
		Assert.Equal(
			"startup render rejected screen=counter reason=Rejected record program version. fallback=counter",
			StartupGuard.RenderLog(screen, reason));
		var tree = Screen.Render(new AppState(), new HeadlessHost());
		var title = Assert.IsType<Text>(ElementIds.Require(tree, "title"));
		Assert.Equal(Screen.TitlePrefix + "0", title.Value);
	}

	[Fact]
	public void A_staged_counter_keeps_a_restored_count()
	{
		var nav = new ScreenNav();
		nav.Stage("counter", Catalog("Counter"));
		nav.ShowRoot("counter");
		Assert.Equal(0, nav.Loaded);
		Assert.Equal(1, nav.Staged);
		var prepared = StartupGuard.Prepare(
			nav,
			(bytes, bag) => RecordProgram.SeedAppCounter(bytes, bag, 4, "Ada", false, false),
			out var screen,
			out var reason);
		Assert.True(prepared);
		Assert.Equal("", screen);
		Assert.Equal("", reason);
		var top = nav.Top();
		Assert.NotNull(top);
		Assert.Equal(4, top.Value.State.ReadInt("Count"));
		Assert.Equal("Ada", top.Value.State.ReadString("Name"));
		var tree = top.Value.Render(new HeadlessHost());
		Assert.Equal(Screen.TitlePrefix + "4", Assert.IsType<Text>(ElementIds.Require(tree, "title")).Value);
		Assert.Equal("Hello Ada", Assert.IsType<Text>(ElementIds.Require(tree, "greeting")).Value);
	}

	[Fact]
	public void A_ui_snapshot_restores_count_and_name_without_a_program()
	{
		var saved = new AppState { Count = 4, Name = "Ada", GreetingFirst = true };
		var flags = InstanceUi.Flags(saved);
		var fresh = new AppState { Count = 1, Name = "kept" };
		var nav = new ScreenNav();
		nav.Restore(new NavCheckpoint
		{
			Stack = ["stories"],
			Screens = [new NavScreen { Id = "stories", Program = [(byte)'N', (byte)'P', (byte)'C', (byte)'1'] }],
		});
		Assert.Equal(1, nav.Depth);
		InstanceUi.Apply(fresh, saved.Count, saved.Name, flags);
		Assert.Equal(4, fresh.Count);
		Assert.Equal("Ada", fresh.Name);
		Assert.True(fresh.GreetingFirst);
		Assert.False(fresh.PlainRoot);
		Assert.Equal(1, nav.Depth);
		Assert.Equal("stories", nav.TopId());
		var tree = Screen.Render(fresh, new HeadlessHost());
		Assert.Equal(Screen.TitlePrefix + "4", Assert.IsType<Text>(ElementIds.Require(tree, "title")).Value);
		Assert.Equal("Hello Ada", Assert.IsType<Text>(ElementIds.Require(tree, "greeting")).Value);
	}

	[Fact]
	public void A_replaced_submit_runs_the_new_handler()
	{
		var seen = "";
		Element tree = new TextInput("", _ => { }, "Search", "Search")
		{
			Key = "search",
			Submit = Submit.Search,
			OnSubmit = () => seen = "first",
		};
		Assert.Equal("submitted search", ElementIds.Submit(tree, "search"));
		Assert.Equal("first", seen);
		tree = new TextInput("", _ => { }, "Search", "Search")
		{
			Key = "search",
			Submit = Submit.Search,
			OnSubmit = () => seen = "second",
		};
		Assert.Equal("submitted search", ElementIds.Submit(tree, "search"));
		Assert.Equal("second", seen);
	}

	[Fact]
	public void An_unknown_root_names_the_counter_fallback()
	{
		Assert.Equal("startup root unknown id=missing fallback=counter", StartupGuard.RootLog("missing"));
	}

	[Fact]
	public void An_unregistered_jni_name_misses()
	{
		var thrown = Assert.Throws<InvalidOperationException>(() => JniRegistry.Call("DeviceMissing.Nope", []));
		Assert.Equal("jni-bind miss DeviceMissing.Nope", thrown.Message);
	}

	static uint PreviousStamp()
	{
		var rows = new List<OpcodeTable.Row>();
		foreach (var row in OpcodeTable.Rows)
		{
			if (row.Code == RecordProgram.OpMakeTextInput)
				continue;
			rows.Add(row);
		}

		return OpcodeTable.Stamp(rows.ToArray());
	}

	static string StampText(uint stamp) =>
		string.Join(
			',',
			((byte)stamp).ToString(CultureInfo.InvariantCulture),
			((byte)(stamp >> 8)).ToString(CultureInfo.InvariantCulture),
			((byte)(stamp >> 16)).ToString(CultureInfo.InvariantCulture),
			((byte)(stamp >> 24)).ToString(CultureInfo.InvariantCulture));

	static byte[] Catalog(string name)
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Android", "NavCatalog.cs"));
		var match = Regex.Match(source, "static readonly byte\\[] " + name + " = \\[([^\\]]+)\\]", RegexOptions.Singleline);
		Assert.True(match.Success, name);
		var numbers = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var bytes = new byte[numbers.Length];
		for (var index = 0; index < numbers.Length; index++)
			bytes[index] = byte.Parse(numbers[index], CultureInfo.InvariantCulture);
		return bytes;
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "src", "NativePoc.Android", "NavCatalog.cs")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}
