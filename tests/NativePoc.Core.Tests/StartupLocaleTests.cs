using NativePoc;

namespace NativePoc.Tests;

public sealed class StartupLocaleTests
{
	[Fact]
	public void Staged_screens_stay_bytes_until_the_one_that_is_shown()
	{
		var alpha = Program("alpha", "Hello");
		var beta = Program("beta", "Beta");
		var nav = new ScreenNav();
		nav.Stage("bad", [1, 2, 3, 4]);
		nav.Stage("alpha", alpha);
		nav.Stage("beta", beta);
		nav.ShowRoot("alpha");
		Assert.Equal(0, nav.Loaded);
		Assert.Equal(3, nav.Staged);
		Assert.True(nav.Push("beta"));
		Assert.Equal(1, nav.Loaded);
		Assert.Equal(2, nav.Staged);
		var shownBound = nav.Top() ?? throw new InvalidOperationException("missing");
		var shown = shownBound.Render(new Quiet());
		Assert.Equal("Beta", Assert.IsType<Text>(shown).Value);
		Assert.Equal(1, nav.Loaded);
		Assert.True(nav.Pop());
		var rootBound = nav.Top() ?? throw new InvalidOperationException("missing");
		var root = rootBound.Render(new Quiet());
		Assert.Equal("Hello", Assert.IsType<Text>(root).Value);
		Assert.Equal(2, nav.Loaded);
		Assert.Equal(1, nav.Staged);
		Assert.Throws<InvalidOperationException>(() => nav.Push("bad"));
	}

	[Fact]
	public void Locale_strings_plural_and_direction_do_not_use_icu()
	{
		try
		{
			StringTable.Use("en");
			Assert.Equal("Trips", L.Get("Trips"));
			Assert.Equal("3 trips", L.Plural("3", L.Get("1 trip"), L.Get("{0} trips")));
			Assert.False(StringTable.RightToLeft);
			Assert.Equal("", StringTable.AssetSuffix);

			StringTable.Use("pt-PT");
			Assert.Equal("pt", StringTable.Language);
			Assert.Equal(".pt", StringTable.AssetSuffix);
			Assert.Equal("Viagens", L.Get("Trips"));
			Assert.Equal("Estrada da costa", L.Get("Coast road"));
			Assert.Equal("1 viagem", L.Plural("1", L.Get("1 trip"), L.Get("{0} trips")));
			Assert.Equal("3 viagens", L.Plural("3", L.Get("1 trip"), L.Get("{0} trips")));
			Assert.Equal("Olá Ada", L.Format("Olá {0}", "Ada"));
			Assert.False(StringTable.RightToLeft);

			StringTable.Use("ar-XB");
			Assert.True(StringTable.RightToLeft);
			Assert.Equal("\u200F[Trips]", L.Get("Trips"));
			Assert.Equal("en", StringTable.Language);
		}
		finally
		{
			StringTable.Use("en");
		}
	}

	[Fact]
	public void A_host_view_keeps_its_slot_when_the_prop_changes_and_rebuilds_when_the_kind_changes()
	{
		var host = new MemoryHost();
		var reconciler = new ViewReconciler<MemoryNode>(host);
		var first = new HostView("webview", "url", "about:blank")
		{
			Key = "page",
			Event = "navigate",
			OnEvent = (_, _) => { },
		};
		reconciler.Render(new Column([first]) { Key = "web" });
		var node = reconciler.ViewAt(NodePath.Root.Child(0));
		Assert.Equal(ElementKind.Host, node.Kind);
		Assert.Equal(1, node.HostMounts);
		Assert.Equal("about:blank", node.HostValue);
		var second = new HostView("webview", "url", "about:blank#next") { Key = "page", Event = "navigate" };
		reconciler.Render(new Column([second]) { Key = "web" });
		Assert.Same(node, reconciler.ViewAt(NodePath.Root.Child(0)));
		Assert.Equal(1, node.HostMounts);
		Assert.Equal("about:blank#next", node.HostValue);
		reconciler.Render(new Column([new HostView("map", "url", "x") { Key = "page" }]) { Key = "web" });
		Assert.Same(node, reconciler.ViewAt(NodePath.Root.Child(0)));
		Assert.Equal(2, node.HostMounts);
		Assert.Equal("map", node.HostKind);
	}

	static byte[] Program(string id, string text)
	{
		return RecordProgram.Encode(
			[text, "title", "ScreenId", id],
			[
				new RecordProgram.PropDef(RecordProgram.TypeString, 1, 0, false, 0, [], []),
				new RecordProgram.PropDef(RecordProgram.TypeScreen, 2, 3, false, 0, [], []),
			],
			[
				RecordProgram.OpPushStr, 0, 0,
				RecordProgram.OpMakeText, 1, 0,
				RecordProgram.OpReturn,
			],
			id);
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
