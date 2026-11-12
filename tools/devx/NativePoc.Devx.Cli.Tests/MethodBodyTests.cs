using NativePoc;

namespace NativePoc.Devx.Tests;

public sealed class MethodBodyTests
{
	[Fact]
	public void Next_and_Label_lower_and_the_slot_runs_them()
	{
		var service = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Screen", "CounterService.cs"));
		var library = File.ReadAllText(Path.Combine(Repo(), "samples", "NativePoc.Library", "LibraryFormat.cs"));
		Assert.True(MethodBody.TryLower(service, out var nextBodies, out var nextWhy), nextWhy);
		Assert.True(MethodBody.TryLower(library, out var labelBodies, out var labelWhy), labelWhy);
		var next = Assert.Single(nextBodies);
		var label = Assert.Single(labelBodies);
		RecordProgram.ClearReplacements();
		Assert.Equal("vm-body NativePoc.CounterService.Next", Reply(ScreenQuery.ReplaceBody(next.TypeName, next.MethodName, next.ReturnKind, next.ArgKinds, next.Code, next.Strings)));
		Assert.Equal("vm-body NativePoc.Library.LibraryFormat.Label", Reply(ScreenQuery.ReplaceBody(label.TypeName, label.MethodName, label.ReturnKind, label.ArgKinds, label.Code, label.Strings)));
		Assert.True(RecordProgram.TryRunReplacement("CounterService", "Next", [RecordProgram.AppValue.FromInt(3)], out var added));
		Assert.Equal(8, added.Int);
		Assert.True(RecordProgram.TryRunReplacement("LibraryFormat", "Label", [RecordProgram.AppValue.FromStr("  Ada  ")], out var trimmed));
		Assert.Equal("Ada", trimmed.Text);
		Assert.True(RecordProgram.TryRunReplacement("LibraryFormat", "Label", [RecordProgram.AppValue.FromStr("")], out var empty));
		Assert.Equal("empty", empty.Text);
		Assert.False(RecordProgram.TryRunReplacement("Missing", "Next", [RecordProgram.AppValue.FromInt(1)], out _));
		RecordProgram.ClearReplacements();
	}

	[Fact]
	public void A_statement_body_with_locals_if_while_and_for_runs()
	{
		const string next = """
			namespace NativePoc;
			public static class CounterService
			{
				public static int Next(int count)
				{
					var step = 2;
					if (count < 0)
						return 0;
					var total = count;
					var i = 0;
					while (i < step)
					{
						total = total + 4;
						i = i + 1;
					}
					return total + 1;
				}
			}
			""";
		const string label = """
			namespace NativePoc.Library;
			public static class LibraryFormat
			{
				public static string Label(string name)
				{
					if (name.Length == 0)
						return "blank";
					var trimmed = name.Trim();
					var n = 0;
					for (var i = 0; i < 2; i = i + 1)
						n = n + 1;
					if (n < 2)
						return "short";
					return trimmed;
				}
			}
			""";
		Assert.True(MethodBody.TryLower(next, out var nextBodies, out var nextWhy), nextWhy);
		Assert.True(MethodBody.TryLower(label, out var labelBodies, out var labelWhy), labelWhy);
		RecordProgram.ClearReplacements();
		var nextBody = Assert.Single(nextBodies);
		var labelBody = Assert.Single(labelBodies);
		Assert.Equal("vm-body NativePoc.CounterService.Next", Reply(ScreenQuery.ReplaceBody(nextBody.TypeName, nextBody.MethodName, nextBody.ReturnKind, nextBody.ArgKinds, nextBody.Code, nextBody.Strings)));
		Assert.Equal("vm-body NativePoc.Library.LibraryFormat.Label", Reply(ScreenQuery.ReplaceBody(labelBody.TypeName, labelBody.MethodName, labelBody.ReturnKind, labelBody.ArgKinds, labelBody.Code, labelBody.Strings)));
		Assert.True(RecordProgram.TryRunReplacement("CounterService", "Next", [RecordProgram.AppValue.FromInt(3)], out var added));
		Assert.Equal(12, added.Int);
		Assert.True(RecordProgram.TryRunReplacement("CounterService", "Next", [RecordProgram.AppValue.FromInt(-4)], out var stopped));
		Assert.Equal(0, stopped.Int);
		Assert.True(RecordProgram.TryRunReplacement("LibraryFormat", "Label", [RecordProgram.AppValue.FromStr("  Ada  ")], out var trimmed));
		Assert.Equal("Ada", trimmed.Text);
		Assert.True(RecordProgram.TryRunReplacement("LibraryFormat", "Label", [RecordProgram.AppValue.FromStr("")], out var empty));
		Assert.Equal("blank", empty.Text);
		RecordProgram.ClearReplacements();
	}

	[Fact]
	public void A_body_that_cannot_be_lowered_is_refused()
	{
		const string source = """
			namespace NativePoc;
			public static class CounterService
			{
				public static int Next(int count)
				{
					try
					{
						return count;
					}
					catch
					{
						return 0;
					}
				}
			}
			""";
		Assert.False(MethodBody.TryLower(source, out _, out var refusal));
		Assert.Contains("could not be lowered into the action VM", refusal, StringComparison.Ordinal);
		Assert.Contains("Relaunching.", refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void A_button_records_the_screen_file_and_line()
	{
		var button = new Button("Save", () => { }, "Save");
		Assert.Contains("MethodBodyTests.cs", button.SourceFile, StringComparison.Ordinal);
		Assert.True(button.SourceLine > 0);
	}

	[Fact]
	public void An_interpreted_throw_reports_the_screen_line()
	{
		var source = """
			namespace NativePoc;
			public sealed class AppState
			{
				public int Count { get; set; }
				public string Name { get; set; } = "";
			}
			public static class Screen
			{
				public static Element Render(AppState state, IPlatform platform) =>
					new Column(
					[
						new Button("Save", () => { throw new InvalidOperationException("save broke"); }, "Save") { Key = "save" },
					])
					{ Key = "root" };
			}
			""";
		var lowered = SubsetLowerer.TryLower(source, originFile: "src/NativePoc.Screen/Screen.cs");
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message);
		var bound = RecordProgram.Bind(lowered.Program!, new RecordProgram.Bag());
		var tree = bound.Render(new RecordingPlatform());
		string? span = null;
		ActionLog.OnFault = (file, message) => span = file + " " + message;
		try
		{
			ElementIds.Invoke(tree, "save", null);
		}
		finally
		{
			ActionLog.OnFault = null;
		}

		Assert.NotNull(span);
		Assert.Contains("src/NativePoc.Screen/Screen.cs:", span, StringComparison.Ordinal);
		Assert.Contains("save broke", span, StringComparison.Ordinal);
	}

	static string Reply(byte[] query)
	{
		Assert.Equal(ScreenQuery.VmBody, ScreenQuery.Op(query));
		return "vm-body " + RecordProgram.InstallReplacement(query.AsSpan(6));
	}

	sealed class RecordingPlatform : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}
