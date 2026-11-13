using System.Globalization;
using System.Text;

namespace NativePoc.Devx;

static class CatalogBake
{
	static readonly (string Name, string File)[] Screens =
	[
		("Counter", "src/NativePoc.Screen/Screen.cs"),
		("Packing", "samples/NativePoc.Packing/PackingScreen.cs"),
		("Settings", "samples/NativePoc.Settings/SettingsScreen.cs"),
		("Trips", "samples/NativePoc.Trips/TripsScreen.cs"),
		("Trip", "samples/NativePoc.Trips/TripScreen.cs"),
		("Kit", "samples/NativePoc.Trips/KitScreen.cs"),
		("Web", "samples/NativePoc.Web/WebScreen.cs"),
	];

	public static int Check()
	{
		var problem = CatalogVersion.Problem(File.ReadAllText(CatalogPath(Repo())));
		if (problem is null)
			return 0;
		Console.Error.WriteLine("catalog-check " + problem);
		return 1;
	}

	public static int Sync()
	{
		var repo = Repo();
		var path = CatalogPath(repo);
		var source = File.ReadAllText(path);
		if (CatalogVersion.Problem(source) is null)
			return 0;
		foreach (var (name, file) in Screens)
		{
			var bytes = CatalogVersion.Read(source, name);
			if (bytes is not null && CatalogVersion.Current(bytes))
				continue;
			var lowered = Lower(repo, file);
			if (lowered is null)
			{
				Console.Error.WriteLine("catalog-sync " + name + " did not lower");
				return 1;
			}

			var host = new HeadlessHost();
			try
			{
				host.Apply(lowered, new RecordProgram.Bag());
			}
			catch (InvalidOperationException ex)
			{
				Console.Error.WriteLine("catalog-sync " + name + " " + ex.Message);
				return 1;
			}

			source = Replace(source, name, lowered);
		}

		var problem = CatalogVersion.Problem(source);
		if (problem is not null)
		{
			Console.Error.WriteLine("catalog-sync " + problem);
			return 1;
		}

		File.WriteAllText(path, source);
		Console.WriteLine("catalog-sync wrote " + path);
		return 0;
	}

	static byte[]? Lower(string repo, string relative)
	{
		var file = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(file))
			return null;
		var dir = Path.GetDirectoryName(file)!;
		var companions = Directory.EnumerateFiles(dir, "*.cs")
			.Where(item => !string.Equals(item, file, StringComparison.Ordinal))
			.Select(File.ReadAllText)
			.ToArray();
		var lowered = SubsetLowerer.TryLower(File.ReadAllText(file), companions, originFile: relative);
		return lowered.Ok ? lowered.Program : null;
	}

	static string Replace(string source, string name, byte[] program)
	{
		var body = Format(program);
		var pattern = "static readonly byte[] " + name + " = [";
		var start = source.IndexOf(pattern, StringComparison.Ordinal);
		if (start < 0)
			throw new InvalidOperationException("catalog-sync missing " + name);
		var end = source.IndexOf("];", start, StringComparison.Ordinal);
		if (end < 0)
			throw new InvalidOperationException("catalog-sync open " + name);
		return source[..start] + pattern + "\n" + body + "\n\t];" + source[(end + 2)..];
	}

	static string Format(byte[] program)
	{
		var builder = new StringBuilder();
		for (var index = 0; index < program.Length; index++)
		{
			if (index > 0)
			{
				builder.Append(',');
				if (index % 16 == 0)
					builder.Append("\n\t");
			}
			else
				builder.Append('\t');
			builder.Append(program[index].ToString(CultureInfo.InvariantCulture));
		}

		builder.Append(',');
		return builder.ToString();
	}

	static string CatalogPath(string repo) =>
		Path.Combine(repo, "src", "NativePoc.Android", "NavCatalog.cs");

	static string Repo()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "src", "NativePoc.Android", "NavCatalog.cs")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}
