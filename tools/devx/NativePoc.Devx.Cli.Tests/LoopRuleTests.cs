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
		Assert.False(RestartPolicy.ShouldRestart(false, true, true, () => "y"));
		Assert.True(RestartPolicy.ShouldRestart(true, true, false, Boom));
		Assert.False(RestartPolicy.ShouldRestart(true, false, false, Boom));
		Assert.True(RestartPolicy.ShouldRestart(true, false, true, () => "y"));
		Assert.False(RestartPolicy.ShouldRestart(true, false, true, () => "n"));
		Assert.False(RestartPolicy.ShouldRestart(true, false, true, () => ""));
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
