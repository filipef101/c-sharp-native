using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.ReloadGen;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ForeachSnapshotFix))]
[Shared]
public sealed class ForeachSnapshotFix : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ForeachSnapshotAnalyzer.Id);

	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null)
			return;
		var diagnostic = context.Diagnostics[0];
		if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression)
			return;
		context.RegisterCodeFix(
			CodeAction.Create(
				"Snapshot the list with ToArray()",
				token => Apply(context.Document, root, expression, token),
				ForeachSnapshotAnalyzer.Id),
			diagnostic);
	}

	public static async Task<string> FixAsync(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create("snapshot", new[] { tree });
		var analyzed = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ForeachSnapshotAnalyzer()));
		var diagnostic = (await analyzed.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false)).Single();
		var root = await tree.GetRootAsync().ConfigureAwait(false);
		if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression)
			throw new InvalidOperationException("NP264 did not point at the list.");
		var provider = new ForeachSnapshotFix();
		if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
			throw new InvalidOperationException("NP264 is not a fix.");
		return root.ReplaceNode(expression, ForeachSnapshot.Wrap(expression)).ToFullString();
	}

	static Task<Document> Apply(Document document, SyntaxNode root, ExpressionSyntax expression, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(expression, ForeachSnapshot.Wrap(expression))));
	}
}

public static class ForeachSnapshotEdit
{
	public static Task<string> FixAsync(string source) => ForeachSnapshotFix.FixAsync(source);

	public static bool HandlesNp264 => new ForeachSnapshotFix().FixableDiagnosticIds.Contains(ForeachSnapshotAnalyzer.Id);
}
