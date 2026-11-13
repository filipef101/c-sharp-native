using System.Globalization;
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
