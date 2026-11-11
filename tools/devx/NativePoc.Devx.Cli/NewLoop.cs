using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class NewLoop
{
	public static int Run(string[] args)
	{
		if (args.Length != 2 || args[0] != "new")
			throw new UsageException("Usage: cnative new <name>");
		if (!IsName(args[1]))
			throw new UsageException("Usage: cnative new <name>");
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		Console.WriteLine(Write(repo, args[1]));
		return 0;
	}

	public static bool IsName(string name)
	{
		if (name.Length == 0 || name[0] < 'a' || name[0] > 'z')
			return false;
		foreach (var ch in name)
		{
			if (ch is not ((>= 'a' and <= 'z') or (>= '0' and <= '9')))
				return false;
		}

		return true;
	}

	public static string Write(string root, string name)
	{
		if (!IsName(name))
			throw new UsageException("Usage: cnative new <name>");
		var dir = Path.Combine(root, "samples", name);
		Directory.CreateDirectory(dir);
		var file = Path.Combine(dir, TypeName(name) + "Screen.cs");
		if (File.Exists(file))
			throw new UsageException(file + " already exists.");
		File.WriteAllText(file, Template(name));
		return file;
	}

	public static string TypeName(string name) =>
		char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];

	public static IReadOnlyList<string> AppScreens(string root)
	{
		var samples = Path.Combine(root, "samples");
		if (!Directory.Exists(samples))
			return [];
		var found = new List<string>();
		foreach (var dir in Directory.EnumerateDirectories(samples))
		{
			var name = Path.GetFileName(dir);
			if (!IsName(name))
				continue;
			var relative = "samples/" + name + "/" + TypeName(name) + "Screen.cs";
			if (!File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
				continue;
			if (!ChangeEffectRules.IsAppScreen(relative))
				continue;
			found.Add(relative);
		}

		found.Sort(StringComparer.Ordinal);
		return found;
	}

	public static string Template(string name)
	{
		var type = TypeName(name);
		var builder = new StringBuilder();
		builder.Append("namespace NativePoc.").Append(type).Append(';').Append('\n');
		builder.Append('\n');
		builder.Append("public sealed class ").Append(type).Append("State").Append('\n');
		builder.Append('{').Append('\n');
		builder.Append('\t').Append("public int Count { get; set; }").Append('\n');
		builder.Append('\t').Append("public string Title { get; set; } = \"Hello\";").Append('\n');
		builder.Append('}').Append('\n');
		builder.Append('\n');
		builder.Append("public static class ").Append(type).Append("Screen").Append('\n');
		builder.Append('{').Append('\n');
		builder.Append('\t').Append("public const string ScreenId = \"").Append(name).Append("\";").Append('\n');
		builder.Append('\n');
		builder.Append('\t').Append("public static Element Render(").Append(type).Append("State state, IPlatform platform)").Append('\n');
		builder.Append('\t').Append('{').Append('\n');
		builder.Append('\t').Append('\t').Append("return new Column(").Append('\n');
		builder.Append('\t').Append('\t').Append('\t').Append("new Text(state.Title) { Key = \"title\" },").Append('\n');
		builder.Append('\t').Append('\t').Append('\t').Append("new Text(\"Count \" + state.Count) { Key = \"count\" },").Append('\n');
		builder.Append('\t').Append('\t').Append('\t').Append("new Button(\"Add\", () => { state.Count++; platform.RequestRender(); }, \"Add\") { Key = \"add\" }").Append('\n');
		builder.Append('\t').Append('\t').Append(')').Append('\n');
		builder.Append('\t').Append('\t').Append("{ Key = \"").Append(name).Append("\" };").Append('\n');
		builder.Append('\t').Append('}').Append('\n');
		builder.Append('}').Append('\n');
		return builder.ToString();
	}
}
