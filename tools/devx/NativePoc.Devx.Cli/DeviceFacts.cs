namespace NativePoc.Devx;

static class DeviceList
{
	public static IReadOnlyList<string> Online(string adbDevicesText)
	{
		var list = new List<string>();
		foreach (var line in adbDevicesText.Split('\n'))
		{
			var trimmed = line.Trim().TrimEnd('\r');
			if (trimmed.Length == 0 || trimmed.StartsWith("List of devices", StringComparison.Ordinal))
				continue;
			var parts = trimmed.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && parts[1] == "device")
				list.Add(parts[0]);
		}

		return list;
	}

	public static string Require(IReadOnlyList<string> serials, string? requested)
	{
		if (requested is not null)
		{
			if (!serials.Contains(requested))
				throw new InvalidOperationException("Device " + requested + " is not online.");
			return requested;
		}

		if (serials.Count == 1)
			return serials[0];
		if (serials.Count == 0)
			throw new InvalidOperationException("No emulator or device is online.");
		throw new InvalidOperationException("More than one device is online. Pass --device <serial>.");
	}
}

static class Abi
{
	public static string RidFromProperty(string value)
	{
		var abi = value.Trim();
		return abi switch
		{
			"x86_64" => "android-x64",
			"arm64-v8a" => "android-arm64",
			_ => throw new InvalidOperationException("Unsupported CPU ABI '" + abi + "'. Pass --abi android-x64 or android-arm64."),
		};
	}
}

static class ActivityName
{
	public static string? FromResolve(string text)
	{
		string? found = null;
		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim().TrimEnd('\r');
			if (trimmed.Contains('/') && !trimmed.Contains(' '))
				found = trimmed;
		}

		return found;
	}
}

static class PackageId
{
	public static string FromProject(string project) => FromText(File.ReadAllText(project));

	public static string FromText(string text)
	{
		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim().TrimEnd('\r');
			const string open = "<ApplicationId>";
			const string close = "</ApplicationId>";
			if (!trimmed.StartsWith(open, StringComparison.Ordinal) || !trimmed.EndsWith(close, StringComparison.Ordinal))
				continue;
			var id = trimmed[open.Length..^close.Length].Trim();
			if (id.Length == 0 || id.Contains(' ') || !id.Contains('.'))
				continue;
			return id;
		}

		throw new InvalidOperationException("cnative-app.csproj has no ApplicationId.");
	}
}

static class LaunchCommand
{
	public static string Package =>
		Environment.GetEnvironmentVariable("NATIVEPOC_PACKAGE") is { Length: > 0 } package
			? package.Trim()
			: "com.scratch.nativepoc";

	public static string[] Start(string component) =>
		["shell", "am", "start", "-n", component];

	public static IReadOnlyList<string[]> BeforeStart(string package, string component, string? extras = null)
	{
		var commands = new List<string[]>
		{
			new[] { "shell", "am", "force-stop", package },
		};
		foreach (var relative in TokenFiles.RelativePaths)
			commands.Add(new[] { "shell", "run-as", package, "rm", "-f", relative });
		commands.Add(new[] { "shell", "run-as", package, "rm", "-f", "files/" + HotReloadAddress.SocketFile });
		var start = new List<string> { "shell", "am", "start", "-n", component };
		if (!string.IsNullOrEmpty(extras))
		{
			foreach (var part in extras.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				start.Add(part);
		}

		commands.Add(start.ToArray());
		return commands;
	}

	public static bool PassesToken(IReadOnlyList<string> args)
	{
		for (var i = 0; i < args.Count; i++)
		{
			if (args[i] is "--es" or "--ez" or "--ei" or "--esn")
				return true;
			if (args[i].Contains("nativepoc_hotreload", StringComparison.Ordinal))
				return true;
		}

		return false;
	}
}
