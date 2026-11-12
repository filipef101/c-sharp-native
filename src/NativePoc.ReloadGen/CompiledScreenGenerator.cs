using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NativePoc.ReloadGen;

[Generator]
public sealed class CompiledScreenGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterSourceOutput(context.CompilationProvider, static (production, compilation) =>
		{
			var source = compilation.GetTypeByMetadataName("NativePoc.ScreenNav") is null
				|| compilation.GetTypeByMetadataName("NativePoc.IPlatform") is null
				|| compilation.GetTypeByMetadataName("NativePoc.Element") is null
				|| compilation.GetTypeByMetadataName("NativePoc.Text") is null
				? "// This compilation has no screen host.\n"
				: Emit(compilation);
			production.AddSource("CompiledScreens.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static string Emit(Compilation compilation)
	{
		var screens = new List<Screen>();
		var rows = new List<INamedTypeSymbol>();
		foreach (var type in Types(compilation.Assembly.GlobalNamespace))
		{
			if (!IsAppFile(type))
				continue;
			if (ScreenOf(type) is { } screen)
				screens.Add(screen);
			else if (type.TypeKind is TypeKind.Class or TypeKind.Struct && !type.IsStatic)
				rows.Add(type);
		}

		screens.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
		var builder = new StringBuilder();
		builder.AppendLine("// Generated. A cnative new screen is a direct call in a Release build.");
		builder.AppendLine("#nullable enable");
		builder.AppendLine("namespace NativePoc;");
		builder.AppendLine();
		builder.AppendLine("static class CompiledScreens");
		builder.AppendLine("{");
		foreach (var screen in screens)
			builder.Append("\tstatic readonly ").Append(screen.State).Append(' ').Append(screen.Holder).Append(" = new();\n");
		foreach (var screen in screens)
		{
			foreach (var row in rows)
			{
				if (!Shares(screen.StateSymbol, row))
					continue;
				builder.Append("\tstatic ").Append(row.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append("? ").Append(RowField(screen.Id, row)).Append(";\n");
			}
		}

		builder.AppendLine();
		builder.AppendLine("\tpublic static void Install(ScreenNav nav)");
		builder.AppendLine("\t{");
		foreach (var screen in screens)
			builder.Append("\t\tnav.Allow(\"").Append(Escape(screen.Id)).Append("\");\n");
		builder.AppendLine("\t}");
		builder.AppendLine();
		if (screens.Count == 0)
			builder.AppendLine("\tpublic static bool Knows(string id) => false;");
		else
		{
			builder.Append("\tpublic static bool Knows(string id) => id is ");
			for (var index = 0; index < screens.Count; index++)
			{
				if (index > 0)
					builder.Append(" or ");
				builder.Append('"').Append(Escape(screens[index].Id)).Append('"');
			}

			builder.AppendLine(";");
		}
		builder.AppendLine();
		builder.AppendLine("\tpublic static bool TryRender(string id, IPlatform platform, out Element element)");
		builder.AppendLine("\t{");
		builder.AppendLine("\t\tswitch (id)");
		builder.AppendLine("\t\t{");
		foreach (var screen in screens)
		{
			builder.Append("\t\t\tcase \"").Append(Escape(screen.Id)).Append("\":\n");
			builder.Append("\t\t\t\telement = ").Append(screen.Type).Append(".Render(").Append(screen.Holder).Append(", platform);\n");
			builder.AppendLine("\t\t\t\treturn true;");
		}

		builder.AppendLine("\t\t\tdefault:");
		builder.AppendLine("\t\t\t\telement = new Text(\"\");");
		builder.AppendLine("\t\t\t\treturn false;");
		builder.AppendLine("\t\t}");
		builder.AppendLine("\t}");
		builder.AppendLine();
		builder.AppendLine("\tpublic static void Deliver(string screen, object row)");
		builder.AppendLine("\t{");
		var delivered = false;
		foreach (var screen in screens)
		{
			foreach (var row in rows)
			{
				if (SymbolEqualityComparer.Default.Equals(screen.StateSymbol, row))
					continue;
				var shared = Shared(screen.StateSymbol, row);
				if (shared.Count == 0)
					continue;
				delivered = true;
				var local = "row" + RowField(screen.Id, row);
				builder.Append("\t\tif (screen == \"").Append(Escape(screen.Id)).Append("\" && row is ").Append(row.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' ').Append(local).Append(")\n");
				builder.AppendLine("\t\t{");
				foreach (var property in shared)
					builder.Append("\t\t\t").Append(screen.Holder).Append('.').Append(property.Name).Append(" = ").Append(local).Append('.').Append(property.Name).Append(";\n");
				if (screen.StateSymbol.GetMembers().OfType<IPropertySymbol>().Any(property => property.Name == "Error" && property.Type.SpecialType == SpecialType.System_String && property.SetMethod is not null))
					builder.Append("\t\t\t").Append(screen.Holder).Append(".Error = \"\";\n");
				builder.Append("\t\t\t").Append(RowField(screen.Id, row)).Append(" = ").Append(local).Append(";\n");
				builder.AppendLine("\t\t\treturn;");
				builder.AppendLine("\t\t}");
			}
		}

		if (!delivered)
			builder.AppendLine("\t\t_ = (screen, row);");
		builder.AppendLine("\t}");
		builder.AppendLine();
		builder.AppendLine("\tpublic static void WriteBack(string screen)");
		builder.AppendLine("\t{");
		var wrote = false;
		foreach (var screen in screens)
		{
			foreach (var row in rows)
			{
				if (SymbolEqualityComparer.Default.Equals(screen.StateSymbol, row))
					continue;
				var shared = Shared(screen.StateSymbol, row);
				if (shared.Count == 0)
					continue;
				wrote = true;
				var field = RowField(screen.Id, row);
				var saved = "saved" + field;
				builder.Append("\t\tif (screen == \"").Append(Escape(screen.Id)).Append("\" && ").Append(field).Append(" is { } ").Append(saved).Append(")\n");
				builder.AppendLine("\t\t{");
				foreach (var property in shared)
					builder.Append("\t\t\t").Append(saved).Append('.').Append(property.Name).Append(" = ").Append(screen.Holder).Append('.').Append(property.Name).Append(";\n");
				builder.AppendLine("\t\t\treturn;");
				builder.AppendLine("\t\t}");
			}
		}

		if (!wrote)
			builder.AppendLine("\t\t_ = screen;");
		builder.AppendLine("\t}");
		builder.AppendLine("}");
		return builder.ToString();
	}

	static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol space)
	{
		foreach (var type in space.GetTypeMembers())
		{
			yield return type;
			foreach (var nested in Types(type))
				yield return nested;
		}

		foreach (var child in space.GetNamespaceMembers())
		{
			foreach (var type in Types(child))
				yield return type;
		}
	}

	static IEnumerable<INamedTypeSymbol> Types(INamedTypeSymbol type)
	{
		foreach (var nested in type.GetTypeMembers())
		{
			yield return nested;
			foreach (var inner in Types(nested))
				yield return inner;
		}
	}

	static Screen? ScreenOf(INamedTypeSymbol type)
	{
		if (type.DeclaredAccessibility != Accessibility.Public || type.IsGenericType)
			return null;
		string? id = null;
		foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
		{
			if (field.Name == "ScreenId" && field.IsConst && field.ConstantValue is string value && value.Length > 0)
				id = value;
		}

		if (id is null)
			return null;
		foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
		{
			if (!method.IsStatic || method.Name != "Render" || method.Parameters.Length != 2)
				continue;
			if (method.Parameters[1].Type.Name != "IPlatform")
				continue;
			if (method.ReturnType.Name != "Element")
				continue;
			var state = method.Parameters[0].Type as INamedTypeSymbol;
			if (state is null)
				continue;
			return new Screen(id, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), state.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), HolderOf(id), state);
		}

		return null;
	}

	static bool IsAppFile(INamedTypeSymbol type)
	{
		foreach (var reference in type.DeclaringSyntaxReferences)
		{
			var parts = reference.SyntaxTree.FilePath.Replace('\\', '/').Split('/');
			var samples = System.Array.IndexOf(parts, "samples");
			if (samples < 0 || samples + 1 >= parts.Length)
				continue;
			var folder = parts[samples + 1];
			if (folder.Length > 0 && folder[0] is >= 'a' and <= 'z' && folder.IndexOf('.') < 0)
				return true;
		}

		return false;
	}

	static bool Shares(INamedTypeSymbol state, INamedTypeSymbol row) => Shared(state, row).Count > 0;

	static List<IPropertySymbol> Shared(INamedTypeSymbol state, INamedTypeSymbol row)
	{
		var found = new List<IPropertySymbol>();
		if (SymbolEqualityComparer.Default.Equals(state, row))
			return found;
		foreach (var property in state.GetMembers().OfType<IPropertySymbol>())
		{
			if (property.Name is "RouteScreen" or "RouteList" or "RouteRow" or "RouteMap" or "RouteFlags" or "Error")
				continue;
			if (property.SetMethod is null || property.GetMethod is null || property.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (property.Type.SpecialType != SpecialType.System_String && property.Type.SpecialType != SpecialType.System_Boolean)
				continue;
			var match = row.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault(candidate =>
				candidate.Type.Equals(property.Type, SymbolEqualityComparer.Default)
				&& candidate.SetMethod is not null
				&& candidate.GetMethod is not null
				&& candidate.DeclaredAccessibility == Accessibility.Public);
			if (match is not null)
				found.Add(property);
		}

		return found;
	}

	static string HolderOf(string id)
	{
		var builder = new StringBuilder("State");
		foreach (var ch in id)
			builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
		return builder.ToString();
	}

	static string RowField(string screen, INamedTypeSymbol row) =>
		"Row" + HolderOf(screen) + row.Name;

	static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

	sealed class Screen
	{
		public Screen(string id, string type, string state, string holder, INamedTypeSymbol stateSymbol)
		{
			Id = id;
			Type = type;
			State = state;
			Holder = holder;
			StateSymbol = stateSymbol;
		}

		public string Id { get; }

		public string Type { get; }

		public string State { get; }

		public string Holder { get; }

		public INamedTypeSymbol StateSymbol { get; }
	}
}
