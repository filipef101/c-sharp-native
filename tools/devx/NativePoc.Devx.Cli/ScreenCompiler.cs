using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NativePoc.Devx;

sealed record CompileOutput(byte[]? Assembly, IReadOnlyList<FormattedDiagnostic> Errors);

sealed class ScreenCompiler
{
	public const string RelativePath = "src/NativePoc.Screen/Screen.cs";

	public static readonly string[] ImplicitGlobalUsings =
	[
		"System",
		"System.Collections.Generic",
		"System.IO",
		"System.Linq",
		"System.Net.Http",
		"System.Threading",
		"System.Threading.Tasks",
	];

	readonly string _repo;
	readonly string _screenFile;
	CSharpCompilation _compilation;
	SyntaxTree _screen;

	ScreenCompiler(string repo, string screenFile, CSharpCompilation compilation, SyntaxTree screen)
	{
		_repo = repo;
		_screenFile = screenFile;
		_compilation = compilation;
		_screen = screen;
	}

	public static string ImplicitUsingsSource()
	{
		var builder = new StringBuilder();
		foreach (var name in ImplicitGlobalUsings)
			builder.Append("global using global::").Append(name).Append(";\n");
		return builder.ToString();
	}

	public static ScreenCompiler Create(string repo)
	{
		var screenFile = Path.Combine(repo, RelativePath.Replace('/', Path.DirectorySeparatorChar));
		var usings = CSharpSyntaxTree.ParseText(
			ImplicitUsingsSource(),
			new CSharpParseOptions(LanguageVersion.Preview),
			path: Path.Combine(repo, "src", "NativePoc.Screen", "ImplicitUsings.g.cs"),
			encoding: Encoding.UTF8);
		var screen = CSharpSyntaxTree.ParseText(
			"",
			new CSharpParseOptions(LanguageVersion.Preview),
			path: screenFile,
			encoding: Encoding.UTF8);
		var compilation = CSharpCompilation.Create(
			"NativePoc.Screen",
			[usings, screen],
			References(repo),
			new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: OptimizationLevel.Debug,
				nullableContextOptions: NullableContextOptions.Enable));
		return new ScreenCompiler(repo, screenFile, compilation, screen);
	}

	public CompileOutput CompileFile() => Compile(File.ReadAllText(_screenFile));

	public string ReadSource() => File.ReadAllText(_screenFile);

	public CompileOutput Compile(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(
			source,
			new CSharpParseOptions(LanguageVersion.Preview),
			path: _screenFile,
			encoding: Encoding.UTF8);
		_compilation = _compilation.ReplaceSyntaxTree(_screen, tree);
		_screen = tree;
		using var pe = new MemoryStream();
		var emitted = _compilation.Emit(pe);
		if (!emitted.Success)
		{
			var errors = emitted.Diagnostics
				.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
				.Select(ToDiagnostic)
				.ToArray();
			return new CompileOutput(null, errors);
		}

		return new CompileOutput(pe.ToArray(), []);
	}

	FormattedDiagnostic ToDiagnostic(Diagnostic diagnostic)
	{
		var span = diagnostic.Location.GetLineSpan();
		var file = string.IsNullOrEmpty(span.Path)
			? RelativePath
			: HostOutput.DisplayPath(span.Path, _repo);
		return new FormattedDiagnostic(
			file,
			span.StartLinePosition.Line + 1,
			span.StartLinePosition.Character + 1,
			diagnostic.Id,
			diagnostic.GetMessage(CultureInfo.InvariantCulture));
	}

	static string CoreAssembly(string repo)
	{
		var built = Path.Combine(repo, "src", "NativePoc.Core", "bin", "Debug", "net10.0", "NativePoc.Core.dll");
		if (File.Exists(built))
			return built;
		var beside = Path.Combine(AppContext.BaseDirectory, "NativePoc.Core.dll");
		if (File.Exists(beside))
			return beside;
		var cache = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".nuget",
			"packages",
			"nativepoc.core");
		if (Directory.Exists(cache))
		{
			foreach (var candidate in Directory.EnumerateFiles(cache, "NativePoc.Core.dll", SearchOption.AllDirectories))
			{
				if (candidate.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
					return candidate;
			}
		}

		throw new InvalidOperationException("Build NativePoc.Core before reloading the screen.");
	}

	static IReadOnlyList<MetadataReference> References(string repo)
	{
		var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
		var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
		if (!Directory.Exists(packRoot))
			throw new InvalidOperationException("The net10.0 reference pack was not found under " + packRoot + ".");
		var refDir = Directory.GetDirectories(packRoot)
			.Select(dir => Path.Combine(dir, "ref", "net10.0"))
			.FirstOrDefault(Directory.Exists)
			?? throw new InvalidOperationException("The net10.0 reference pack was not found under " + packRoot + ".");
		var corePath = CoreAssembly(repo);
		var coreBytes = File.ReadAllBytes(corePath);
		using var core = new MemoryStream(coreBytes, writable: false);
		return Directory.GetFiles(refDir, "*.dll")
			.Select(path => MetadataReference.CreateFromFile(path))
			.Append(MetadataReference.CreateFromStream(core))
			.ToArray();
	}
}
