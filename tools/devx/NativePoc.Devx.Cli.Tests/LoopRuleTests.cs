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
}
