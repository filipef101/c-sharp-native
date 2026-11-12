using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NativePoc.ReloadGen;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BindingThreadAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "NP265";

	static readonly DiagnosticDescriptor Rule = new(
		Id,
		"A long-running binding stays off the UI thread",
		"'{0}' blocks the UI thread. Call it from Task.Run, or call the off-thread entry.",
		"NativePoc",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
	}

	static void Analyze(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		if (invocation.Expression is not MemberAccessExpressionSyntax member)
			return;
		var method = member.Name.Identifier.ValueText;
		var type = TypeName(member.Expression);
		if (type is null)
			return;
		if (!BindingThread.DeclaredBlocking(context.Compilation, type, method))
			return;
		if (!BindingThread.BlocksUi(invocation))
			return;
		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), type + "." + method));
	}

	static string? TypeName(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax identifier)
			return identifier.Identifier.ValueText;
		if (expression is MemberAccessExpressionSyntax member)
			return member.Name.Identifier.ValueText;
		return null;
	}
}

internal static class BindingThread
{
	public static string[] Analyze(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create("binding-thread", new[] { tree });
		var analyzed = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BindingThreadAnalyzer()));
		return analyzed.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()).ToArray();
	}

	public static bool DeclaredBlocking(Compilation compilation, string typeName, string methodName)
	{
		foreach (var tree in compilation.SyntaxTrees)
		{
			foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
			{
				if (method.Identifier.ValueText != methodName)
					continue;
				if (method.Parent is not ClassDeclarationSyntax type || type.Identifier.ValueText != typeName)
					continue;
				if (Marked(method))
					return true;
			}
		}

		return false;
	}

	public static bool BlocksUi(SyntaxNode node)
	{
		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			if (current is AnonymousFunctionExpressionSyntax lambda)
				return !IsTaskRunArgument(lambda);
			if (current is MethodDeclarationSyntax method && method.Identifier.ValueText == "Render")
				return true;
		}

		return false;
	}

	static bool Marked(MethodDeclarationSyntax method)
	{
		foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
		{
			var name = attribute.Name.ToString();
			if (name is not ("JniMethod" or "JniMethodAttribute"))
				continue;
			if (attribute.ArgumentList is null)
				continue;
			foreach (var argument in attribute.ArgumentList.Arguments)
			{
				if (argument.NameEquals?.Name.Identifier.ValueText != "Blocking")
					continue;
				if (argument.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.TrueLiteralExpression))
					return true;
			}
		}

		return false;
	}

	static bool IsTaskRunArgument(AnonymousFunctionExpressionSyntax lambda)
	{
		SyntaxNode? parent = lambda.Parent;
		if (parent is ArgumentSyntax)
			parent = parent.Parent?.Parent;
		else if (parent is ArgumentListSyntax)
			parent = parent.Parent;
		if (parent is not InvocationExpressionSyntax invoke)
			return false;
		if (invoke.Expression is not MemberAccessExpressionSyntax member || member.Name.Identifier.ValueText != "Run")
			return false;
		return Receiver(member.Expression) is "Task" or "ThreadPool";
	}

	static string? Receiver(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax identifier)
			return identifier.Identifier.ValueText;
		if (expression is MemberAccessExpressionSyntax member)
			return member.Name.Identifier.ValueText;
		return null;
	}
}
