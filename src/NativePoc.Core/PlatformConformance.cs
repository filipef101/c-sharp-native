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
