using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ArgParserTests
{
	[Fact]
	public void Run_android_uses_the_watch_defaults()
	{
		var options = ArgParser.Parse(["run", "android"]);

		Assert.False(options.Help);
		Assert.False(options.Once);
		Assert.Null(options.Serial);
		Assert.Null(options.Abi);
		Assert.Equal(40, options.DebounceMs);
		Assert.False(options.RestartOnRude);
	}

	[Fact]
	public void Flags_set_device_abi_debounce_and_restart()
	{
		var options = ArgParser.Parse(
		[
			"run",
			"android",
			"--restart-on-rude",
			"--device",
			"emulator-5554",
			"--abi",
			"android-arm64",
			"--debounce",
			"400",
			"--once",
		]);

		Assert.True(options.Once);
		Assert.Equal("emulator-5554", options.Serial);
		Assert.Equal("android-arm64", options.Abi);
		Assert.Equal(400, options.DebounceMs);
		Assert.True(options.RestartOnRude);
		Assert.False(options.Listener);
	}

	[Fact]
	public void Root_flag_names_the_screen_shown_last()
	{
		var options = ArgParser.Parse(["run", "android", "--root", "feed", "--listener"]);
		Assert.Equal("feed", options.Root);
		Assert.True(options.Listener);
	}

	[Fact]
	public void Listener_flag_keeps_the_device_abi_default()
	{
		var options = ArgParser.Parse(["run", "android", "--listener"]);
		Assert.True(options.Listener);
		Assert.Null(options.Abi);
	}

	[Theory]
	[InlineData("--help")]
	[InlineData("-h")]
	[InlineData("help")]
	public void Help_alone_does_not_run(string help)
	{
		var options = ArgParser.Parse([help]);
		Assert.True(options.Help);
	}

	[Fact]
	public void Help_after_the_command_is_accepted()
	{
		var options = ArgParser.Parse(["run", "android", "--help"]);
		Assert.True(options.Help);
	}

	[Theory]
	[InlineData("run", "ios")]
	[InlineData("watch", null)]
	public void Other_commands_are_rejected(string first, string? second)
	{
		var args = second is null ? new[] { first } : new[] { first, second };
		var error = Assert.Throws<UsageException>(() => ArgParser.Parse(args));
		Assert.Contains("cnative run android", error.Message);
	}

	[Fact]
	public void Empty_args_are_rejected()
	{
		Assert.Throws<UsageException>(() => ArgParser.Parse([]));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-5")]
	[InlineData("60001")]
	[InlineData("fast")]
	public void Debounce_must_be_a_positive_bounded_integer(string value)
	{
		var error = Assert.Throws<UsageException>(() => ArgParser.Parse(["run", "android", "--debounce", value]));
		Assert.Contains("--debounce", error.Message);
	}

	[Fact]
	public void Unknown_abi_is_rejected()
	{
		var error = Assert.Throws<UsageException>(() => ArgParser.Parse(["run", "android", "--abi", "android-x86"]));
		Assert.Contains("android-x64", error.Message);
	}

	[Fact]
	public void A_flag_without_its_value_is_rejected()
	{
		Assert.Throws<UsageException>(() => ArgParser.Parse(["run", "android", "--device"]));
	}

	[Fact]
	public void Unknown_flags_are_rejected()
	{
		var error = Assert.Throws<UsageException>(() => ArgParser.Parse(["run", "android", "--extra"]));
		Assert.Contains("--extra", error.Message);
	}
}
