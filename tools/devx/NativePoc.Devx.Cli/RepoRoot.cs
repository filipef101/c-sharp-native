namespace NativePoc.Devx;

static class RepoRoot
{
	public static string Find(string start)
	{
		var dir = new DirectoryInfo(Path.GetFullPath(start));
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("Repository root was not found. Run cnative from the Native PoC checkout.");
	}
}
