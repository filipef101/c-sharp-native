using System.Diagnostics;
using NativePoc;

namespace NativePoc.iOS.Tests;

public sealed class SpikeChecks
{
	[Fact]
	public void Authored_spike_matches_the_mac_contract()
	{
		var root = RepoRoot();
		var script = Path.Combine(root, "spikes", "ios", "scripts", "check-authored.sh");
		var start = new ProcessStartInfo
		{
			FileName = "sh",
			WorkingDirectory = root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.ArgumentList.Add(script);
		using var process = Process.Start(start);
		Assert.NotNull(process);
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0, stdout + stderr);
		Assert.Contains("authored-check ok", stdout);
	}

	[Fact]
	public void Empty_screen_is_counter_zero_and_the_empty_greeting()
	{
		var state = new AppState();
		var column = Assert.IsType<Column>(Screen.Render(state, new QuietPlatform()));
		Assert.Equal("Counter 0", Assert.IsType<Text>(column.Children[0]).Value);
		var field = Assert.IsType<TextField>(column.Children[1]);
		Assert.Equal("", field.Value);
		Assert.Equal("Name", field.Placeholder);
		Assert.Equal("Name", field.AccessibilityLabel);
		var save = Assert.IsType<Button>(column.Children[2]);
		Assert.Equal("Save", save.Title);
		Assert.Equal("Save name", save.AccessibilityLabel);
		Assert.Equal("Enter a name", Assert.IsType<Text>(column.Children[3]).Value);
	}

	[Fact]
	public void Save_keeps_the_typed_name_and_copies_poc_1()
	{
		var state = new AppState();
		var platform = new QuietPlatform();
		var column = Assert.IsType<Column>(Screen.Render(state, platform));
		Assert.IsType<TextField>(column.Children[1]).OnChange("Ada");
		column = Assert.IsType<Column>(Screen.Render(state, platform));
		Assert.Equal("Hello Ada", Assert.IsType<Text>(column.Children[3]).Value);
		Assert.Equal("Ada", Assert.IsType<TextField>(column.Children[1]).Value);
		Assert.IsType<Button>(column.Children[2]).OnPress();
		Assert.Equal(1, state.Count);
		Assert.Equal("Ada", state.Name);
		Assert.Equal("poc-1", Assert.Single(platform.Nonces));
		column = Assert.IsType<Column>(Screen.Render(state, platform));
		Assert.Equal("Counter 1", Assert.IsType<Text>(column.Children[0]).Value);
		Assert.Equal("Ada", Assert.IsType<TextField>(column.Children[1]).Value);
	}

	static string RepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}
		throw new InvalidOperationException("Repository root was not found.");
	}

	sealed class QuietPlatform : IPlatform
	{
		public List<string> Nonces { get; } = new();

		public void CopyNonce(string nonce) => Nonces.Add(nonce);

		public void RequestRender()
		{
		}
	}
}
