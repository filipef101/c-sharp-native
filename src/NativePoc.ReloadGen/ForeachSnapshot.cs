using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NativePoc.ReloadGen;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForeachSnapshotAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "NP264";

	static readonly DiagnosticDescriptor Rule = new(
		Id,
		"foreach must snapshot a list it changes",
		"This foreach changes '{0}' while walking it. Call ToArray() on the list so the compiled loop matches the reload VM.",
		"NativePoc",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ForEachStatement);
	}

	static void Analyze(SyntaxNodeAnalysisContext context)
	{
		var loop = (ForEachStatementSyntax)context.Node;
		if (!ForeachSnapshot.Mutates(loop))
			return;
		context.ReportDiagnostic(Diagnostic.Create(Rule, loop.Expression.GetLocation(), loop.Expression.ToString()));
	}
}

internal static class ForeachSnapshot
{
	public static string[] Analyze(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create("foreach-snapshot", new[] { tree });
		var analyzed = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ForeachSnapshotAnalyzer()));
		return analyzed.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()).ToArray();
	}

	public static string RewriteSource(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(source);
		return (new Rewriter().Visit(tree.GetRoot()) ?? tree.GetRoot()).ToFullString();
	}

	public static SyntaxNode Rewrite(SyntaxNode root) => new Rewriter().Visit(root) ?? root;

	public static ExpressionSyntax Wrap(ExpressionSyntax expression) =>
		SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				expression.WithoutTrivia(),
				SyntaxFactory.IdentifierName("ToArray")))
		.WithTriviaFrom(expression);

	public static bool Mutates(ForEachStatementSyntax loop)
	{
		if (IsSnapshot(loop.Expression))
			return false;
		foreach (var invocation in loop.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (Nested(invocation, loop))
				continue;
			if (invocation.Expression is not MemberAccessExpressionSyntax member)
				continue;
			var name = member.Name.Identifier.ValueText;
			if (name is not ("Add" or "Clear" or "Insert" or "Remove" or "RemoveAll" or "RemoveAt"))
				continue;
			if (member.Expression.ToString() == loop.Expression.ToString())
				return true;
		}

		return false;
	}

	static bool IsSnapshot(ExpressionSyntax expression) =>
		expression is InvocationExpressionSyntax call
		&& call.ArgumentList.Arguments.Count == 0
		&& call.Expression is MemberAccessExpressionSyntax member
		&& member.Name.Identifier.ValueText == "ToArray";

	static bool Nested(SyntaxNode node, ForEachStatementSyntax loop)
	{
		for (var parent = node.Parent; parent is not null && parent != loop; parent = parent.Parent)
		{
			if (parent is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
				return true;
		}

		return false;
	}

	sealed class Rewriter : CSharpSyntaxRewriter
	{
		public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
		{
			var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
			if (!Mutates(visited))
				return visited;
			return visited.WithExpression(Wrap(visited.Expression));
		}
	}
}
