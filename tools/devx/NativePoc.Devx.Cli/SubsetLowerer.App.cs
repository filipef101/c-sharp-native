using NativePoc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NativePoc.Devx;

public static partial class SubsetLowerer
{
	internal readonly record struct AppSig(string Type, string Method, byte Return, byte[] Args);

	static bool CollectExtras(
		IReadOnlyList<string>? companions,
		IReadOnlyList<string>? callables,
		Dictionary<string, MethodDeclarationSyntax> components,
		Dictionary<string, AppSig> calls,
		string screen,
		out SubsetDiagnostic? error)
	{
		_ = screen;
		error = null;
		if (companions is not null)
		{
			foreach (var companion in companions)
			{
				if (!ParseMembers(companion, out var members, out error))
					return false;
				foreach (var member in members)
				{
					if (member is not ClassDeclarationSyntax type || !type.Modifiers.Any(SyntaxKind.StaticKeyword))
						continue;
					foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
					{
						if (!IsComponent(method))
							continue;
						components[type.Identifier.ValueText + "." + method.Identifier.ValueText] = method;
					}
				}
			}
		}

		if (callables is null)
			return true;
		foreach (var callable in callables)
		{
			if (!ParseRoot(callable, out var root, out var members, out error))
				return false;
			var fileMarked = Marked(root.AttributeLists);
			foreach (var member in members)
			{
				if (member is not ClassDeclarationSyntax type || !type.Modifiers.Any(SyntaxKind.PublicKeyword))
					continue;
				var typeMarked = fileMarked || Marked(type.AttributeLists);
				foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
				{
					if (!typeMarked && !Marked(method.AttributeLists))
						continue;
					if (!Signature(method, out var ret, out var args))
						continue;
					var name = type.Identifier.ValueText;
					calls[name + "." + method.Identifier.ValueText] = new AppSig(name, method.Identifier.ValueText, ret, args);
				}
			}
		}

		return true;
	}

	static bool ParseMembers(string source, out SyntaxList<MemberDeclarationSyntax> members, out SubsetDiagnostic? error)
	{
		if (!ParseRoot(source, out _, out members, out error))
			return false;
		return true;
	}

	static bool ParseRoot(string source, out CompilationUnitSyntax root, out SyntaxList<MemberDeclarationSyntax> members, out SubsetDiagnostic? error)
	{
		root = null!;
		members = default;
		error = null;
		var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		foreach (var diagnostic in tree.GetDiagnostics())
		{
			if (diagnostic.Severity != DiagnosticSeverity.Error)
				continue;
			error = At(NotAScreen, diagnostic.GetMessage(), diagnostic.Location, source);
			return false;
		}

		root = tree.GetCompilationUnitRoot();
		var found = MembersOf(root);
		if (found is null)
		{
			error = At(NotAScreen, "A shared file is a namespace with types.", root, source);
			return false;
		}

		members = found.Value;
		return true;
	}

	static bool IsComponent(MethodDeclarationSyntax method)
	{
		if (method.TypeParameterList is not null || method.ParameterList.Parameters.Count != 1)
			return false;
		if (!method.Modifiers.Any(SyntaxKind.PublicKeyword) || !method.Modifiers.Any(SyntaxKind.StaticKeyword))
			return false;
		var text = method.ReturnType.ToString();
		return text is "Element" or "NativePoc.Element";
	}

	static bool Signature(MethodDeclarationSyntax method, out byte ret, out byte[] args)
	{
		ret = 0;
		args = [];
		if (method.TypeParameterList is not null || !method.Modifiers.Any(SyntaxKind.PublicKeyword))
			return false;
		if (method.Modifiers.Any(SyntaxKind.AbstractKeyword) || method.Modifiers.Any(SyntaxKind.ExternKeyword))
			return false;
		var text = method.ReturnType.ToString();
		ret = text switch
		{
			"void" => 0,
			"int" => RecordProgram.VmKindInt,
			"bool" => RecordProgram.VmKindBool,
			"string" => RecordProgram.VmKindStr,
			_ => 255,
		};
		if (ret == 255 || method.ParameterList.Parameters.Count > 8)
			return false;
		args = new byte[method.ParameterList.Parameters.Count];
		for (var index = 0; index < args.Length; index++)
		{
			var parameter = method.ParameterList.Parameters[index];
			if (parameter.Modifiers.Count > 0 || parameter.Type is null)
				return false;
			args[index] = parameter.Type.ToString() switch
			{
				"int" => RecordProgram.VmKindInt,
				"bool" => RecordProgram.VmKindBool,
				"string" => RecordProgram.VmKindStr,
				_ => 255,
			};
			if (args[index] == 255)
				return false;
		}

		return true;
	}

	static bool Marked(SyntaxList<AttributeListSyntax> lists)
	{
		foreach (var list in lists)
		{
			foreach (var attribute in list.Attributes)
			{
				var name = attribute.Name.ToString();
				if (name is "ReloadCallable" or "ReloadCallableAttribute" or "NativePoc.ReloadCallable" or "NativePoc.ReloadCallableAttribute")
					return true;
			}
		}

		return false;
	}

	sealed partial class Lower
	{
		readonly Dictionary<string, MethodDeclarationSyntax> _components;
		readonly Dictionary<string, AppSig> _calls;
		readonly HashSet<string> _callTypes;

		bool EmitComponent(MethodDeclarationSyntax method, InvocationExpressionSyntax call, KeySet? keys)
		{
			if (call.ArgumentList.Arguments.Count != 1
				|| call.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax argument
				|| argument.Identifier.ValueText != _state
				|| method.ParameterList.Parameters.Count != 1
				|| method.ParameterList.Parameters[0].Identifier.ValueText != _state)
				return Fail(UnknownElement, "The screen subset does not include " + method.Identifier.ValueText + ".", call);
			ExpressionSyntax? body = method.ExpressionBody?.Expression;
			if (body is null && method.Body is { Statements: [ReturnStatementSyntax returned] })
				body = returned.Expression;
			if (body is null)
				return Fail(UnknownElement, "The screen subset does not include " + method.Identifier.ValueText + ".", method);
			return Element(body, keys);
		}

		bool WriteApp(string type, string method, InvocationExpressionSyntax invocation, VmBuf buf, out byte kind)
		{
			kind = 0;
			var label = type + "." + method;
			if (!_calls.TryGetValue(label, out var call) || invocation.ArgumentList.Arguments.Count != call.Args.Length)
				return Fail(BadLambda, label + " is not in the compiled call table. Add [ReloadCallable] and relaunch.", invocation);
			for (var index = 0; index < call.Args.Length; index++)
			{
				if (!WriteExpr(invocation.ArgumentList.Arguments[index].Expression, buf, out var argKind))
					return false;
				if (argKind != call.Args[index])
					return Fail(BadLambda, label + " is not in the compiled call table. Add [ReloadCallable] and relaunch.", invocation);
			}

			buf.Op(RecordProgram.VmApp);
			buf.U16(_code.Intern(type));
			buf.U16(_code.Intern(method));
			buf.Raw((byte)call.Args.Length);
			kind = call.Return;
			return true;
		}
	}
}
