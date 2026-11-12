namespace NativePoc;

/* The checks any screen platform has to pass: create, update, remove, text,
   list, accessibility, and a hot-reload apply. HeadlessHost implements the
   surface. The Android activity drives the same checks through
   ViewReconciler; that host is exercised from the core tests because
   Android.Views is not available here. */
public interface IPlatformSurface
{
	void Show(Func<IPlatform, Element> render);

	string Accessibility { get; }

	Element? Tree { get; }

	Element Apply(ReadOnlySpan<byte> program, RecordProgram.Bag bag);

	int NavDepth { get; }

	string NavFocus { get; }

	void ResetNav();
}

public static class PlatformConformance
{
	public static void Run(IPlatformSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);
		var state = new Probe();
		IPlatform? live = null;
		surface.Show(platform =>
		{
			live = platform;
			var children = new List<Element>
			{
				new Text(state.Title) { Key = "title" },
				new Button("Save", () =>
				{
					state.Title = "Saved";
					platform.RequestRender();
				}, "Save name")
				{ Key = "save" },
			};
			if (state.ShowNote)
				children.Add(new Text("Keep me") { Key = "note" });
			children.Add(new ListView([new Text("One"), new Text("Two")]) { Key = "rows" });
			return new Column(children);
		});

		var created = surface.Accessibility;
		Require(created.Contains("value=\"Hello\"", StringComparison.Ordinal), "create");
		Require(created.Contains("id=\"note\"", StringComparison.Ordinal), "create note");
		Require(created.Contains("value=\"Hello\"", StringComparison.Ordinal), "text");
		Require(created.Contains("count=2", StringComparison.Ordinal), "list");
		Require(created.Contains("label=\"Save name\"", StringComparison.Ordinal), "accessibility");
		if (live is null || surface.Tree is null)
			throw new InvalidOperationException("Platform conformance failed: create.");

		ElementIds.Invoke(surface.Tree, "save", null);
		Require(surface.Accessibility.Contains("value=\"Saved\"", StringComparison.Ordinal), "update");
		Require(surface.Accessibility.Contains("id=\"note\"", StringComparison.Ordinal), "update keeps the note");

		state.ShowNote = false;
		live.RequestRender();
		Require(!surface.Accessibility.Contains("id=\"note\"", StringComparison.Ordinal), "remove");
		Require(surface.Accessibility.Contains("value=\"Saved\"", StringComparison.Ordinal), "remove keeps the title");

		var code = new byte[]
		{
			RecordProgram.OpPushStr, 0, 0,
			RecordProgram.OpMakeText, 1, 0,
			RecordProgram.OpReturn,
		};
		surface.Apply(RecordProgram.Encode(["Edited", "title"], [], code, "edit"), new RecordProgram.Bag());
		Require(surface.Accessibility.Contains("value=\"Edited\"", StringComparison.Ordinal), "hot-reload apply");
		Require(!surface.Accessibility.Contains("Save name", StringComparison.Ordinal), "hot-reload apply replaces the tree");

		surface.ResetNav();
		IPlatform? navPlatform = null;
		surface.Show(platform =>
		{
			navPlatform = platform;
			if (surface.NavDepth > 1)
			{
				return new Column(
				[
					new Text("Detail") { Key = "heading", Style = new Style { FontSize = 22 } },
				]);
			}

			return new Column(
			[
				new Text("Home") { Key = "heading", Style = new Style { FontSize = 22 } },
				new Button("Open", () => platform.Push("detail"), "Open") { Key = "open" },
			]);
		});
		Require(surface.Accessibility.Contains("value=\"Home\"", StringComparison.Ordinal), "nav home");
		Require(surface.NavDepth == 1, "nav home depth");
		if (navPlatform is null || surface.Tree is null)
			throw new InvalidOperationException("Platform conformance failed: nav home.");
		ElementIds.Invoke(surface.Tree, "open", null);
		Require(surface.Accessibility.Contains("value=\"Detail\"", StringComparison.Ordinal), "nav push");
		Require(surface.NavDepth == 2, "nav push depth");
		Require(string.Equals(surface.NavFocus, "heading", StringComparison.Ordinal), "nav focus");
		navPlatform.Pop();
		Require(surface.NavDepth == 1, "nav back");
		Require(surface.Accessibility.Contains("value=\"Home\"", StringComparison.Ordinal), "nav back home");
		Require(string.Equals(surface.NavFocus, "heading", StringComparison.Ordinal), "nav back focus");
		ElementIds.Invoke(surface.Tree!, "open", null);
		Require(surface.NavDepth == 2, "nav push again");
		surface.Apply(RecordProgram.Encode(["Kept", "title"], [], code, "nav-edit"), new RecordProgram.Bag());
		Require(surface.NavDepth == 2, "hot reload keeps the stack");
		Require(surface.Accessibility.Contains("value=\"Kept\"", StringComparison.Ordinal), "hot reload paints the edit");
	}

	static void Require(bool ok, string name)
	{
		if (!ok)
			throw new InvalidOperationException("Platform conformance failed: " + name + ".");
	}

	sealed class Probe
	{
		public string Title { get; set; } = "Hello";

		public bool ShowNote { get; set; } = true;
	}
}
