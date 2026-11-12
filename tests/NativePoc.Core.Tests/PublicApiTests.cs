using System.Reflection;
using System.Text;

namespace NativePoc.Tests;

public sealed class PublicApiTests
{
	[Fact]
	public void Public_api_matches_the_reviewed_baseline()
	{
		var root = FindRoot();
		var actual = Inventory();
		var baselinePath = Path.Combine(root, "tests", "NativePoc.Core.Tests", "PublicApi.baseline.txt");
		var docPath = Path.Combine(root, "docs", "api.md");
		if (Environment.GetEnvironmentVariable("NATIVEPOC_PUBLICAPI_UPDATE") == "1")
		{
			File.WriteAllText(baselinePath, string.Join('\n', actual) + "\n");
			WriteDoc(docPath, actual);
			return;
		}

		Assert.True(File.Exists(baselinePath), "PublicApi.baseline.txt is missing.");
		var expected = File.ReadAllLines(baselinePath).Where(line => line.Length > 0).ToArray();
		var missing = actual.Except(expected, StringComparer.Ordinal).Take(30).ToArray();
		var extra = expected.Except(actual, StringComparer.Ordinal).Take(30).ToArray();
		Assert.True(
			missing.Length == 0 && extra.Length == 0 && actual.Count == expected.Length,
			"Public API changed. Review the mark, then set NATIVEPOC_PUBLICAPI_UPDATE=1.\nmissing:\n"
			+ string.Join('\n', missing)
			+ "\nextra:\n"
			+ string.Join('\n', extra));
		var doc = File.ReadAllText(docPath);
		var start = doc.IndexOf("<!-- api-baseline -->", StringComparison.Ordinal);
		var end = doc.IndexOf("<!-- /api-baseline -->", StringComparison.Ordinal);
		Assert.True(start >= 0 && end > start, "docs/api.md is missing the api-baseline markers.");
		var listed = doc[(start + "<!-- api-baseline -->".Length)..end].Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
		Assert.Equal(string.Join('\n', actual), listed);
	}

	[Fact]
	public void Android_and_generator_public_types_stay_internal()
	{
		var root = FindRoot();
		var found = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var dir in new[]
		{
			Path.Combine(root, "src", "NativePoc.Android"),
			Path.Combine(root, "src", "NativePoc.ReloadGen"),
		})
		{
			foreach (var file in Directory.GetFiles(dir, "*.cs"))
			{
				foreach (var line in File.ReadLines(file))
				{
					var name = PublicType(line);
					if (name is not null)
						found.Add(name);
				}
			}
		}

