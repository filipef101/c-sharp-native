namespace NativePoc.Tests;

public sealed class HotReloadContractTests
{
	[Fact]
	public void Debug_listener_matches_the_cnative_token_contract()
	{
		var server = Source("src", "NativePoc.Android", "HotReloadServer.cs");
		var activity = Source("src", "NativePoc.Android", "MainActivity.cs");

		Assert.StartsWith("#if DEBUG", server);
		Assert.Contains("const string TokenFile = \"nativepoc-hotreload.token\"", server);
		Assert.Contains("new LocalServerSocket(\"npoc-\" + hex)", server);
		Assert.Contains("DeleteFile(\"hotreload.token\")", server);
		Assert.Contains("Log.Info(MainActivity.Tag, \"hotreload-listen\")", server);
		Assert.DoesNotContain("nativepoc_hotreload", server);
		Assert.DoesNotContain("localabstract:", server);
		Assert.Contains("#if DEBUG", activity);
		Assert.Contains("HotReloadServer.Start(this)", activity);
		Assert.DoesNotContain("nativepoc_hotreload", activity);
		Assert.DoesNotContain("GetStringExtra", activity);
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
}
