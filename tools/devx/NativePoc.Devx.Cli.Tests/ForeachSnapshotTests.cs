using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NativePoc.ReloadGen;

namespace NativePoc.Devx.Tests;

public sealed class ForeachSnapshotTests
{
	[Fact]
	public async Task A_mutating_foreach_is_NP264_and_ToArray_clears_it()
	{
		const string source = """
			class Box {
				void Drop(System.Collections.Generic.List<string> items) {
					foreach (var item in items)
						items.Remove(item);
				}
				void Press(System.Collections.Generic.List<string> items) {
					foreach (var item in items)
						Later(() => items.Remove(item));
				}
				void Later(System.Action action) {}
			}
			""";
		var hit = ForeachSnapshot.Analyze(source);
		var diagnostic = Assert.Single(hit);
		Assert.StartsWith(ForeachSnapshotAnalyzer.Id, diagnostic, StringComparison.Ordinal);
		Assert.Contains("items", diagnostic, StringComparison.Ordinal);

		var rewritten = await ForeachSnapshotEdit.FixAsync(source);
		Assert.Contains("items.ToArray()", rewritten, StringComparison.Ordinal);
		Assert.Contains("Later(() => items.Remove(item))", rewritten, StringComparison.Ordinal);
		Assert.Empty(ForeachSnapshot.Analyze(rewritten));
		Assert.True(ForeachSnapshotEdit.HandlesNp264);
	}

	[Fact]
	public void Compiled_remove_matches_the_reload_vm()
	{
		const string source = """
			namespace NativePoc.Notes;
			public sealed class Note
			{
				public string Name { get; set; } = "";
				public bool Packed { get; set; }
			}
			public sealed class NotesState
			{
				public List<Note> Items { get; } = [];
				public int Count { get; set; }
			}
			public static class NotesScreen
			{
				public const string ScreenId = "notes";
				public static Element Render(NotesState state, IPlatform platform)
				{
					return new Column(
						new Button("Drop", () => { state.Items.Add(new Note { Name = "Lantern" }); state.Items.Add(new Note { Name = "Rope" }); foreach (var item in state.Items) if (item.Name == "Lantern") state.Items.Remove(item); }, "Drop") { Key = "drop" }
					) { Key = "notes" };
				}
			}
			""";
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var session = ScreenSession.Open(lowered.Program!);
		session.Tap("notes/drop");
		Assert.Null(ActionLog.LastStop);
		Assert.Equal("Rope", Assert.Single(session.Bag.Rows("Items"))[0]);

		var parsed = CSharpSyntaxTree.ParseText(ForeachSnapshot.RewriteSource("using System;\nusing System.Collections.Generic;\nusing NativePoc;\n" + source), new CSharpParseOptions(LanguageVersion.Preview));
		Assert.Contains("ToArray()", parsed.ToString(), StringComparison.Ordinal);
		var core = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var compilation = CSharpCompilation.Create(
			"snapshot-" + Guid.NewGuid().ToString("N"),
			[parsed],
			[
				MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(Element).Assembly.Location),
				MetadataReference.CreateFromFile(Path.Combine(core, "System.Runtime.dll")),
				MetadataReference.CreateFromFile(Path.Combine(core, "System.Collections.dll")),
			],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using var pe = new MemoryStream();
		var emitted = compilation.Emit(pe);
		Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
		var assembly = System.Reflection.Assembly.Load(pe.ToArray());
		var screen = assembly.GetTypes().Single(type => type.GetMethod("Render") is not null);
		var state = Activator.CreateInstance(screen.GetMethod("Render")!.GetParameters()[0].ParameterType)!;
		var platform = new CompiledPlatform();
		var root = (Element)screen.GetMethod("Render")!.Invoke(null, [state, platform])!;
		var button = ElementIds.Flatten(root).Select(node => node.Element).OfType<Button>().Single();
		button.OnPress();
		var items = (System.Collections.IEnumerable)state.GetType().GetProperty("Items")!.GetValue(state)!;
		var names = items.Cast<object>().Select(row => (string)row.GetType().GetProperty("Name")!.GetValue(row)!).ToArray();
		Assert.Equal(new[] { "Rope" }, names);
	}

	sealed class CompiledPlatform : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}
}
