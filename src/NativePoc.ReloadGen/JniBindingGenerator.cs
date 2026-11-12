using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NativePoc.ReloadGen;

[Generator]
public sealed class JniBindingGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterSourceOutput(context.CompilationProvider, static (production, compilation) =>
		{
			var source = Emit(compilation);
			if (source.Length == 0)
				return;
			production.AddSource("JniBinding.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	public static string Emit(Compilation compilation)
	{
		var builder = new StringBuilder();
		foreach (var tree in compilation.SyntaxTrees)
		{
			var model = compilation.GetSemanticModel(tree);
			foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
			{
				if (model.GetDeclaredSymbol(type) is not INamedTypeSymbol symbol)
					continue;
				var jni = TypeName(symbol);
				if (jni is null)
					continue;
				var methods = new List<string>();
				var registers = new List<string>();
				foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
				{
					if (model.GetDeclaredSymbol(method) is not IMethodSymbol member)
						continue;
					var body = Method(symbol, member, jni, out var register);
					if (body is null)
						continue;
					methods.Add(body);
					registers.Add(register);
				}

				if (methods.Count == 0)
					continue;
				if (builder.Length == 0)
				{
					builder.AppendLine("// Generated. A declared JNI binding. No hand-written JNI in the app.");
					builder.AppendLine("#nullable enable");
					builder.AppendLine("using System.Globalization;");
					builder.AppendLine("using Android.Runtime;");
					builder.AppendLine("using Java.Interop;");
				}

				builder.Append("namespace ").Append(symbol.ContainingNamespace.ToDisplayString()).AppendLine(";");
				builder.AppendLine();
				builder.Append("static partial class ").Append(symbol.Name).AppendLine();
				builder.AppendLine("{");
				foreach (var method in methods)
					builder.Append(method);
				builder.AppendLine("\t[System.Runtime.CompilerServices.ModuleInitializer]");
				builder.AppendLine("\tinternal static void RegisterJni()");
				builder.AppendLine("\t{");
				foreach (var register in registers)
					builder.Append(register);
				builder.AppendLine("\t}");
				builder.AppendLine("}");
			}
		}

		return builder.ToString();
	}

	static string? TypeName(INamedTypeSymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.Name is not ("JniTypeAttribute" or "JniType"))
				continue;
			if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name && name.Length > 0)
				return name;
		}

		return null;
	}

	static string? Method(INamedTypeSymbol type, IMethodSymbol method, string jniClass, out string register)
	{
		register = "";
		if (!method.IsPartialDefinition)
			return null;
		string? name = null;
		string? signature = null;
		var instance = false;
		var service = "";
		foreach (var attribute in method.GetAttributes())
		{
			if (attribute.AttributeClass?.Name is not ("JniMethodAttribute" or "JniMethod"))
				continue;
			if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is string methodName)
				name = methodName;
			if (attribute.ConstructorArguments.Length >= 2 && attribute.ConstructorArguments[1].Value is string methodSignature)
				signature = methodSignature;
			foreach (var argument in attribute.NamedArguments)
			{
				if (argument.Key == "Instance" && argument.Value.Value is bool flag)
					instance = flag;
				if (argument.Key == "Service" && argument.Value.Value is string value)
					service = value;
			}
		}

		if (name is null || signature is null)
			return null;
		var open = signature.IndexOf('(');
		var close = signature.IndexOf(')');
		if (open < 0 || close < open)
			return null;
		var args = signature.Substring(open + 1, close - open - 1);
		var ret = signature.Substring(close + 1);
		var call = Call(ret, instance);
		if (call is null)
			return null;
		var parameters = Parameters(args);
		if (parameters is null)
			return null;
		var names = new List<string>();
		var builder = new StringBuilder();
		builder.Append("\tpublic static partial ").Append(ReturnType(ret)).Append(' ').Append(method.Name).Append('(');
		for (var index = 0; index < parameters.Count; index++)
		{
			if (index > 0)
				builder.Append(", ");
			var parameterName = index < method.Parameters.Length ? method.Parameters[index].Name : "a" + index.ToString(CultureInfo.InvariantCulture);
			names.Add(parameterName);
			builder.Append(parameters[index].Type).Append(' ').Append(parameterName);
		}

		builder.AppendLine(")");
		builder.AppendLine("\t{");
		builder.Append("\t\tvar jniClass = JNIEnv.FindClass(\"").Append(Escape(jniClass)).AppendLine("\");");
		builder.AppendLine("\t\tif (jniClass == System.IntPtr.Zero)");
		builder.Append("\t\t\tthrow new System.InvalidOperationException(\"jni-bind miss class=").Append(Escape(jniClass)).AppendLine("\");");
		if (instance)
		{
			builder.Append("\t\tvar context = Android.App.Application.Context ?? throw new System.InvalidOperationException(\"jni-bind no-context\");");
			builder.AppendLine();
			builder.Append("\t\tvar service = context.GetSystemService(\"").Append(Escape(service)).AppendLine("\");");
			builder.AppendLine("\t\tif (service is null)");
			builder.Append("\t\t\tthrow new System.InvalidOperationException(\"jni-bind miss service=").Append(Escape(service)).AppendLine("\");");
			builder.Append("\t\tvar jniMethod = JNIEnv.GetMethodID(jniClass, \"").Append(Escape(name)).Append("\", \"").Append(Escape(signature)).AppendLine("\");");
		}
		else
		{
			builder.Append("\t\tvar jniMethod = JNIEnv.GetStaticMethodID(jniClass, \"").Append(Escape(name)).Append("\", \"").Append(Escape(signature)).AppendLine("\");");
		}

		builder.AppendLine("\t\tif (jniMethod == System.IntPtr.Zero)");
		builder.Append("\t\t\tthrow new System.InvalidOperationException(\"jni-bind miss method=").Append(Escape(name)).AppendLine("\");");
		builder.Append("\t\tAndroid.Util.Log.Info(\"NativePoc\", \"jni-bind class=").Append(Escape(jniClass)).Append(" method=").Append(Escape(name)).Append('"');
		for (var index = 0; index < names.Count; index++)
			builder.Append(" + \" ").Append(names[index]).Append("=\" + ").Append(names[index]);
		builder.AppendLine(");");
		if (ret == "V")
		{
			if (instance)
				builder.Append("\t\tJNIEnv.").Append(call).Append("(service.Handle, jniMethod");
			else
				builder.Append("\t\tJNIEnv.").Append(call).Append("(jniClass, jniMethod");
			AppendArgs(builder, names);
			builder.AppendLine(");");
		}
		else
		{
			builder.Append("\t\tvar jniResult = JNIEnv.").Append(call).Append(instance ? "(service.Handle, jniMethod" : "(jniClass, jniMethod");
			AppendArgs(builder, names);
			builder.AppendLine(");");
			builder.AppendLine("\t\treturn jniResult;");
		}

		builder.AppendLine("\t}");
		builder.AppendLine();
		var registration = new StringBuilder();
		registration.Append("\t\tNativePoc.JniRegistry.Add(\"").Append(type.Name).Append('.').Append(method.Name).Append("\", args => ").Append(method.Name).Append('(');
		for (var index = 0; index < parameters.Count; index++)
		{
			if (index > 0)
				registration.Append(", ");
			registration.Append(RegistryArg(parameters[index].Type, index));
		}

		registration.AppendLine("));");
		register = registration.ToString();
		return builder.ToString();
	}

	static string RegistryArg(string type, int index)
	{
		var cell = "args[" + index.ToString(CultureInfo.InvariantCulture) + "]";
		if (type == "long")
			return "long.Parse(" + cell + ", CultureInfo.InvariantCulture)";
		if (type == "int")
			return "int.Parse(" + cell + ", CultureInfo.InvariantCulture)";
		if (type == "bool")
			return cell + " == \"true\"";
		return cell;
	}

	static void AppendArgs(StringBuilder builder, List<string> names)
	{
		if (names.Count == 0)
			return;
		builder.Append(", new JValue[] { ");
		for (var index = 0; index < names.Count; index++)
		{
			if (index > 0)
				builder.Append(", ");
			builder.Append("new JValue(").Append(names[index]).Append(')');
		}

		builder.Append(" }");
	}

	static List<(string Type, string Jni)>? Parameters(string args)
	{
		var list = new List<(string, string)>();
		var index = 0;
		while (index < args.Length)
		{
			var mark = args[index];
			if (mark == 'J')
				list.Add(("long", "J"));
			else if (mark == 'I')
				list.Add(("int", "I"));
			else if (mark == 'Z')
				list.Add(("bool", "Z"));
			else
				return null;
			index++;
		}

		return list;
	}

	static string? Call(string ret, bool instance) => ret switch
	{
		"V" => instance ? "CallVoidMethod" : "CallStaticVoidMethod",
		"Z" => instance ? "CallBooleanMethod" : "CallStaticBooleanMethod",
		"J" => instance ? "CallLongMethod" : "CallStaticLongMethod",
		"I" => instance ? "CallIntMethod" : "CallStaticIntMethod",
		_ => null,
	};

	static string ReturnType(string ret) => ret switch
	{
		"V" => "void",
		"Z" => "bool",
		"J" => "long",
		"I" => "int",
		_ => "void",
	};

	static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