		var expected = new[]
		{
			"CompiledScreenGenerator",
			"FeedModule",
			"JniBindingGenerator",
			"MainActivity",
			"PackingActivity",
			"RecreateReceiver",
			"ReloadDispatchGenerator",
		};
		Assert.Equal(expected, found.ToArray());
	}

	static string? PublicType(string line)
	{
		var trimmed = line.Trim();
		if (!trimmed.StartsWith("public ", StringComparison.Ordinal))
			return null;
		string[] kinds = ["class ", "record struct ", "record ", "struct ", "interface ", "enum "];
		foreach (var kind in kinds)
		{
			var at = trimmed.IndexOf(kind, StringComparison.Ordinal);
			if (at < 0)
				continue;
			var name = trimmed[(at + kind.Length)..];
			var end = 0;
			while (end < name.Length && (char.IsLetterOrDigit(name[end]) || name[end] == '_'))
				end++;
			if (end == 0)
				return null;
			return name[..end];
		}

		return null;
	}

	static List<string> Inventory()
	{
		var lines = new List<string>();
		foreach (var assembly in new[] { typeof(Element).Assembly, typeof(Screen).Assembly })
		{
			foreach (var type in assembly.GetTypes())
			{
				if (type.Namespace is null || !type.Namespace.StartsWith("NativePoc", StringComparison.Ordinal))
					continue;
				if (!Exposed(type))
					continue;
				if (type.Name.Contains('<', StringComparison.Ordinal))
					continue;
				var mark = Mark(type);
				lines.Add(mark + "|T:" + Display(type));
				lines.AddRange(Members(type, mark));
			}
		}

		lines.Sort(StringComparer.Ordinal);
		return lines;
	}

	static IEnumerable<string> Members(Type type, string mark)
	{
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		foreach (var ctor in type.GetConstructors(flags))
			yield return mark + "|M:" + Display(type) + ".#ctor(" + Args(ctor) + ")";
		foreach (var field in type.GetFields(flags))
		{
			if (field.IsSpecialName)
				continue;
			yield return mark + "|F:" + Display(type) + "." + field.Name;
		}

		foreach (var property in type.GetProperties(flags))
			yield return mark + "|P:" + Display(type) + "." + property.Name;
		foreach (var ev in type.GetEvents(flags))
			yield return mark + "|E:" + Display(type) + "." + ev.Name;
		foreach (var method in type.GetMethods(flags))
		{
			if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal))
				continue;
			if (method.Name.Contains('<', StringComparison.Ordinal))
				continue;
			var generic = "";
			if (method.IsGenericMethod)
				generic = "<" + string.Join(",", method.GetGenericArguments().Select(static item => item.Name)) + ">";
			yield return mark + "|M:" + Display(type) + "." + method.Name + generic + "(" + Args(method) + ")";
		}
	}

	static string Args(MethodBase method) =>
		string.Join(",", method.GetParameters().Select(static item => TypeName(item.ParameterType)));

	static string TypeName(Type type)
	{
		if (type.IsGenericParameter)
			return type.Name;
		if (type.IsByRef)
			return TypeName(type.GetElementType()!) + "&";
		if (type.IsPointer)
			return TypeName(type.GetElementType()!) + "*";
		if (type.IsArray)
			return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
		if (type.IsGenericType)
		{
			var args = string.Join(",", type.GetGenericArguments().Select(TypeName));
			return Display(type.GetGenericTypeDefinition()) + "<" + args + ">";
		}

		return Display(type);
	}

	static string Display(Type type)
	{
		if (type.IsGenericType)
			type = type.GetGenericTypeDefinition();
		if (type.DeclaringType is { } parent)
			return Display(parent) + "." + Short(type);
		return type.Namespace + "." + Short(type);
	}

	static string Short(Type type)
	{
		var name = type.Name;
		var tick = name.IndexOf('`');
		return tick < 0 ? name : name[..tick];
	}

	static bool Exposed(Type type)
	{
		for (var current = type; current is not null; current = current.DeclaringType)
		{
			var visible = current.IsNested ? current.IsNestedPublic : current.IsPublic;
			if (!visible)
				return false;
		}

		return true;
	}

	static string Mark(Type type)
	{
		var name = Short(type);
		if (Marks.TryGetValue(name, out var mark))
			return mark;
		if (type.DeclaringType is { } parent)
			return Mark(parent);
		throw new InvalidOperationException("Unreviewed public type " + type.FullName);
	}

	static readonly Dictionary<string, string> Marks = new(StringComparer.Ordinal)
	{
		["Element"] = "stable",
		["Column"] = "stable",
		["Text"] = "stable",
		["Button"] = "stable",
		["TextField"] = "stable",
		["TextInput"] = "stable",
		["Switch"] = "stable",
		["ScrollView"] = "stable",
		["ListView"] = "stable",
		["Image"] = "stable",
		["HostView"] = "stable",
		["Style"] = "stable",
		["Align"] = "stable",
		["FontWeight"] = "stable",
		["Keyboard"] = "stable",
		["Submit"] = "stable",
		["ElementKind"] = "stable",
		["Nav"] = "stable",
		["Persist"] = "stable",
		["PersistRecord"] = "stable",
		["Theme"] = "stable",
		["Http"] = "stable",
		["Clipboard"] = "stable",
		["Haptics"] = "stable",
		["Share"] = "stable",
		["Storage"] = "stable",
		["Location"] = "stable",
		["Timer"] = "stable",
		["Animate"] = "stable",
		["LocationAccess"] = "stable",
		["LocationAccessRules"] = "stable",
		["LocationFix"] = "stable",
		["IPlatform"] = "stable",
		["INativeServices"] = "stable",
		["AppState"] = "stable",
		["AppSession"] = "stable",
		["L"] = "stable",
		["StringTable"] = "stable",
		["ReloadCallableAttribute"] = "stable",
		["Motion"] = "stable",
		["Gestures"] = "stable",
		["ScreenNav"] = "stable",
		["NavScreen"] = "stable",
		["FeedRows"] = "experimental",
		["DeferredList"] = "experimental",
		["ViewReconciler"] = "experimental",
		["IElementHost"] = "experimental",
		["RowFace"] = "experimental",
		["RowPrefix"] = "experimental",
		["RowWindowPlan"] = "experimental",
		["RowMetrics"] = "experimental",
		["RowSlotState"] = "experimental",
		["ScreenSnapshot"] = "experimental",
		["ScreenQuery"] = "experimental",
		["StateCheckpoint"] = "experimental",
		["ScreenFreshness"] = "experimental",
		["ImageSource"] = "experimental",
		["ImageSlot"] = "experimental",
		["LruCache"] = "experimental",
		["EffectContext"] = "experimental",
		["EffectGate"] = "experimental",
		["ActionLog"] = "experimental",
		["PlatformConformance"] = "experimental",
		["IPlatformSurface"] = "experimental",
		["HeadlessHost"] = "experimental",
		["HeadlessLayout"] = "experimental",
		["MotionScale"] = "experimental",
		["Screen"] = "experimental",
		["CounterService"] = "experimental",
		["NavFocusState"] = "experimental",
		["NavCheckpoint"] = "experimental",
		["JniTypeAttribute"] = "experimental",
		["JniMethodAttribute"] = "experimental",
		["StyleCodec"] = "internal-ought-to-be",
		["MotionCodec"] = "internal-ought-to-be",
		["MotionMath"] = "internal-ought-to-be",
		["ThemeTokens"] = "internal-ought-to-be",
		["PersistCodec"] = "internal-ought-to-be",
		["TreeDiff"] = "internal-ought-to-be",
		["Patch"] = "internal-ought-to-be",
		["Mount"] = "internal-ought-to-be",
		["Replace"] = "internal-ought-to-be",
		["Unmount"] = "internal-ought-to-be",
		["SetText"] = "internal-ought-to-be",
		["SetButton"] = "internal-ought-to-be",
		["SetTextField"] = "internal-ought-to-be",
		["SetFieldMode"] = "internal-ought-to-be",
		["SetSwitch"] = "internal-ought-to-be",
		["Reorder"] = "internal-ought-to-be",
		["SetList"] = "internal-ought-to-be",
		["SetStyle"] = "internal-ought-to-be",
		["SetMotion"] = "internal-ought-to-be",
		["SetGesture"] = "internal-ought-to-be",
		["SetImage"] = "internal-ought-to-be",
		["SetHost"] = "internal-ought-to-be",
		["ElementIds"] = "internal-ought-to-be",
		["NodePath"] = "internal-ought-to-be",
		["TapTrace"] = "internal-ought-to-be",
		["StateEpoch"] = "internal-ought-to-be",
		["HostPolicy"] = "internal-ought-to-be",
		["HostDispatch"] = "internal-ought-to-be",
		["HostInput"] = "internal-ought-to-be",
		["ActionStoppedException"] = "internal-ought-to-be",
		["PackingCheckpoint"] = "internal-ought-to-be",
		["PackingCheckpointItem"] = "internal-ought-to-be",
		["IPackingProgram"] = "internal-ought-to-be",
		["ScreenSession"] = "internal-ought-to-be",
		["RecordProgram"] = "internal-ought-to-be",
		["ScreenProgram"] = "internal-ought-to-be",
	};

	static void WriteDoc(string path, IReadOnlyList<string> lines)
	{
		var text = File.ReadAllText(path);
		const string open = "<!-- api-baseline -->";
		const string close = "<!-- /api-baseline -->";
		var start = text.IndexOf(open, StringComparison.Ordinal);
		var end = text.IndexOf(close, StringComparison.Ordinal);
		if (start < 0 || end < start)
			throw new InvalidOperationException("docs/api.md is missing the api-baseline markers.");
		var body = new StringBuilder();
		body.Append(text[..(start + open.Length)]);
		body.Append('\n');
		body.Append(string.Join('\n', lines));
		body.Append('\n');
		body.Append(text[end..]);
		File.WriteAllText(path, body.ToString());
	}

	static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}
}
