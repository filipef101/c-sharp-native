namespace NativePoc.Devx;

static class HotReloadHostClient
{
	public static string TokenPath => Path.Combine(Path.GetTempPath(), "nativepoc-hotreload.token");

	public static string DllPath(string repo) =>
		Path.Combine(repo, "tools", "NativePoc.HotReload.Host", "bin", "Debug", "net10.0", "NativePoc.HotReload.Host.dll");

	public static (int Code, string Stdout, string Stderr) Once(string repo, string tokenHex)
	{
		var dll = DllPath(repo);
		if (!File.Exists(dll))
			throw new InvalidOperationException("The hot reload host is not built: " + dll);
		File.WriteAllText(TokenPath, tokenHex.Trim() + "\n");
		var logPath = Path.Combine(repo, "evidence", "hot-reload-once.log");
		var hadLog = File.Exists(logPath);
		var previous = hadLog ? File.ReadAllBytes(logPath) : null;
		try
		{
			return ProcessRunner.Run("dotnet", [dll, "once"], repo);
		}
		finally
		{
			if (hadLog && previous is not null)
				File.WriteAllBytes(logPath, previous);
			else if (File.Exists(logPath))
				File.Delete(logPath);
		}
	}
}
