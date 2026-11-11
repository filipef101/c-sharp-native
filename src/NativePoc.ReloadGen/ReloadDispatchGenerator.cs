using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NativePoc.ReloadGen;

[Generator]
public sealed class ReloadDispatchGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterSourceOutput(context.CompilationProvider, static (production, compilation) =>
		{
			var methods = new List<Callable>();
			var seen = new HashSet<string>();
			Collect(compilation.Assembly, methods, seen);
			foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
				Collect(reference, methods, seen);
			production.AddSource("ReloadDispatch.g.cs", SourceText.From(Emit(methods), Encoding.UTF8));
		});
	}

	static void Collect(IAssemblySymbol assembly, List<Callable> methods, HashSet<string> seen)
	{
		var spaces = new List<string>();
		foreach (var attribute in assembly.GetAttributes())
		{
			if (!IsMark(attribute) || attribute.ConstructorArguments.Length != 1)
				continue;
			if (attribute.ConstructorArguments[0].Value is string space && space.Length > 0)
				spaces.Add(space);
		}

		Walk(assembly.GlobalNamespace, spaces, methods, seen);
	}

	static void Walk(INamespaceSymbol space, List<string> opted, List<Callable> methods, HashSet<string> seen)
	{
		foreach (var type in space.GetTypeMembers())
			Take(type, opted, methods, seen);
		foreach (var child in space.GetNamespaceMembers())
			Walk(child, opted, methods, seen);
	}

	static void Take(INamedTypeSymbol type, List<string> opted, List<Callable> methods, HashSet<string> seen)
	{
		if (type.DeclaredAccessibility != Accessibility.Public || type.IsGenericType)
			return;
		var markedType = type.GetAttributes().Any(IsMark);
		var full = type.ToDisplayString();
		var optedIn = opted.Any(space => full.StartsWith(space + ".", System.StringComparison.Ordinal) || NamespaceOf(type) == space);
		foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
		{
			if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (method.IsGenericMethod || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
				continue;
			var marked = markedType || optedIn || method.GetAttributes().Any(IsMark);
			var holder = InstanceHolder(type, method);
			if (!method.IsStatic && !holder)
				continue;
			if (!marked || !Supported(method, out var ret, out var args))
				continue;
			var key = full + "." + method.Name + "/" + args;
			if (!seen.Add(key))
				continue;
			methods.Add(new Callable(type.Name, full, method.Name, method.IsStatic, ret, args, holder));
		}

		foreach (var nested in type.GetTypeMembers())
			Take(nested, opted, methods, seen);
	}

	static bool InstanceHolder(INamedTypeSymbol type, IMethodSymbol method)
	{
		if (method.IsStatic || type.IsStatic)
			return false;
		return type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0);
	}

	static bool Supported(IMethodSymbol method, out string ret, out string args)
	{
		ret = "";
		args = "";
		if (!Kind(method.ReturnType, out ret))
			return false;
		var builder = new StringBuilder();
		foreach (var parameter in method.Parameters)
		{
			if (!Kind(parameter.Type, out var kind) || kind.Length == 0)
				return false;
			if (builder.Length > 0)
				builder.Append(',');
			builder.Append(kind);
		}

		args = builder.ToString();
		return true;
	}

	static bool Kind(ITypeSymbol type, out string kind)
	{
		kind = "";
		if (type.SpecialType == SpecialType.System_Void)
			return true;
		kind = type.SpecialType switch
		{
			SpecialType.System_Int32 => "int",
			SpecialType.System_Boolean => "bool",
			SpecialType.System_String => "string",
			_ => "",
		};
		return kind.Length > 0;
	}

	static string NamespaceOf(INamedTypeSymbol type) =>
		type.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() : "";

	static bool IsMark(AttributeData attribute)
	{
		var type = attribute.AttributeClass;
		if (type is null || type.Name is not ("ReloadCallableAttribute" or "ReloadCallable"))
			return false;
		return type.ContainingNamespace?.ToDisplayString() == "NativePoc";
	}

	static string Emit(List<Callable> methods)
	{
		var builder = new StringBuilder();
		builder.AppendLine("#pragma warning disable CA2255");
		builder.AppendLine("namespace NativePoc");
		builder.AppendLine("{");
		builder.AppendLine("\tinternal static class ReloadDispatch");
		builder.AppendLine("\t{");
		var holders = new HashSet<string>();
		foreach (var method in methods)
		{
			if (method.Static || !method.HasHolder || !holders.Add(method.FullType))
				continue;
			builder.Append("\t\tstatic readonly ").Append(method.FullType).Append(' ').Append(Holder(method.FullType)).Append(" = new ").Append(method.FullType).AppendLine("();");
		}

		builder.AppendLine("\t\t[global::System.Runtime.CompilerServices.ModuleInitializer]");
		builder.AppendLine("\t\tinternal static void Install() => RecordProgram.BindAppCall(Call);");
		builder.AppendLine();
		builder.AppendLine("\t\tinternal static RecordProgram.AppValue Call(string typeName, string methodName, RecordProgram.AppValue[] args)");
		builder.AppendLine("\t\t{");
		var simpleCounts = methods.GroupBy(method => method.SimpleType).ToDictionary(group => group.Key, group => group.Select(method => method.FullType).Distinct().Count());
		foreach (var method in methods)
			EmitArm(builder, method, simpleCounts[method.SimpleType] == 1);
		builder.AppendLine("\t\t\tthrow new global::System.InvalidOperationException(\"No compiled method \" + typeName + \".\" + methodName + \". The call table has no arm. Relaunch to compile a new method.\");");
		builder.AppendLine("\t\t}");
		builder.AppendLine("\t}");
		builder.AppendLine("}");
		return builder.ToString();
	}

	static void EmitArm(StringBuilder builder, Callable method, bool simple)
	{
		var count = method.ArgKinds.Length == 0 ? 0 : method.ArgKinds.Split(',').Length;
		builder.Append("\t\t\tif ((typeName == \"").Append(Escape(method.FullType)).Append('"');
		if (simple)
			builder.Append(" || typeName == \"").Append(Escape(method.SimpleType)).Append('"');
		builder.Append(") && methodName == \"").Append(Escape(method.Method)).Append("\" && args.Length == ").Append(count).AppendLine(")");
		builder.AppendLine("\t\t\t{");
		builder.Append(Bind(method));
		if (method.Return.Length == 0)
		{
			builder.Append("\t\t\t\t").Append(Receiver(method)).Append(method.Method).Append('(').Append(Names(count)).AppendLine(");");
			builder.AppendLine("\t\t\t\treturn default;");
		}
		else
		{
			builder.Append("\t\t\t\treturn ").Append(Wrap(method.Return)).Append(Receiver(method)).Append(method.Method).Append('(').Append(Names(count)).AppendLine("));");
		}

		builder.AppendLine("\t\t\t}");
	}

	static string Receiver(Callable method)
	{
		if (method.Static)
			return method.FullType + ".";
		if (!method.HasHolder)
			return method.FullType + ".";
		return Holder(method.FullType) + ".";
	}

	static string Holder(string fullType) => "_hold_" + fullType.Replace('.', '_');

	static string Bind(Callable method)
	{
		// A `??` argument left in the call is unbalanced after ILLink inlines a
		// constant method: the replacement ldstr is removed and dup/brtrue/pop stay.
		// The local is its own statement, so that inlining stays balanced.
		if (method.ArgKinds.Length == 0)
			return "";
		var kinds = method.ArgKinds.Split(',');
		var builder = new StringBuilder();
		for (var index = 0; index < kinds.Length; index++)
		{
			var value = kinds[index] switch
			{
				"int" => "args[" + index + "].Int",
				"bool" => "args[" + index + "].Flag",
				"string" => "args[" + index + "].Text ?? \"\"",
				_ => "args[" + index + "].Text ?? \"\"",
			};
			builder.Append("\t\t\t\t").Append(kinds[index]).Append(" a").Append(index).Append(" = ").Append(value).AppendLine(";");
		}

		return builder.ToString();
	}

	static string Names(int count)
	{
		if (count == 0)
			return "";
		var names = new string[count];
		for (var index = 0; index < count; index++)
			names[index] = "a" + index;
		return string.Join(", ", names);
	}

	static string Wrap(string ret) => ret switch
	{
		"int" => "RecordProgram.AppValue.FromInt(",
		"bool" => "RecordProgram.AppValue.FromBool(",
		"string" => "RecordProgram.AppValue.FromStr(",
		_ => "RecordProgram.AppValue.FromStr(",
	};

	static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

	readonly struct Callable
	{
		public Callable(string simpleType, string fullType, string method, bool isStatic, string ret, string args, bool hasHolder)
		{
			SimpleType = simpleType;
			FullType = fullType;
			Method = method;
			Static = isStatic;
			Return = ret;
			ArgKinds = args;
			HasHolder = hasHolder;
		}

		public string SimpleType { get; }

		public string FullType { get; }

		public string Method { get; }

		public bool Static { get; }

		public string Return { get; }

		public string ArgKinds { get; }

		public bool HasHolder { get; }
	}
}
