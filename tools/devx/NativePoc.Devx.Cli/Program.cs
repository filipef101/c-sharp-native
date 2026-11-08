using NativePoc.Devx;

try
{
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
