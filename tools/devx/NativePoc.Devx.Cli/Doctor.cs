namespace NativePoc.Devx;

readonly record struct DoctorFacts(
	string? DotnetVersion,
	string? WorkloadText,
	string? AdbPath,
	string? DevicesText,
	string? IlcPath);

static class Doctor
{
	public static int Run()
	{
		var text = Report(Probe());
		Console.Write(text);
		return text.Contains("FAIL ", StringComparison.Ordinal) ? 1 : 0;
	}

	public static string Report(DoctorFacts facts)
	{
		var lines = new List<string>();
		var sdk = facts.DotnetVersion is not null && facts.DotnetVersion.StartsWith("10.", StringComparison.Ordinal);
		if (facts.DotnetVersion is null)
			lines.Add("FAIL dotnet: the .NET SDK was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and open a new shell.");
		else if (!sdk)
			lines.Add("FAIL dotnet: found " + facts.DotnetVersion + ". This repo targets net10.0. Install the .NET 10 SDK.");
		else
			lines.Add("OK dotnet: " + facts.DotnetVersion);

		var android = facts.WorkloadText is not null && HasAndroidWorkload(facts.WorkloadText);
		if (!sdk)
			lines.Add("FAIL android workload: skipped because the .NET 10 SDK is missing. Install that SDK, then run `dotnet workload install android`.");
		else if (facts.WorkloadText is null)
			lines.Add("FAIL android workload: `dotnet workload list` failed. Run `dotnet workload install android`.");
		else if (!android)
			lines.Add("FAIL android workload: the android workload is not installed. Run `dotnet workload install android`.");
		else
			lines.Add("OK android workload: installed.");

		if (facts.AdbPath is null)
			lines.Add("FAIL adb: adb was not found. Install Android SDK platform-tools and put adb on PATH, or set ANDROID_HOME to the SDK root (platform-tools/adb).");
		else
			lines.Add("OK adb: " + facts.AdbPath);

		if (facts.AdbPath is null)
			lines.Add("FAIL device: skipped because adb is missing.");
		else if (facts.DevicesText is null)
			lines.Add("FAIL device: `adb devices` failed. Check that platform-tools can reach the emulator, then run `adb devices`.");
		else
		{
			var online = DeviceList.Online(facts.DevicesText);
			if (online.Count == 0)
				lines.Add("FAIL device: no emulator or device is online. Run `emulator -list-avds`, then `emulator -avd <name>`, or connect a device. `adb devices` should show a serial followed by the word device.");
			else
				lines.Add("OK device: " + string.Join(", ", online));
		}

		if (!android)
			lines.Add("FAIL NativeAOT: the android workload provides the IL compiler for `dotnet publish -c Release`. Run `dotnet workload install android`.");
		else if (facts.IlcPath is null)
			lines.Add("OK NativeAOT: the android workload is installed. The ILCompiler pack was not in the NuGet cache; the first `dotnet publish -c Release -r android-x64` restores it.");
		else
			lines.Add("OK NativeAOT: " + facts.IlcPath);

		return string.Join('\n', lines) + "\n";
	}

	static DoctorFacts Probe()
	{
		var version = Capture("dotnet", ["--version"]);
		var workloads = Capture("dotnet", ["workload", "list"]);
		var adb = FindAdb();
		string? devices = null;
		if (adb is not null)
		{
			var listed = Capture(adb, ["devices"]);
			if (listed.Ok)
				devices = listed.Text;
		}

		return new DoctorFacts(
			version.Ok ? version.Text.Trim() : null,
			workloads.Ok ? workloads.Text : null,
			adb,
			devices,
			FindIlc());
	}

	static bool HasAndroidWorkload(string text)
	{
		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim();
			if (trimmed.Length < "android".Length || !trimmed.StartsWith("android", StringComparison.Ordinal))
				continue;
			if (trimmed.Length == "android".Length || char.IsWhiteSpace(trimmed["android".Length]))
				return true;
		}

		return false;
	}

	static string? FindAdb()
	{
		if (Capture("adb", ["version"]).Ok)
			return "adb";
		foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
		{
			var root = Environment.GetEnvironmentVariable(variable);
			if (string.IsNullOrEmpty(root))
				continue;
			var candidate = Path.Combine(root, "platform-tools", "adb");
			if (File.Exists(candidate))
				return candidate;
		}

		return null;
	}

	static string? FindIlc()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var nuget = Path.Combine(home, ".nuget", "packages");
		if (!Directory.Exists(nuget))
			return null;
		string? found = null;
		foreach (var dir in Directory.EnumerateDirectories(nuget))
		{
			var name = Path.GetFileName(dir);
			if (!name.Contains("ilcompiler", StringComparison.OrdinalIgnoreCase))
				continue;
			found = dir;
			if (name.Contains("android", StringComparison.OrdinalIgnoreCase))
				return dir;
		}

		return found;
	}

	static (bool Ok, string Text) Capture(string file, string[] args)
	{
		try
		{
			var result = ProcessRunner.Run(file, args);
			return result.Code == 0 ? (true, result.Stdout) : (false, result.Stderr);
		}
		catch (InvalidOperationException)
		{
			return (false, "");
		}
	}
}
