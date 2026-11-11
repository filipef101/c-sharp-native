using System.Globalization;
using NativePoc;

namespace NativePoc.Devx;

static class TestLoop
{
	public static int Run(string[] args)
	{
		string? serial;
		try
		{
			serial = Parse(args);
		}
		catch (UsageException ex)
		{
			Console.Error.WriteLine(ex.Message);
			Console.Error.WriteLine(ArgParser.Usage);
			return 2;
		}

		try
		{
			return Execute(serial);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			Console.Error.WriteLine("cnative: " + ex.Message);
			return 1;
		}
	}

	static string? Parse(IReadOnlyList<string> args)
	{
		if (args.Count < 2 || args[0] != "test" || args[1] != "android")
			throw new UsageException("Usage: cnative test android [--device serial]");
		string? serial = null;
		for (var index = 2; index < args.Count; index++)
		{
			if (args[index] != "--device" || index + 1 >= args.Count)
				throw new UsageException("Usage: cnative test android [--device serial]");
			serial = args[++index];
		}

		return serial;
	}

	static int Execute(string? requested)
	{
		var repo = RepoRoot.Find(Directory.GetCurrentDirectory());
		var directory = Path.Combine(repo, "tests", "device");
		var files = Directory.GetFiles(directory, "*.app.test").OrderBy(path => path, StringComparer.Ordinal).ToArray();
		if (files.Length == 0)
			throw new InvalidOperationException("No tests/device/*.app.test files.");
		var scripts = new List<(string Path, IReadOnlyList<DeviceStep> Steps)>();
		foreach (var file in files)
			scripts.Add((file, DeviceScript.Parse(File.ReadAllText(file))));
		var serial = AndroidLoop.ResolveSerial(requested);
		var rid = Abi.RidFromProperty(Prop(serial, "ro.product.cpu.abi"));
		var session = new ReloadSession();
		try
		{
			session.Token = AndroidLoop.DeployAndLaunch(repo, rid, serial);
			session.Open(repo, serial);
			var warmup = AndroidLoop.SendFile(session);
			if (!warmup.Applied || !warmup.Text.Contains("program=1", StringComparison.Ordinal))
				throw new InvalidOperationException("Warmup was not a program: " + warmup.Text);
			var packing = AndroidLoop.SendPacking(session, repo);
			if (!packing.Applied || !packing.Text.Contains("program=1", StringComparison.Ordinal))
				throw new InvalidOperationException("The packing program was not applied: " + packing.Text);
			foreach (var script in scripts)
			{
				Console.WriteLine("cnative: " + Path.GetFileName(script.Path));
				var failed = RunScript(serial, session, script.Path, script.Steps);
				if (failed != 0)
					return failed;
			}

			return 0;
		}
		finally
		{
			session.Dispose();
		}
	}

	static int RunScript(string serial, ReloadSession session, string path, IReadOnlyList<DeviceStep> steps)
	{
		foreach (var step in steps)
		{
			var ok = step.Verb switch
			{
				DeviceVerb.Screen => Show(serial, session, step.Target),
				DeviceVerb.Enter => Query(session, ScreenQuery.EnterId(step.Target, step.Text), "entered " + step.Target),
				DeviceVerb.Press => Query(session, ScreenQuery.PressId(step.Target), "pressed " + step.Target),
				DeviceVerb.State => Contains(session, ScreenQuery.State, step.Target),
				DeviceVerb.Tree => Contains(session, ScreenQuery.Tree, step.Target),
				_ => Unknown(step.Verb),
			};
			if (ok)
				continue;
			Console.Error.WriteLine(Path.GetFileName(path) + ":" + step.Line.ToString(CultureInfo.InvariantCulture) + " failed");
			return 1;
		}

		return 0;
	}

	static bool Unknown(DeviceVerb verb) =>
		throw new InvalidOperationException("Unknown script step " + verb + ".");

	static bool Show(string serial, ReloadSession session, string screen)
	{
		switch (screen)
		{
			case "counter":
				var resolved = AndroidLoop.Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", LaunchCommand.Package);
				var component = ActivityName.FromResolve(resolved.Stdout)
					?? throw new InvalidOperationException("The launcher activity was not resolved: " + resolved.Stdout);
				var started = AndroidLoop.Adb(serial, "shell", "am", "start", "-n", component);
				if (started.Code != 0)
					throw new InvalidOperationException("The counter did not start: " + started.Stderr + started.Stdout);
				return WaitTree(session, "id=\"name\"");
			case "packing":
				var packing = AndroidLoop.Adb(serial, "shell", "am", "start", "-n", LaunchCommand.Package + "/nativepoc.PackingActivity");
				if (packing.Code != 0)
					throw new InvalidOperationException("Packing did not start: " + packing.Stderr + packing.Stdout);
				return WaitTree(session, "id=\"list\"");
			default:
				throw new InvalidOperationException("Unknown screen " + screen + ".");
		}
	}

	static bool WaitTree(ReloadSession session, string needle)
	{
		var last = "";
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var tree = session.Connection.Apply(ScreenQuery.Ask(ScreenQuery.Tree));
			last = tree.Text;
			if (tree.Ok && tree.Text.Contains(needle, StringComparison.Ordinal))
				return true;
			Thread.Sleep(250);
		}

		Console.Error.WriteLine(last);
		return false;
	}

	static bool Query(ReloadSession session, byte[] payload, string needle)
	{
		var reply = session.Connection.Apply(payload);
		if (reply.Ok && reply.Text.Contains(needle, StringComparison.Ordinal))
			return true;
		Console.Error.WriteLine(reply.Text);
		return false;
	}

	static bool Contains(ReloadSession session, byte op, string needle)
	{
		var reply = session.Connection.Apply(ScreenQuery.Ask(op));
		if (reply.Ok && reply.Text.Contains(needle, StringComparison.Ordinal))
			return true;
		Console.Error.WriteLine(reply.Text);
		return false;
	}

	static string Prop(string serial, string name)
	{
		var result = AndroidLoop.Adb(serial, "shell", "getprop", name);
		if (result.Code != 0 || result.Stdout.Trim().Length == 0)
			throw new InvalidOperationException("adb getprop " + name + " failed: " + result.Stderr + result.Stdout);
		return result.Stdout;
	}
}
