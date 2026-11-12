using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NativePoc;
using NativePoc.ReloadGen;

namespace NativePoc.Devx.Tests;

public sealed class ActionParityTests
{
	[Fact]
	public void Corpus_actions_match_compiled_csharp()
	{
		var hello = NewLoop.Template("hello");
		var open = hello.Replace(
			"public int Count { get; set; }",
			"public int Count { get; set; }\n\tpublic bool Open { get; set; }",
			StringComparison.Ordinal);
		Check(hello);
		Check(hello.Replace("state.Count++;", "state.Count += 1;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count -= 1;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count--;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "++state.Count;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = state.Count + 2;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = state.Title + \"!\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title += \"!\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = $\"{state.Title} {state.Count}\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = state.Count == 0 ? \"Zero\" : \"More\";", StringComparison.Ordinal));
		Check(open.Replace("state.Count++;", "state.Open = !state.Open; state.Title = state.Open ? \"Yes\" : \"No\";", StringComparison.Ordinal));
		Check(open.Replace("state.Count++;", "if (state.Open) state.Count += 5; else state.Open = !state.Open;", StringComparison.Ordinal), presses: 2);
		Check(hello.Replace("state.Count++;", "var next = state.Count + 1; var label = state.Title + \" \" + next; state.Count = next; state.Title = label;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 8 / 3;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 7 % 2;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 4; state.Count *= 2;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = state.Count < 1 ? \"yes\" : \"no\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = state.Count > 1 && false ? \"no\" : \"yes\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 3; state.Title = $\"{state.Count * 2}\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 0; var n = state.Count++; state.Title = $\"{n}\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 0; var n = ++state.Count; state.Title = $\"{n}\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"ab\".ToUpper();", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"AB\".ToLower();", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"hello\".Contains(\"ell\") ? \"yes\" : \"no\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"hello\".StartsWith(\"he\") ? \"yes\" : \"no\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"hello\".Substring(1, 2);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = \"hello\".Length;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = Math.Abs(-4);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = Math.Min(3, 1);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = Math.Max(3, 1);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = (int)Math.Round(1.6);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "var x = 1.5 * 4.0; state.Count = (int)x;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"  Hello  \".Trim();", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"  hi  \"; state.Title = state.Title.Trim();", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "string label = null; state.Title = label ?? \"kept\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "string label = null; state.Title = label?.Trim() ?? \"empty\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"12\"; state.Count = int.Parse(state.Title);", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "if (int.TryParse(\"9\", out var n)) state.Count = n;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Count = 0; while (state.Count < 3) state.Count++;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "var n = 0; for (var i = 0; i < 4; i++) n++; state.Count = n;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = $\"{1.5}\";", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "int n = 2147483647; state.Count = n + 1;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "var huge = 1e20; state.Count = (int)huge;", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "var missing = 0.0 / 0.0; state.Count = (int)missing;", StringComparison.Ordinal));
		Check(ListScreen("state.Items.Add(new Note { Name = \"Lantern\", Packed = true });"));
		Check(ListScreen("state.Items.Add(new Note { Name = \"2\" }); state.Items.Add(new Note { Name = \"4\" }); var sum = 0; foreach (var item in state.Items) sum += int.Parse(item.Name); state.Count = sum;"));
		Check(ListScreen("state.Items.Add(new Note { Name = \"Lantern\" }); state.Items.Add(new Note { Name = \"Rope\" }); foreach (var item in state.Items) if (item.Name == \"Lantern\") state.Items.Remove(item);"));
	}

	[Fact]
	public void Try_catch_finally_matches_compiled_csharp()
	{
		var hello = NewLoop.Template("hello");
		Check(hello.Replace("state.Count++;", "try { state.Count = 2; } finally { state.Title = \"fin\"; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "int z = 0; try { state.Count = 1 / z; } catch (DivideByZeroException) { state.Count = 3; } finally { state.Title = \"done\"; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "int z = 0; try { state.Count = 1 / z; } catch (ArithmeticException) { state.Count = 3; } finally { state.Count = state.Count + 1; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "try { state.Count = 1; if (state.Title.StartsWith(\"H\")) throw new InvalidOperationException(\"down\"); state.Count = 9; } catch (InvalidOperationException) { state.Count = 3; } finally { state.Title = \"done\"; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "state.Title = \"no\"; try { if (state.Title.StartsWith(\"H\")) throw new InvalidOperationException(\"down\"); state.Count = 9; } catch (InvalidOperationException) { state.Count = 3; } finally { state.Title = \"ok\"; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "try { if (state.Title.StartsWith(\"H\")) throw new InvalidOperationException(\"down\"); } catch (FormatException) { state.Count = 1; } catch (InvalidOperationException) { state.Count = 4; }", StringComparison.Ordinal));
		Check(hello.Replace("state.Count++;", "int z = 0; try { state.Count = 1 / z; } catch { state.Count = 5; }", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "try { if (state.Title.StartsWith(\"H\")) throw new InvalidOperationException(\"down\"); } catch (FormatException) { state.Count = 7; }", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "try { if (state.Title.StartsWith(\"H\")) throw new InvalidOperationException(\"down\"); } finally { throw new InvalidOperationException(\"fin\"); }", StringComparison.Ordinal));
	}

	[Fact]
	public void Stops_use_the_csharp_exception_message()
	{
		var hello = NewLoop.Template("hello");
		Stop(hello.Replace("state.Count++;", "int z = 0; state.Count = 1 / z;", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "int z = 0; state.Count = 1 % z;", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "int n = 2147483647; n = n + 1; state.Count = n / -1;", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "state.Title = \"hi\".Substring(0, 5);", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "state.Count = int.Parse(\"no\");", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "int n = 2147483647; n = n + 1; state.Count = Math.Abs(n);", StringComparison.Ordinal));
		Stop(hello.Replace("state.Count++;", "string label = null; state.Title = label.Trim();", StringComparison.Ordinal));
		var hello2 = NewLoop.Template("hello");
		Check(hello2.Replace("state.Count++;", "state.Title = $\"{1.0 / 0.0}\";", StringComparison.Ordinal));
	}

	static void Check(string source, int presses = 1)
	{
		var compiled = Compile(source);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line + ":" + lowered.Diagnostics[0].Column);
		var session = ScreenSession.Open(lowered.Program!);
		Assert.Equal(compiled.Tree(), session.Tree());
		for (var i = 0; i < presses; i++)
		{
			compiled.Press();
			session.Tap(compiled.Button);
			Assert.True(compiled.Tree() == session.Tree(), "STOP=" + ActionLog.LastStop + "\n" + compiled.Tree() + "\n---\n" + session.Tree());
			Assert.Equal(compiled.StateText(), Snapshot(session));
		}
	}

	static void Stop(string source)
	{
		var compiled = Compile(source);
		var lowered = SubsetLowerer.TryLower(source);
		Assert.True(lowered.Ok, lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message + " @" + lowered.Diagnostics[0].Line + ":" + lowered.Diagnostics[0].Column);
		var session = ScreenSession.Open(lowered.Program!);
		Exception? thrown = null;
		try
		{
			compiled.Press();
		}
		catch (Exception ex)
		{
			thrown = ex;
		}

		Assert.NotNull(thrown);
		session.Tap(compiled.Button);
		Assert.Equal(thrown.Message, ActionLog.LastStop);
	}

	static string Snapshot(ScreenSession session)
	{
		var bag = session.Bag;
		var builder = new StringBuilder();
		foreach (var name in new[] { "Added", "Count", "Draft", "Open", "Title" })
		{
			if (bag.TryReadInt(name, out var number))
				builder.Append(name).Append('=').Append(number.ToString(CultureInfo.InvariantCulture)).Append(';');
			else if (bag.TryReadString(name, out var text))
				builder.Append(name).Append('=').Append(text).Append(';');
			else if (bag.TryReadBool(name, out var flag))
				builder.Append(name).Append('=').Append(flag ? "true" : "false").Append(';');
		}

		var rows = bag.Rows("Items");
		if (rows.Count > 0)
		{
			builder.Append("Items=");
			foreach (var row in rows)
				builder.Append('[').Append(string.Join(',', row)).Append(']');
		}

		return builder.ToString();
	}

	static Compiled Compile(string source)
	{
		var text = "using System;\nusing System.Collections.Generic;\nusing NativePoc;\n" + source;
		var parsed = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
		var shaped = new ColumnArgs().Visit(parsed.GetRoot())!.ToFullString();
		var tree = CSharpSyntaxTree.ParseText(ForeachSnapshot.RewriteSource(shaped), (CSharpParseOptions)parsed.Options);
		var compilation = CSharpCompilation.Create(
			"parity-" + Guid.NewGuid().ToString("N"),
			[tree],
			References(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
		using var pe = new MemoryStream();
		var emitted = compilation.Emit(pe);
		if (!emitted.Success)
		{
			var message = string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
			Assert.Fail(message);
		}

		var assembly = Assembly.Load(pe.ToArray());
		var screen = assembly.GetTypes().Single(type => type.GetMethod("Render") is not null);
		var stateType = screen.GetMethod("Render")!.GetParameters()[0].ParameterType;
		var state = Activator.CreateInstance(stateType)!;
		var platform = new CompiledPlatform();
		var root = (Element)screen.GetMethod("Render")!.Invoke(null, [state, platform])!;
		return new Compiled(state, root, platform);
	}

	static MetadataReference[] References()
	{
		var core = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		return
		[
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Element).Assembly.Location),
			MetadataReference.CreateFromFile(Path.Combine(core, "System.Runtime.dll")),
			MetadataReference.CreateFromFile(Path.Combine(core, "System.Collections.dll")),
		];
	}

	static string ListScreen(string handler) =>
		"""
		namespace NativePoc.Notes;

		public sealed class Note
		{
			public string Name { get; set; } = "";
			public bool Packed { get; set; }
		}

		public sealed class NotesState
		{
			public List<Note> Items { get; } = [];
			public int Count { get; set; }
			public int Added { get; set; }
			public bool Open { get; set; }
		}

		public static class NotesScreen
		{
			public const string ScreenId = "notes";

			public static Element Render(NotesState state, IPlatform platform)
			{
				return new Column(
					new Text("Count " + state.Count) { Key = "count" },
					new Button("Add", () => { __HANDLER__ platform.RequestRender(); }, "Add") { Key = "add" }
				) { Key = "notes" };
			}
		}
		""".Replace("__HANDLER__", handler + " ", StringComparison.Ordinal);

	sealed class Compiled
	{
		readonly object _state;
		Element _root;
		readonly CompiledPlatform _platform;
		readonly MethodInfo _render;
		readonly Type _screen;

		public Compiled(object state, Element root, CompiledPlatform platform)
		{
			_state = state;
			_root = root;
			_platform = platform;
			_screen = state.GetType().Assembly.GetTypes().Single(type => type.GetMethod("Render") is not null);
			_render = _screen.GetMethod("Render")!;
			Button = ScreenSnapshot.FormatTree(root).Contains("id=\"hello/add\"", StringComparison.Ordinal) ? "hello/add" : "notes/add";
		}

		public string Button { get; }

		public string Tree() => ScreenSnapshot.FormatTree(_root);

		public void Press()
		{
			var nodes = ElementIds.Flatten(_root).Where(node => node.Element is Button).ToArray();
			var chosen = nodes.FirstOrDefault(node => node.Id == Button);
			var button = chosen.Element as Button ?? nodes.Select(node => node.Element).OfType<Button>().Single();
			try
			{
				button.OnPress();
			}
			catch (TargetInvocationException ex) when (ex.InnerException is not null)
			{
				throw ex.InnerException;
			}

			if (_platform.Render)
				_root = (Element)_render.Invoke(null, [_state, _platform])!;
		}

		public string StateText()
		{
			var builder = new StringBuilder();
			var properties = _state.GetType().GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal);
			foreach (var property in properties)
			{
				if (property.PropertyType == typeof(int))
					builder.Append(property.Name).Append('=').Append(((int)property.GetValue(_state)!).ToString(CultureInfo.InvariantCulture)).Append(';');
				else if (property.PropertyType == typeof(bool))
					builder.Append(property.Name).Append('=').Append((bool)property.GetValue(_state)! ? "true" : "false").Append(';');
				else if (property.PropertyType == typeof(string))
					builder.Append(property.Name).Append('=').Append(property.GetValue(_state) ?? "").Append(';');
			}

			foreach (var property in properties.Where(property => property.PropertyType.IsGenericType))
			{
				var rows = ((System.Collections.IEnumerable)property.GetValue(_state)!).Cast<object>().ToArray();
				if (rows.Length == 0)
					continue;
				builder.Append(property.Name).Append('=');
				foreach (var row in rows)
				{
					builder.Append('[');
					var cells = row.GetType().GetProperties().Select(cell => cell.PropertyType == typeof(bool)
						? ((bool)cell.GetValue(row)! ? "true" : "false")
						: cell.GetValue(row)?.ToString() ?? "");
					builder.Append(string.Join(',', cells));
					builder.Append(']');
				}
			}

			return builder.ToString();
		}
	}

	sealed class ColumnArgs : CSharpSyntaxRewriter
	{
		public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
			if (visited.Type.ToString() != "Column" || visited.ArgumentList is null || visited.ArgumentList.Arguments.Count < 2)
				return visited;
			var elements = visited.ArgumentList.Arguments.Select(argument => (CollectionElementSyntax)SyntaxFactory.ExpressionElement(argument.Expression));
			var collection = SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList(elements));
			return visited.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(collection))));
		}
	}

	sealed class CompiledPlatform : IPlatform
	{
		public bool Render { get; private set; }

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender() => Render = true;

		public void Push(string screen)
		{
		}

		public void Pop()
		{
		}

		public void Effect(string property, string resource)
		{
		}

		public void Module(string module, string method, string[] args)
		{
		}
	}
}
