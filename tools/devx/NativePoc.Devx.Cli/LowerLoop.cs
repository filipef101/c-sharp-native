using System.Globalization;
using NativePoc;

namespace NativePoc.Devx;

static class LowerLoop
{
	public static int Run(string[] args)
	{
		try
		{
			return Execute(args);
		}
		catch (Exception ex) when (ex is UsageException or InvalidOperationException or IOException)
		{
			Console.Error.WriteLine(ex.Message);
			if (ex is UsageException)
				Console.Error.WriteLine(ArgParser.Usage);
			return ex is UsageException ? 2 : 1;
		}
	}

	static int Execute(string[] args)
	{
		if (args.Length < 2 || args[0] != "lower")
			throw new UsageException("Usage: cnative lower <file> [--json] [--apply]");
		string? file = null;
		string? serial = null;
		var json = false;
		var apply = false;
		for (var index = 1; index < args.Length; index++)
		{
			var arg = args[index];
			switch (arg)
			{
				case "--json":
					json = true;
					break;
				case "--apply":
					apply = true;
					break;
				case "--device":
					if (index + 1 >= args.Length)
						throw new UsageException("--device needs a serial.");
					serial = args[++index];
					break;
				default:
					if (file is not null)
						throw new UsageException("Usage: cnative lower <file> [--json] [--apply]");
					file = arg;
					break;
			}
		}

		if (file is null)
			throw new UsageException("Usage: cnative lower <file> [--json] [--apply]");
		var source = File.ReadAllText(file);
		var result = SubsetLowerer.TryLower(source);
		if (json)
			Console.WriteLine(SubsetLowerer.ToJson(result));
		else if (!result.Ok)
			Console.Error.WriteLine(result.Diagnostics[0].Id + ": " + result.Diagnostics[0].Message);
		else
			Console.WriteLine("lowered " + result.Program!.Length.ToString(CultureInfo.InvariantCulture));
		if (!result.Ok)
			return 1;
		if (!apply)
			return 0;
		var resolved = AndroidLoop.ResolveSerial(serial);
		var token = AndroidLoop.WaitToken(resolved);
		using var connection = new HotReloadConnection(resolved, token.Hex);
		var reply = connection.Apply(result.Program!);
		Console.WriteLine(reply.Text);
		return reply.Ok ? 0 : 1;
	}
}
