using NativePoc;

namespace NativePoc.iOS.Tests;

public sealed class IosStatusTests
{
	[Fact]
	public void The_portable_map_names_a_UIKit_class_for_every_element()
	{
		Assert.Equal("UIStackView", IosSurface.ViewClass(ElementKind.Column));
		Assert.Equal("UILabel", IosSurface.ViewClass(ElementKind.Text));
		Assert.Equal("UIButton", IosSurface.ViewClass(ElementKind.Button));
		Assert.Equal("UITextField", IosSurface.ViewClass(ElementKind.TextField));
		Assert.Equal("UISwitch", IosSurface.ViewClass(ElementKind.Switch));
		Assert.Equal("UIScrollView", IosSurface.ViewClass(ElementKind.Scroll));
		Assert.Equal("UIScrollView", IosSurface.ViewClass(ElementKind.List));
		Assert.Equal("UIImageView", IosSurface.ViewClass(ElementKind.Image));
		Assert.Equal("UIView", IosSurface.ViewClass(ElementKind.Host));
		Assert.True(IosSurface.Applies(new Style { FontSize = 22, Gap = 8, Color = 0xFF1C1917 }));
		Assert.False(IosSurface.Applies(default));
		Assert.Equal(
			["clipboard.Copy", "haptics.Confirm", "share.Text", "storage.Set", "theme.Mode"],
			IosSurface.Modules);
	}

	[Fact]
	public void A_record_program_is_the_same_contract_the_host_is_authored_against()
	{
		var program = RecordProgram.Encode(
			["Hello", "title"],
			[new RecordProgram.PropDef(RecordProgram.TypeString, 1, 0, false, 0, [], [])],
			[
				RecordProgram.OpPushStr, 0, 0,
				RecordProgram.OpMakeText, 1, 0,
				RecordProgram.OpReturn,
			],
			"hello");
		var tree = RecordProgram.Bind(program, new RecordProgram.Bag()).Render(new Quiet());
		var text = Assert.IsType<Text>(tree);
		Assert.Equal("Hello", text.Value);
		Assert.Equal("title", text.Key);
		Assert.Equal(18, new Text("Hello") { Style = new Style { FontSize = 18 } }.Style!.Value.FontSize);
		Assert.Equal("UILabel", IosSurface.ViewClass(TreeDiff.KindOf(text)));
	}

	[Fact]
	public void The_UIKit_sources_name_the_contract_and_are_outside_the_Linux_build()
	{
		var root = RepoRoot();
		var host = File.ReadAllText(Path.Combine(root, "spikes", "ios", "NativePoc.iOS", "UIKitHost.cs"));
		var screen = File.ReadAllText(Path.Combine(root, "spikes", "ios", "NativePoc.iOS", "ScreenController.cs"));
		var bridge = File.ReadAllText(Path.Combine(root, "spikes", "ios", "NativePoc.iOS", "PlatformBridge.cs"));
		var catalog = File.ReadAllText(Path.Combine(root, "spikes", "ios", "NativePoc.iOS", "IosCatalog.cs"));
		var combined = host + screen + bridge + catalog;
		foreach (var kind in new[]
		{
			ElementKind.Column, ElementKind.Text, ElementKind.Button, ElementKind.TextField,
			ElementKind.Switch, ElementKind.Scroll, ElementKind.List, ElementKind.Image, ElementKind.Host,
		})
			Assert.Contains(IosSurface.ViewClass(kind), host, StringComparison.Ordinal);
		foreach (var module in IosSurface.Modules)
			Assert.Contains(module.Split('.')[0], bridge, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SetStyle", host, StringComparison.Ordinal);
		Assert.Contains("SetGesture", host, StringComparison.Ordinal);
		Assert.Contains("SetMotion", host, StringComparison.Ordinal);
		Assert.Contains("ScreenNav", screen, StringComparison.Ordinal);
		Assert.Contains("RecordProgram", combined, StringComparison.Ordinal);
		Assert.Contains("UITapGestureRecognizer", host, StringComparison.Ordinal);
		Assert.Contains("UILongPressGestureRecognizer", host, StringComparison.Ordinal);
		Assert.Contains("UISwipeGestureRecognizer", host, StringComparison.Ordinal);
		var slnx = File.ReadAllText(Path.Combine(root, "NativePoc.slnx"));
		Assert.DoesNotContain("NativePoc.iOS.csproj", slnx, StringComparison.Ordinal);
		Assert.Contains("NativePoc.iOS.Tests.csproj", slnx, StringComparison.Ordinal);
		var script = Path.Combine(root, "spikes", "ios", "scripts", "check-authored.sh");
		var start = new System.Diagnostics.ProcessStartInfo
		{
			FileName = "sh",
			WorkingDirectory = root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.ArgumentList.Add(script);
		using var process = System.Diagnostics.Process.Start(start);
		Assert.NotNull(process);
		var stdout = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		Assert.Equal(0, process.ExitCode);
		Assert.Contains("authored-check ok", stdout, StringComparison.Ordinal);
		Assert.Contains("never-compiled", stdout, StringComparison.Ordinal);
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

		throw new InvalidOperationException("Repo root was not found.");
	}

	sealed class Quiet : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}
}
