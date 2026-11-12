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
		Assert.Equal(IosBridge.Modules, IosSurface.Modules);
		Assert.Contains("http.GetJson", IosSurface.Modules);
		Assert.Contains("location.Read", IosSurface.Modules);
		Assert.Contains("storage.Get", IosSurface.Modules);
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
		Assert.Contains("HostPolicy", host, StringComparison.Ordinal);
		Assert.Contains("FieldBox", host, StringComparison.Ordinal);
		Assert.Contains("UITextView", host, StringComparison.Ordinal);
		Assert.Contains("LayoutScroll", host, StringComparison.Ordinal);
		Assert.Contains("nativepoc-touch", host, StringComparison.Ordinal);
		Assert.Contains("IosBridge", bridge, StringComparison.Ordinal);
		Assert.Contains("CopyText", bridge, StringComparison.Ordinal);
		Assert.Contains("WriteBack", bridge, StringComparison.Ordinal);
		Assert.Contains("PushRow", bridge, StringComparison.Ordinal);
		Assert.Contains("NSUrlSession", bridge, StringComparison.Ordinal);
		Assert.Contains("CLLocationManager", bridge, StringComparison.Ordinal);
		Assert.Contains("RecordProgram.Bind", screen, StringComparison.Ordinal);
		Assert.Contains("ScreenSnapshot.FormatTree", screen, StringComparison.Ordinal);
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

	[Fact]
	public void The_stand_in_maps_swipe_back_and_scale_zero_without_UIKit()
	{
		Assert.False(MotionMap.PopCommits(0.10f, 0f));
		Assert.True(MotionMap.PopCommits(0.40f, 0f));
		Assert.True(MotionMap.PopCommits(0.15f, 1000f));
		Assert.False(MotionMap.PopCommits(0.15f, 100f));
		Assert.Equal(0, MotionMap.PopMs(0.40f, true, 0f, 1f));
		Assert.Equal(0, MotionMap.AnimatedMs(220, 0f, 1f));
		Assert.Equal(100, MotionMap.ValueAtScaleZero(86, 100));
		var host = File.ReadAllText(Path.Combine(RepoRoot(), "spikes", "ios", "NativePoc.iOS", "UIKitHost.cs"));
		Assert.Contains("UINavigationController", host, StringComparison.Ordinal);
		Assert.Contains("CADisplayLink", host, StringComparison.Ordinal);
		Assert.Contains("net10.0-ios is not compiled", host, StringComparison.Ordinal);
	}

	[Fact]
	public void The_managed_UIKit_host_passes_platform_conformance()
	{
		var surface = new IosLogicSurface();
		PlatformConformance.Run(surface);
		Assert.Equal("Kept", surface.Reconciler.Root!.Text);
	}

	[Fact]
	public void Scroll_children_keep_their_index_and_text_follows_the_shared_policy()
	{
		var host = new IosLogicHost();
		var scroll = host.Create(ElementKind.Scroll);
		var first = host.Create(ElementKind.Text);
		var second = host.Create(ElementKind.Text);
		host.Insert(scroll, 0, first);
		host.Insert(scroll, 1, second);
		host.Remove(scroll, 0);
		Assert.Same(second, Assert.Single(scroll.Children));
		host.Insert(scroll, 0, first);
		Assert.Same(first, scroll.Children[0]);
		Assert.Same(second, scroll.Children[1]);

		host.SetText(first, "");
		Assert.True(first.Hidden);
		Assert.Equal("", first.AccessibilityValue);
		host.SetText(first, "Hello");
		Assert.False(first.Hidden);
		Assert.Equal("Hello", first.AccessibilityValue);

		host.SetGesture(first, Gestures.LongPress);
		Assert.Equal(HostPolicy.TouchFloorDp, first.TouchFloor);
		host.SetGesture(first, 0);
		Assert.Equal(0, first.TouchFloor);
		host.SetStyle(first, new Style { FontSize = 22 });
		Assert.True(first.Heading);
		host.SetStyle(first, new Style { FontSize = 18 });
		Assert.False(first.Heading);

		var field = host.Create(ElementKind.TextField);
		host.SetFieldMode(field, Keyboard.Text, 3, Submit.None);
		Assert.Equal("UITextView", field.FieldClass);
		host.SetFieldMode(field, Keyboard.Password, 3, Submit.None);
		Assert.Equal("UITextField", field.FieldClass);

		var list = host.Create(ElementKind.List);
		host.SetList(list, [new Column([new Text("Save name")]), new Text("Two")]);
		Assert.Equal("count=2", list.ListSummary);
		Assert.Equal("Save name", list.Rows[0]);
		Assert.Equal("Two", list.Rows[1]);
	}

	[Fact]
	public async Task The_bridge_accepts_storage_http_location_and_the_platform_methods()
	{
		var services = new MemoryServices();
		IosBridge.Module(services, "storage", "Set", ["visit.place", "Pier"]);
		Assert.Equal("Pier", await IosBridge.ModuleAsync(services, "storage", "Get", ["visit.place"]));
		Assert.Equal("body", await IosBridge.ModuleAsync(services, "http", "GetJson", ["http://10.0.2.2/visit"]));
		Assert.Equal("denied", await IosBridge.ModuleAsync(services, "location", "Read", []));
		Assert.Equal(LocationAccess.Denied, Location.Parse("denied").Access);
		IosBridge.CopyText(services, "form", "Pier");
		Assert.Equal("Pier", services.Copied);
		IosBridge.WriteBack(services, "visit");
		Assert.Equal("visit", services.Written);
		var pushed = "";
		IosBridge.PushRow(services, "stop", "row", screen => pushed = screen);
		Assert.Equal("stop", pushed);
		Assert.Equal("row", services.Row);
		string? resumed = null;
		IosBridge.Continue(Task.FromResult("ok"), text => resumed = text);
		Assert.Equal("ok", resumed);
		Assert.Throws<InvalidOperationException>(() => IosBridge.Module(services, "persist", "Put", ["k", "v"]));
		Assert.StartsWith("http-error: unknown", await IosBridge.ModuleAsync(services, "missing", "No", []), StringComparison.Ordinal);
	}

	[Fact]
	public void The_managed_host_runs_the_same_jni_and_telemetry_program()
	{
		var seen = "";
		JniRegistry.Add("Probe.Ping", args => seen = args.Length == 0 ? "" : args[0]);
		var before = ModuleTelemetry.FieldDeliveries;
		IPlatformSurface surface = new IosLogicSurface();
		var tree = surface.Apply(PulseProgram(), new RecordProgram.Bag());
		ElementIds.Invoke(tree, "pulse", null);
		Assert.Equal("40", seen);
		Assert.Equal(before + 1, ModuleTelemetry.FieldDeliveries);
		Assert.Contains("net10.0-ios is not compiled", File.ReadAllText(Path.Combine(RepoRoot(), "spikes", "ios", "NativePoc.iOS", "UIKitHost.cs")), StringComparison.Ordinal);
	}

	static byte[] PulseProgram()
	{
		string[] strings = ["Pulse", "pulse", "jni", "Probe.Ping", "40", "telemetry", "FieldDeliveries"];
		var script = new byte[]
		{
			RecordProgram.ActModule, 2, 0, 3, 0, 1, 0, 4, 0,
			RecordProgram.ActModule, 5, 0, 6, 0, 0,
		};
		var code = new byte[]
		{
			RecordProgram.OpPushStr, 0, 0,
			RecordProgram.OpPushStr, 0, 0,
			RecordProgram.OpMakeButton, 1, 0, (byte)script.Length, 0,
		};
		var body = new byte[code.Length + script.Length + 1];
		code.CopyTo(body, 0);
		script.CopyTo(body, code.Length);
		body[^1] = RecordProgram.OpReturn;
		return RecordProgram.Encode(strings, [], body, "ios-jni");
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

	sealed class MemoryServices : IIosServices
	{
		public Dictionary<string, string> Store { get; } = new(StringComparer.Ordinal);

		public string Copied { get; private set; } = "";

		public string Written { get; private set; } = "";

		public object? Row { get; private set; }

		public void Copy(string label, string text)
		{
			_ = label;
			Copied = text;
		}

		public void Haptic()
		{
		}

		public void Share(string text)
		{
			_ = text;
		}

		public void Set(string key, string value) => Store[key] = value;

		public string Get(string key) => Store.TryGetValue(key, out var value) ? value : "";

		public void Theme(string mode)
		{
			_ = mode;
		}

		public void Play(string key)
		{
			_ = key;
		}

		public string Http(string method, string url, string? body)
		{
			_ = method;
			_ = url;
			_ = body;
			return "body";
		}

		public string Location() => "denied";

		public void RememberRow(string screen, object row)
		{
			_ = screen;
			Row = row;
		}

		public void WriteBack(string screen) => Written = screen;
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
