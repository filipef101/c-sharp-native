namespace NativePoc.Tests;

public sealed class HotReloadContractTests
{
	[Fact]
	public void Debug_listener_matches_the_cnative_token_contract()
	{
		var server = Source("src", "NativePoc.Android", "HotReloadServer.cs");
		var activity = Source("src", "NativePoc.Android", "MainActivity.cs");
		var host = Source("tools", "NativePoc.HotReload.Host", "Program.cs");

		Assert.StartsWith("#if DEBUG", server);
		Assert.Contains("const string TokenFile = \"nativepoc-hotreload.token\"", server);
		Assert.Contains("const string SocketName = \"npoc-hotreload\"", server);
		Assert.Contains("new LocalServerSocket(SocketName)", server);
		Assert.DoesNotContain("npoc-\" + hex", server);
		Assert.Contains("DeleteFile(\"hotreload.token\")", server);
		Assert.Contains("Log.Info(MainActivity.Tag, \"hotreload-listen\")", server);
		Assert.DoesNotContain("nativepoc_hotreload", server);
		Assert.Contains("localabstract:npoc-hotreload", host);
		Assert.DoesNotContain("localabstract:npoc-\" +", host);
		Assert.DoesNotContain("localabstract:", server);
		Assert.Contains("#if DEBUG", activity);
		Assert.Contains("HotReloadServer.Start(this)", activity);
		Assert.DoesNotContain("nativepoc_hotreload", activity);
		Assert.Contains("GetStringExtra(\"nativepoc_keyed_move\")", activity);
		Assert.Contains("GetStringExtra(\"nativepoc_root_swap\")", activity);
		Assert.Contains("GetStringExtra(\"nativepoc_bench\")", activity);
		Assert.Contains("GetStringExtra(\"nativepoc_root\")", activity);
		Assert.Contains("GetStringExtra(\"nativepoc_bind_log\")", activity);
		Assert.DoesNotContain("GetStringExtra(\"nativepoc_hotreload\")", activity);
		Assert.Equal(9, CountOf(activity, "GetStringExtra"));
	}

	[Fact]
	public void Debug_restores_the_checkpoint_before_the_first_frame()
	{
		var activity = Source("src", "NativePoc.Android", "MainActivity.cs");
		var packing = Source("src", "NativePoc.Android", "PackingActivity.cs");
		var store = Source("src", "NativePoc.Android", "StateStore.cs");
		var load = activity.IndexOf("StateStore.TryLoad(this)", StringComparison.Ordinal);
		var render = activity.IndexOf("RenderNow();", StringComparison.Ordinal);
		var packingLoad = packing.IndexOf("StateStore.TryLoad(this)", StringComparison.Ordinal);
		var packingRender = packing.IndexOf("RenderNow();", StringComparison.Ordinal);
		Assert.True(load >= 0 && render > load);
		Assert.True(packingLoad >= 0 && packingRender > packingLoad);
		Assert.Contains("StateStore.Save(this)", activity);
		Assert.Contains("StateStore.Save(this)", packing);
		Assert.Contains("NavCatalog.ApplyCheckpoint", activity);
		Assert.Contains("NavCatalog.ApplyCheckpoint", store);
		Assert.Contains("nav=", activity);
		Assert.Contains("#if DEBUG", store);
		Assert.Contains("nativepoc-state.bin", store);
		Assert.DoesNotContain("nativepoc-hotreload.token", store);
	}

	[Fact]
	public void A_screen_program_is_applied_before_any_assembly_load()
	{
		var server = Source("src", "NativePoc.Android", "HotReloadServer.cs");
		var query = server.IndexOf("ScreenQuery.IsQuery", StringComparison.Ordinal);
		var program = server.IndexOf("ScreenProgram.IsProgram", StringComparison.Ordinal);
		var load = server.IndexOf("new AssemblyLoadContext", StringComparison.Ordinal);
		Assert.True(query >= 0);
		Assert.True(program > query);
		Assert.True(load > program);
		Assert.Contains("UseProgram", server);
		Assert.Contains("#if NATIVEPOC_PROGRAM_ONLY", server);
		Assert.Contains("This build has no assembly loader.", server);
	}

	[Fact]
	public void Release_publish_hides_only_the_workload_il3053()
	{
		var project = Source("src", "NativePoc.Android", "NativePoc.Android.csproj");
		Assert.Contains("<NoWarn>$(NoWarn);IL3053</NoWarn>", project);
		Assert.Contains("<WarningsNotAsErrors>XA1040</WarningsNotAsErrors>", project);
		Assert.Contains("<PublishAot>true</PublishAot>", project);
		Assert.DoesNotContain("IL3053</WarningsNotAsErrors>", project);
		Assert.Contains("NativePocDevListener refuses to build into a normal Release.", project);
		Assert.Contains("PublishAot on a Debug build requires NativePocDevListener", project);
	}

	static string Source(params string[] parts)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
			{
				var path = dir.FullName;
				foreach (var part in parts)
					path = Path.Combine(path, part);
				return File.ReadAllText(path);
			}
			dir = dir.Parent;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}

	static int CountOf(string text, string value)
	{
		var count = 0;
		var index = 0;
		while (index < text.Length)
		{
			var found = text.IndexOf(value, index, StringComparison.Ordinal);
			if (found < 0)
				return count;
			count++;
			index = found + value.Length;
		}
		return count;
	}
}
