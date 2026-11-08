using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class HostOutputTests
{
	[Fact]
	public void Formats_a_real_Roslyn_diagnostic_as_file_line()
	{
		var repo = Directory.CreateTempSubdirectory("cnative-diag");
		var path = Path.Combine(repo.FullName, "Screen.cs");
		try
		{
			File.WriteAllText(path, "class C { int x = ; }\n");
			var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
			var compilation = CSharpCompilation.Create(
				"probe",
				[tree],
				[],
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			var errors = compilation.GetDiagnostics()
				.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
				.Select(diagnostic => diagnostic.ToString())
				.ToArray();
			Assert.NotEmpty(errors);
			var stdout = "exception=InvalidOperationException " + string.Join('\n', errors);
			var parsed = HostOutput.Parse(1, stdout, "", repo.FullName);

			Assert.Equal(ReloadStatus.CompileError, HostOutput.Classify(parsed));
			Assert.NotEmpty(parsed.Errors);
			Assert.Matches(@"^Screen\.cs:\d+:\d+: error CS\d+: .+$", parsed.Errors[0].Display);
			Assert.Equal(
				parsed.Errors.Select(error => error.Display).ToArray(),
				ReloadConsole.Format(ReloadStatus.CompileError, parsed, 12.5));
		}
		finally
		{
			repo.Delete(true);
		}
	}

	[Fact]
	public void A_pathless_host_diagnostic_uses_the_screen_file()
	{
		var stdout = "exception=InvalidOperationException (5,36): error CS1525: Invalid expression term ';'";
		var parsed = HostOutput.Parse(1, stdout, "");

		Assert.Equal(ReloadStatus.CompileError, HostOutput.Classify(parsed));
		Assert.Equal(
			["src/NativePoc.Screen/Screen.cs:5:36: error CS1525: Invalid expression term ';'"],
			ReloadConsole.Format(ReloadStatus.CompileError, parsed, 12.5));
	}

	[Fact]
	public void Two_errors_keep_their_own_lines()
	{
		var stdout = """
			exception=InvalidOperationException src/NativePoc.Screen/Screen.cs(4,12): error CS1002: ; expected
			src/NativePoc.Screen/Screen.cs(9,1): error CS1513: } expected
			""";
		var parsed = HostOutput.Parse(1, stdout, "");

		Assert.Equal(2, parsed.Errors.Count);
		Assert.Equal("src/NativePoc.Screen/Screen.cs:4:12: error CS1002: ; expected", parsed.Errors[0].Display);
		Assert.Equal("src/NativePoc.Screen/Screen.cs:9:1: error CS1513: } expected", parsed.Errors[1].Display);
	}

	[Fact]
	public void A_successful_apply_reports_reload_and_apply_time()
	{
		var stdout = """
			once-ms=19.3
			bytes=4096
			reply=Counter 1 collectible=True
			ui=Counter 1 | Ada
			pid=15262
			""";
		var parsed = HostOutput.Parse(0, stdout, "");
		var lines = ReloadConsole.Format(HostOutput.Classify(parsed), parsed, 24.8);

		Assert.Equal(ReloadStatus.Applied, HostOutput.Classify(parsed));
		Assert.Equal(
			["reloaded in 24.8 ms (apply 19.3 ms)", "reply Counter 1 collectible=True"],
			lines);
	}

	[Fact]
	public void The_host_single_line_once_result_is_an_apply()
	{
		var stdout = """
			once-ms=220.1 bytes=7680 reply=Counter 0 collectible=True
			ui=Native PoC | Counter 0 | Name | Save | Enter a name
			pid=6379
			""";
		var parsed = HostOutput.Parse(0, stdout, "");
		var lines = ReloadConsole.Format(HostOutput.Classify(parsed), parsed, 348.2);

		Assert.Equal(ReloadStatus.Applied, HostOutput.Classify(parsed));
		Assert.Equal(220.1, parsed.OnceMs);
		Assert.Equal("Counter 0 collectible=True", parsed.Reply);
		Assert.Equal(
			["reloaded in 348.2 ms (apply 220.1 ms)", "reply Counter 0 collectible=True"],
			lines);
	}

	[Fact]
	public void A_missing_method_is_a_rude_edit()
	{
		var stdout = "exception=InvalidOperationException MissingMethodException: Method not found: int NativePoc.AppState.get_Extra().";
		var parsed = HostOutput.Parse(1, stdout, "");

		Assert.Equal(ReloadStatus.RudeEdit, HostOutput.Classify(parsed));
		Assert.Equal(["rude edit -> restart required"], ReloadConsole.Format(ReloadStatus.RudeEdit, parsed, 8));
		Assert.Equal(
			"MissingMethodException: Method not found: int NativePoc.AppState.get_Extra().",
			ReloadConsole.RudeDetail(stdout));
		Assert.Equal("restarting", ReloadConsole.FollowUp(true, false));
		Assert.Equal("pass --restart-on-rude to rebuild and relaunch", ReloadConsole.FollowUp(false, true));
		Assert.Null(ReloadConsole.FollowUp(false, false));
		Assert.True(ReloadConsole.OfferRestartFlag(false, false));
		Assert.False(ReloadConsole.OfferRestartFlag(true, false));
		Assert.False(ReloadConsole.OfferRestartFlag(false, true));
	}

	[Fact]
	public void A_transport_failure_stays_a_failure()
	{
		var parsed = HostOutput.Parse(1, "exception=InvalidOperationException Connection refused", "");

		Assert.Equal(ReloadStatus.Failed, HostOutput.Classify(parsed));
		var lines = ReloadConsole.Format(ReloadStatus.Failed, parsed, 3);
		Assert.Equal("reload failed", lines[0]);
		Assert.Contains("Connection refused", lines[1]);
	}
}
