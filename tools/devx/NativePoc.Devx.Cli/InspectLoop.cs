namespace NativePoc.Devx;

static class InspectLoop
{
	public static int Run(string[] args)
	{
		try
		{
			if (args.Length != 2 || args[0] != "inspect")
				throw new UsageException("Usage: cnative inspect <Screen.cs>");
			var json = ScreenInspect.FormatFile(args[1]);
			Console.Write(json);
			return ScreenInspect.Violations(json) == 0 ? 0 : 1;
		}
		catch (Exception ex) when (ex is UsageException or InvalidOperationException or IOException)
		{
			Console.Error.WriteLine(ex.Message);
			if (ex is UsageException)
				Console.Error.WriteLine(ArgParser.Usage);
			return ex is UsageException ? 2 : 1;
		}
	}
}
