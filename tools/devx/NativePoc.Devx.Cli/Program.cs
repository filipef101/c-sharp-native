using NativePoc.Devx;

try
{
	if (args.Length > 0 && args[0] == "agent")
		return AgentLoop.Run(args);
	if (args.Length > 0 && args[0] == "new")
		return NewLoop.Run(args);
	if (args.Length > 0 && args[0] == "lower")
		return LowerLoop.Run(args);
	if (args.Length > 0 && args[0] == "measure")
		return MeasureLoop.Run(args);
	if (args.Length > 0 && args[0] == "test")
		return TestLoop.Run(args);
	if (args.Length > 0 && args[0] == "reset-state")
		return AndroidLoop.ResetState(args);
	if (args.Length > 0 && args[0] == "doctor")
	{
		if (args.Length != 1)
			throw new UsageException("Usage: cnative doctor");
		return Doctor.Run();
	}
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
