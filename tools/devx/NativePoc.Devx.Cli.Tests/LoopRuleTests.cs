using System.IO.Compression;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class LoopRuleTests
{
	[Fact]
	public void Launch_deletes_the_stale_token_before_am_start()
	{
		var commands = LaunchCommand.BeforeStart(
			LaunchCommand.Package,
			"com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity");
		var lines = commands.Select(command => string.Join(' ', command)).ToArray();
		var delete = Array.IndexOf(lines, "shell run-as com.scratch.nativepoc rm -f files/nativepoc-hotreload.token");
		var start = Array.FindIndex(lines, line => line.StartsWith("shell am start ", StringComparison.Ordinal));

		Assert.Equal("shell am force-stop com.scratch.nativepoc", lines[0]);
		Assert.Contains("shell run-as com.scratch.nativepoc rm -f files/hotreload.token", lines);
		Assert.Contains("shell run-as com.scratch.nativepoc rm -f cache/nativepoc-hotreload.token", lines);
		Assert.True(delete > 0);
		Assert.True(start == lines.Length - 1);
		Assert.True(delete < start);
	}

	[Fact]
	public void An_app_package_id_comes_from_the_project()
	{
		var id = PackageId.FromText("""
			<Project>
				<PropertyGroup>
					<ApplicationId>com.scratch.hello</ApplicationId>
				</PropertyGroup>
			</Project>
			""");
		Assert.Equal("com.scratch.hello", id);
	}

	[Fact]
	public void Package_follows_NATIVEPOC_PACKAGE()
	{
		var previous = Environment.GetEnvironmentVariable("NATIVEPOC_PACKAGE");
		try
		{
			Environment.SetEnvironmentVariable("NATIVEPOC_PACKAGE", null);
			Assert.Equal("com.scratch.nativepoc", LaunchCommand.Package);
			Environment.SetEnvironmentVariable("NATIVEPOC_PACKAGE", " com.scratch.nativepoc.showcase ");
			Assert.Equal("com.scratch.nativepoc.showcase", LaunchCommand.Package);
		}
		finally
		{
			Environment.SetEnvironmentVariable("NATIVEPOC_PACKAGE", previous);
		}
	}

	[Fact]
	public void Launch_leaves_the_debug_state_file()
	{
		var commands = LaunchCommand.BeforeStart(
			LaunchCommand.Package,
			"com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity");
		var text = string.Join('\n', commands.Select(command => string.Join(' ', command)));
		Assert.Contains("nativepoc-hotreload.token", text);
		Assert.DoesNotContain("nativepoc-state.bin", text);
	}

	[Fact]
	public void The_first_run_captures_state_before_deploy()
	{
		var source = RepoFile("tools", "devx", "NativePoc.Devx.Cli", "AndroidLoop.cs");
		var execute = source.IndexOf("static int Execute", StringComparison.Ordinal);
		var capture = source.IndexOf("CaptureRunning(serial)", execute, StringComparison.Ordinal);
		var deploy = source.IndexOf("DeployAndLaunch(repo, rid, serial, listener:", capture, StringComparison.Ordinal);
		var restore = source.IndexOf("Restore(session, checkpoint)", deploy, StringComparison.Ordinal);
		Assert.True(execute >= 0);
		Assert.True(capture > execute);
		Assert.True(deploy > capture);
		Assert.True(restore > deploy);
	}

	[Fact]
	public void Launch_does_not_carry_the_token()
	{
		var args = LaunchCommand.Start("com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity");

		Assert.Equal(["shell", "am", "start", "-n", "com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity"], args);
		Assert.False(LaunchCommand.PassesToken(args));
		Assert.True(LaunchCommand.PassesToken(["shell", "am", "start", "--es", "nativepoc_hotreload", "00"]));
	}

	[Theory]
	[InlineData("0123456789abcdef0123456789ABCDEF", "0123456789abcdef0123456789abcdef")]
	[InlineData("npoc-0123456789abcdef0123456789abcdef\n", "0123456789abcdef0123456789abcdef")]
	public void Token_text_accepts_hex_and_a_socket_name(string text, string hex)
	{
		Assert.True(TokenText.TryParse(text, out var parsed));
		Assert.Equal(hex, parsed);
	}

	[Theory]
	[InlineData("")]
	[InlineData("short")]
	[InlineData("0123456789abcdef0123456789abcdeg")]
	public void Token_text_rejects_malformed_values(string text)
	{
		Assert.False(TokenText.TryParse(text, out _));
	}

	[Fact]
	public void The_abstract_socket_table_yields_the_token()
	{
		var table = "00000000: 00000002 00000000 00010000 0001 01 12345 @npoc-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n";
		Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", TokenText.FromUnixTable(table));
	}

	[Fact]
	public void Token_listings_keep_only_token_files()
	{
		var names = TokenText.TokenFileNames("profileInstalled\nnativepoc-hotreload.token\nsubdir/nope.token\n");
		Assert.Equal(["nativepoc-hotreload.token"], names);
	}

	[Fact]
	public void Screen_edits_reload_and_core_edits_restart()
	{
		Assert.Equal(
			ChangeEffect.ReloadScreen,
			ChangeEffectRules.Classify(["src/NativePoc.Screen/Screen.cs"]));
		Assert.Equal(
			ChangeEffect.RestartRequired,
			ChangeEffectRules.Classify(
			[
				"src/NativePoc.Screen/Screen.cs",
				"src/NativePoc.Core/Elements.cs",
			]));
		Assert.Equal(
			ChangeEffect.Ignore,
			ChangeEffectRules.Classify(["src/NativePoc.Android/obj/Debug/generated.cs"]));
		Assert.True(ChangeEffectRules.IsHostScreen("src/NativePoc.Screen/Screen.cs"));
		Assert.False(ChangeEffectRules.IsHostScreen("src/NativePoc.Screen/Helper.cs"));
	}

	[Fact]
	public void Packing_screen_edits_reload()
	{
		Assert.Equal(
			ChangeEffect.ReloadScreen,
			ChangeEffectRules.Classify(["samples/NativePoc.Packing/PackingScreen.cs"]));
		Assert.Equal(
			ChangeEffect.ReloadScreen,
			ChangeEffectRules.Classify(["samples/NativePoc.Packing/PackingState.cs"]));
		Assert.Equal(
			ChangeEffect.RestartRequired,
			ChangeEffectRules.Classify(
			[
				"samples/NativePoc.Packing/PackingScreen.cs",
				"src/NativePoc.Core/Elements.cs",
			]));
		Assert.True(ChangeEffectRules.IsPackingScreen("samples/NativePoc.Packing/PackingScreen.cs"));
		Assert.False(ChangeEffectRules.IsPackingScreen("samples/NativePoc.Packing/PackingActions.cs"));
		Assert.True(ChangeEffectRules.IsAppScreen("samples/hello/HelloScreen.cs"));
		Assert.Equal(ChangeEffect.ReloadScreen, ChangeEffectRules.Classify(["samples/hello/HelloScreen.cs"]));
		Assert.False(ChangeEffectRules.IsAppScreen("samples/NativePoc.Packing/PackingScreen.cs"));
		Assert.False(ChangeEffectRules.IsAppScreen("samples/NativePoc.LongList/LongListScreen.cs"));
		Assert.True(ChangeEffectRules.IsProjectScreen("samples/NativePoc.Places/PlacesScreen.cs"));
		Assert.True(ChangeEffectRules.IsProjectScreen("samples/NativePoc.Form/FormScreen.cs"));
		Assert.False(ChangeEffectRules.IsProjectScreen("samples/NativePoc.Packing/PackingScreen.cs"));
		Assert.False(ChangeEffectRules.IsProjectScreen("samples/hello/HelloScreen.cs"));
		Assert.Equal(ChangeEffect.ReloadScreen, ChangeEffectRules.Classify(["samples/NativePoc.Places/PlacesScreen.cs"]));
		Assert.Equal(ChangeEffect.ReloadScreen, ChangeEffectRules.Classify(["samples/NativePoc.Form/FormScreen.cs"]));
	}

	[Fact]
	public void New_writes_a_screen_and_refuses_a_second_time()
	{
		var root = Directory.CreateTempSubdirectory("cnative-new-").FullName;
		var file = NewLoop.Write(root, "hello");
		Assert.Equal(Path.Combine(root, "samples", "hello", "HelloScreen.cs"), file);
		var source = File.ReadAllText(file);
		Assert.Contains("public const string ScreenId = \"hello\";", source, StringComparison.Ordinal);
		Assert.Contains("new Button(\"Add\"", source, StringComparison.Ordinal);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		Assert.Throws<UsageException>(() => NewLoop.Write(root, "hello"));
		Assert.Throws<UsageException>(() => NewLoop.Write(root, "Hello"));
	}

	[Fact]
	public void A_new_screen_is_shown_and_add_counts()
	{
		var root = Directory.CreateTempSubdirectory("cnative-new-run-").FullName;
		var file = NewLoop.Write(root, "preview");
		Assert.Equal(new[] { "samples/preview/PreviewScreen.cs" }, NewLoop.AppScreens(root));
		var lowered = SubsetLowerer.TryLower(File.ReadAllText(file));
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		var tree = session.Tree();
		Assert.Contains("Hello", tree, StringComparison.Ordinal);
		Assert.Contains("Count 0", tree, StringComparison.Ordinal);
		session.Tap("preview/add");
		Assert.Contains("Count 1", session.Tree(), StringComparison.Ordinal);
	}

	[Fact]
	public void Restart_follows_the_flag_or_a_yes_answer()
	{
		Assert.False(RestartPolicy.ShouldRestart(false, RestartChoice.Yes, true, () => "y"));
		Assert.True(RestartPolicy.ShouldRestart(true, RestartChoice.Yes, false, Boom));
		Assert.False(RestartPolicy.ShouldRestart(true, RestartChoice.No, true, Boom));
		Assert.False(RestartPolicy.ShouldRestart(true, RestartChoice.Ask, false, Boom));
		Assert.True(RestartPolicy.ShouldRestart(true, RestartChoice.Ask, true, () => "y"));
		Assert.False(RestartPolicy.ShouldRestart(true, RestartChoice.Ask, true, () => "n"));
		Assert.False(RestartPolicy.ShouldRestart(true, RestartChoice.Ask, true, () => ""));
		Assert.Equal("cnative: --no leaves the app running.", RestartPolicy.Follow(false, RestartChoice.No, true, false));
		Assert.Equal(
			"cnative: not a terminal, so the restart stays off. Pass --yes to relaunch, or --no to keep this choice.",
			RestartPolicy.Follow(false, RestartChoice.Ask, false, false));
		Assert.Equal("pass --yes to relaunch. The default is no.", RestartPolicy.Follow(false, RestartChoice.Ask, true, false));
		Assert.Equal("restarting", RestartPolicy.Follow(true, RestartChoice.Yes, false, false));
	}

	[Fact]
	public void One_online_device_is_selected()
	{
		var text = "List of devices attached\nemulator-5554\tdevice\nemulator-5556\toffline\n";
		Assert.Equal(["emulator-5554"], DeviceList.Online(text));
		Assert.Equal("emulator-5554", DeviceList.Require(DeviceList.Online(text), null));
		Assert.Throws<InvalidOperationException>(() => DeviceList.Require([], null));
	}

	[Theory]
	[InlineData("x86_64", "android-x64")]
	[InlineData("arm64-v8a", "android-arm64")]
	public void Device_abi_maps_to_a_rid(string abi, string rid)
	{
		Assert.Equal(rid, Abi.RidFromProperty(abi));
	}

	[Theory]
	[InlineData("android-x64", "emulator-5554")]
	[InlineData("android-arm64", "emulator-5556")]
	public void Debug_and_listener_builds_use_the_device_rid(string rid, string serial)
	{
		var project = "NativePoc.Android.csproj";
		var debug = AndroidBuild.DebugRun(project, rid, serial);
		var listener = AndroidBuild.ListenerPublish(project, rid);

		Assert.Equal("build", debug[0]);
		Assert.Equal(rid, debug[Array.IndexOf(debug, "-r") + 1]);
		Assert.Contains("-t:Run", debug);
		Assert.Contains("-p:AdbTarget=-s " + serial, debug);
		Assert.DoesNotContain(debug, arg => arg.Contains("NativePocDevListener", StringComparison.Ordinal));

		Assert.Equal("publish", listener[0]);
		Assert.Equal("Debug", listener[Array.IndexOf(listener, "-c") + 1]);
		Assert.Equal(rid, listener[Array.IndexOf(listener, "-r") + 1]);
		Assert.Contains("-p:NativePocDevListener=true", listener);
		Assert.Contains("-p:NativePocHttp=true", listener);
		Assert.Contains("-p:NativePocRecycler=true", listener);
		Assert.Contains("-p:NativePocLocation=true", listener);
		Assert.Contains("-p:NativePocPersist=true", listener);
		Assert.Contains("-p:NativePocNav=true", listener);
		Assert.Contains("-p:NativePocJni=true", listener);
		var other = rid == "android-arm64" ? "android-x64" : "android-arm64";
		Assert.DoesNotContain(other, debug);
		Assert.DoesNotContain(other, listener);
		Assert.Equal(
			rid == "android-arm64" ? "lib/arm64-v8a/libNativePoc.Android.so" : "lib/x86_64/libNativePoc.Android.so",
			AndroidBuild.NativeLibraryEntry(rid));
	}

	[Fact]
	public void Resolve_activity_keeps_the_component_line()
	{
		var text = "priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=true\ncom.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity\n";
		Assert.Equal(
			"com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity",
			ActivityName.FromResolve(text));
	}

	[Fact]
	public void Repo_root_walks_up_to_the_solution()
	{
		var root = Directory.CreateTempSubdirectory("cnative-repo");
		var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "app"));
		try
		{
			File.WriteAllText(Path.Combine(root.FullName, "NativePoc.slnx"), "<Solution />");
			Assert.Equal(root.FullName, RepoRoot.Find(nested.FullName));
		}
		finally
		{
			root.Delete(true);
		}
	}

	[Fact]
	public void A_binding_file_asks_for_a_republish_and_a_screen_still_reloads()
	{
		Assert.Equal(ChangeEffect.RepublishBinding, ChangeEffectRules.Classify(["samples/bind/DeviceToast.cs"]));
		Assert.Equal(ChangeEffect.ReloadScreen, ChangeEffectRules.Classify(["samples/bind/BindScreen.cs"]));
		Assert.Equal(
			ChangeEffect.RepublishBinding,
			ChangeEffectRules.Classify(["samples/bind/BindScreen.cs", "samples/bind/DeviceToast.cs"]));
		Assert.True(ChangeEffectRules.IsBindingSource("samples/bind/DeviceClipboard.cs"));
		Assert.False(ChangeEffectRules.IsBindingSource("samples/bind/BindScreen.cs"));
		Assert.Contains("a new binding needs a republish", BindingNotice.Line, StringComparison.Ordinal);
		Assert.Contains("hot-reloads", BindingNotice.Line, StringComparison.Ordinal);
		Assert.True(BindingNotice.Calls("DeviceToast.Show(\"pier\");"));
		Assert.False(BindingNotice.Calls("state.Note = \"pier\";"));
	}

	[Fact]
	public void Listener_output_does_not_share_the_debug_tree()
	{
		var repo = "/work/nativepoc";
		var debug = AndroidBuild.DebugDirectory(repo, "Debug", "android-x64").Replace('\\', '/');
		var listener = AndroidBuild.ListenerOutput(repo, "android-x64").Replace('\\', '/');
		Assert.Contains("/bin/Debug/", debug, StringComparison.Ordinal);
		Assert.Contains("/bin/listener/", listener, StringComparison.Ordinal);
		Assert.DoesNotContain("/bin/listener/", debug, StringComparison.Ordinal);
		Assert.DoesNotContain("/bin/Debug/", listener, StringComparison.Ordinal);
		var props = RepoFile("src", "NativePoc.Android", "Directory.Build.props");
		Assert.Contains("BaseOutputPath>bin/listener/</BaseOutputPath>", props, StringComparison.Ordinal);
		Assert.Contains("NativePocDevListener", props, StringComparison.Ordinal);
	}

	[Fact]
	public void A_dex_without_the_runtime_provider_is_rejected()
	{
		var root = Directory.CreateTempSubdirectory("cnative-dex");
		try
		{
			var missing = Path.Combine(root.FullName, "missing.apk");
			var present = Path.Combine(root.FullName, "present.apk");
			WriteDex(missing, "other");
			WriteDex(present, "NativeAotRuntimeProvider");
			Assert.False(AndroidBuild.DexHasRuntimeProvider(missing));
			Assert.True(AndroidBuild.DexHasRuntimeProvider(present));
		}
		finally
		{
			root.Delete(true);
		}
	}

	[Fact]
	public void Stale_debug_publish_is_removed_and_watch_refuses_it()
	{
		var repo = Directory.CreateTempSubdirectory("cnative-stale-").FullName;
		try
		{
			var stale = Path.Combine(repo, "src", "NativePoc.Android", "bin", "Debug", "net10.0-android", "android-x64", "publish");
			Directory.CreateDirectory(stale);
			var staleApk = Path.Combine(stale, "app-Signed.apk");
			WriteDex(staleApk, "NativeAotRuntimeProvider");
			var kept = Path.Combine(repo, "src", "NativePoc.Android", "bin", "Debug", "net10.0-android", "android-x64", "kept-Signed.apk");
			File.WriteAllBytes(kept, [4]);
			var note = Path.Combine(repo, "src", "NativePoc.Android", "bin", "Debug", "net10.0-android", "android-arm64", "publish");
			Directory.CreateDirectory(note);
			File.WriteAllText(Path.Combine(note, "note.txt"), "keep");
			var listener = AndroidBuild.ListenerOutput(repo, "android-x64");
			Directory.CreateDirectory(listener);
			var listenerApk = Path.Combine(listener, "app-Signed.apk");
			WriteDex(listenerApk, "other");
			var before = AndroidBuild.ListenerDex(repo);
			Assert.False(before.Provider);
			Assert.True(before.Stale);

			var removed = AndroidBuild.RemoveStaleDebugPublish(repo);
			Assert.Single(removed);
			Assert.False(Directory.Exists(stale));
			Assert.True(File.Exists(kept));
			Assert.True(File.Exists(Path.Combine(note, "note.txt")));
			Assert.True(File.Exists(listenerApk));
			var after = AndroidBuild.ListenerDex(repo);
			Assert.False(after.Provider);
			Assert.False(after.Stale);
			AndroidBuild.RequireListenerInstall(repo, "android-x64", listenerApk);
			var refused = Assert.Throws<InvalidOperationException>(() => AndroidBuild.RequireListenerInstall(repo, "android-x64", kept));
			Assert.Contains("watch installs only bin/listener", refused.Message, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	static void WriteDex(string path, string text)
	{
		using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
		var entry = zip.CreateEntry("classes.dex");
		using var stream = entry.Open();
		var bytes = System.Text.Encoding.UTF8.GetBytes(text);
		stream.Write(bytes, 0, bytes.Length);
	}

	static string Boom() => throw new InvalidOperationException("readLine should not be called.");

	static string RepoFile(params string[] parts)
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
