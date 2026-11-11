using NativePoc.Devx;

try
{
	if (args.Length > 0 && args[0] == "agent")
		return AgentLoop.Run(args);
	var options = ArgParser.Parse(args);
	if (options.Help)
	{
		Console.WriteLine(ArgParser.Usage);
		return 0;
	}

	return AndroidLoop.Run(options);
}
catch (UsageException ex)
{
	Console.Error.WriteLine(ex.Message);
	Console.Error.WriteLine(ArgParser.Usage);
	return 2;
}
