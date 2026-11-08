using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

const int Port = 18765;
const int Samples = 20;
if (args.Length != 1 || args[0] is not ("a" or "b" or "c" or "once"))
{
	Console.Error.WriteLine("Usage: NativePoc.HotReload.Host <a|b|c|once>");
	return 2;
}

var mode = args[0];
var root = RepoRoot();
var screenFile = Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs");
var original = File.ReadAllText(screenFile);
var logPath = Path.Combine(root, "evidence", "hot-reload-" + mode + ".log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
var log = new StringBuilder();
var exit = 0;
try
{
	switch (mode)
	{
		case "a":
			MeasureEndToEnd(root, original);
			break;
		case "b":
			MeasureTransfer(root, original);
			break;
		case "c":
			MeasureCompile(original, root);
			break;
		case "once":
			SendOnce(root, screenFile);
			break;
		default:
			exit = 2;
			break;
	}
}
catch (Exception ex)
{
	Line("exception=" + ex.GetType().Name + " " + ex.Message);
	exit = 1;
}
finally
{
	if (mode is "a" or "b")
		File.WriteAllText(screenFile, original);
	File.WriteAllText(logPath, log.ToString());
}

return exit;

void MeasureEndToEnd(string repo, string source)
{
	Line("clock=file-write then Roslyn compile then abstract-socket send through the post-render reply");
	Line("cold=sample 0, the first load after process start; no compile and no send before it");
	var references = References(repo);
	RestartApp();
	PrepareState();
	var pid = Pid();
	Line("pid=" + pid);
	var samples = new List<double>();
	for (var i = 0; i < Samples; i++)
	{
		var prefix = "A" + i.ToString("00") + " ";
		var next = WithPrefix(source, prefix);
		var started = Stopwatch.GetTimestamp();
		File.WriteAllText(screenFile, next);
		var dll = Compile(next, references);
		var reply = Send(dll);
		var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		samples.Add(elapsedMs);
		var pidNow = Pid();
		var ui = UiTexts();
		var expected = prefix.TrimEnd() + " 1";
		var ok = reply.StartsWith(expected + " ", StringComparison.Ordinal)
			&& pidNow == pid
			&& ui.Contains(expected)
			&& ui.Contains("Ada")
			&& ui.Contains("Hello Ada");
		Line(
			"sample=" + i
			+ " ms=" + elapsedMs.ToString("F1")
			+ " pid=" + pidNow
			+ " reply=" + reply
			+ " ui=" + string.Join(" | ", ui)
			+ " ok=" + ok);
		if (i == 0)
			Shot(Path.Combine(repo, "evidence", "hot-reload-a-cold.png"));
		if (i == Samples - 1)
			Shot(Path.Combine(repo, "evidence", "hot-reload-a-warm.png"));
		if (!ok)
			throw new InvalidOperationException("Sample " + i + " did not stay in place.");
	}

	Summarize(samples);
}

void MeasureTransfer(string repo, string source)
{
	Line("clock=abstract-socket send of an already compiled DLL through the post-render reply");
	Line("compile and file write happen before the clock; the app is restarted after the DLLs exist");
	var references = References(repo);
	var dlls = new byte[Samples][];
	for (var i = 0; i < Samples; i++)
	{
		var prefix = "B" + i.ToString("00") + " ";
		dlls[i] = Compile(WithPrefix(source, prefix), references);
		Line("precompiled=" + i + " bytes=" + dlls[i].Length);
	}

	RestartApp();
	PrepareState();
	var pid = Pid();
	Line("pid=" + pid);
	var samples = new List<double>();
	for (var i = 0; i < Samples; i++)
	{
		var prefix = "B" + i.ToString("00") + " ";
		File.WriteAllText(screenFile, WithPrefix(source, prefix));
		var started = Stopwatch.GetTimestamp();
		var reply = Send(dlls[i]);
		var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		samples.Add(elapsedMs);
		var pidNow = Pid();
		var ui = UiTexts();
		var expected = prefix.TrimEnd() + " 1";
		var ok = reply.StartsWith(expected + " ", StringComparison.Ordinal)
			&& pidNow == pid
			&& ui.Contains(expected)
			&& ui.Contains("Ada")
			&& ui.Contains("Hello Ada");
		Line(
			"sample=" + i
			+ " ms=" + elapsedMs.ToString("F1")
			+ " pid=" + pidNow
			+ " reply=" + reply
			+ " ui=" + string.Join(" | ", ui)
			+ " ok=" + ok);
		if (!ok)
			throw new InvalidOperationException("Sample " + i + " did not stay in place.");
	}

	Summarize(samples);
}

void MeasureCompile(string source, string repo)
{
	Line("clock=Roslyn compile of an in-memory source string; no file write and no socket");
	Line("cold=sample 0, the first compilation in this process");
	var references = References(repo);
	var samples = new List<double>();
	for (var i = 0; i < Samples; i++)
	{
		var next = WithPrefix(source, "C" + i.ToString("00") + " ");
		var started = Stopwatch.GetTimestamp();
		var dll = Compile(next, references);
		var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		samples.Add(elapsedMs);
		Line("sample=" + i + " ms=" + elapsedMs.ToString("F1") + " bytes=" + dll.Length);
	}

	Summarize(samples);
}

void SendOnce(string repo, string path)
{
	var references = References(repo);
	var source = File.ReadAllText(path);
	var dll = Compile(source, references);
	LoadToken();
	ForwardAbstract();
	var started = Stopwatch.GetTimestamp();
	var reply = Send(dll);
	var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
	Line("once-ms=" + elapsedMs.ToString("F1") + " bytes=" + dll.Length + " reply=" + reply);
	Line("ui=" + string.Join(" | ", UiTexts()));
	Line("pid=" + Pid());
}

void Summarize(List<double> samples)
{
	Line("samples-ms=" + string.Join(",", samples.Select(value => value.ToString("F1"))));
	Line("cold-ms=" + samples[0].ToString("F1"));
	Line("p50-all-ms=" + Median(samples).ToString("F1"));
	Line("p50-warm-ms=" + Median(samples.Skip(1).ToList()).ToString("F1"));
	Line("min-ms=" + samples.Min().ToString("F1"));
	Line("max-ms=" + samples.Max().ToString("F1"));
}

void Line(string text)
{
	log.AppendLine(text);
	Console.WriteLine(text);
}

static double Median(IReadOnlyList<double> values)
{
	var ordered = values.OrderBy(value => value).ToArray();
	var count = ordered.Length;
	if (count == 0)
		throw new InvalidOperationException("No samples.");
	if (count % 2 == 1)
		return ordered[count / 2];
	return (ordered[count / 2 - 1] + ordered[count / 2]) / 2;
}

static string RepoRoot()
{
	foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
	{
		var dir = new DirectoryInfo(start);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}
	}

	throw new InvalidOperationException("Repository root was not found.");
}

static string WithPrefix(string source, string prefix)
{
	const string marker = "public const string TitlePrefix = \"";
	var start = source.IndexOf(marker, StringComparison.Ordinal);
	if (start < 0)
		throw new InvalidOperationException("TitlePrefix was not found.");
	start += marker.Length;
	var end = source.IndexOf('"', start);
	if (end < 0)
		throw new InvalidOperationException("TitlePrefix value was not closed.");
	return source[..start] + prefix + source[end..];
}

static IReadOnlyList<MetadataReference> References(string root)
{
	var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
		?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
	var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
	var refDir = Directory.GetDirectories(packRoot)
		.Select(dir => Path.Combine(dir, "ref", "net10.0"))
		.FirstOrDefault(Directory.Exists)
		?? throw new InvalidOperationException("The net10.0 reference pack was not found.");
	var core = Path.Combine(root, "src", "NativePoc.Core", "bin", "Debug", "net10.0", "NativePoc.Core.dll");
	if (!File.Exists(core))
		throw new InvalidOperationException("Build NativePoc.Core before measuring hot reload.");
	return Directory.GetFiles(refDir, "*.dll")
		.Select(path => MetadataReference.CreateFromFile(path))
		.Append(MetadataReference.CreateFromFile(core))
		.ToArray();
}

static byte[] Compile(string source, IReadOnlyList<MetadataReference> references)
{
	var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
	var compilation = CSharpCompilation.Create(
		"NativePoc.Screen",
		[tree],
		references,
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
	using var pe = new MemoryStream();
	var result = compilation.Emit(pe);
	if (!result.Success)
	{
		var errors = string.Join(
			Environment.NewLine,
			result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
		throw new InvalidOperationException(errors);
	}

	return pe.ToArray();
}

static string Send(byte[] dll)
{
	using var client = new TcpClient();
	client.NoDelay = true;
	client.ReceiveTimeout = 15000;
	client.SendTimeout = 15000;
	client.Connect(IPAddress.Loopback, Port);
	var stream = client.GetStream();
	var header = new byte[4];
	header[0] = (byte)dll.Length;
	header[1] = (byte)(dll.Length >> 8);
	header[2] = (byte)(dll.Length >> 16);
	header[3] = (byte)(dll.Length >> 24);
	if (HotReloadSession.Token.Length != 16)
		throw new InvalidOperationException("The hot reload token is missing.");
	stream.Write(HotReloadSession.Token);
	stream.Write(header);
	stream.Write(dll);
	stream.Flush();
	var response = new byte[5];
	ReadExact(stream, response);
	var ok = response[0] == 1;
	var length = response[1] | (response[2] << 8) | (response[3] << 16) | (response[4] << 24);
	if (length < 0 || length > 1024 * 1024)
		throw new InvalidOperationException("Rejected reply length " + length + ".");
	var body = new byte[length];
	ReadExact(stream, body);
	var text = Encoding.UTF8.GetString(body);
	if (!ok)
		throw new InvalidOperationException(text);
	return text;
}

static void ReadExact(Stream stream, byte[] buffer)
{
	var offset = 0;
	while (offset < buffer.Length)
	{
		var read = stream.Read(buffer, offset, buffer.Length - offset);
		if (read == 0)
			throw new EndOfStreamException("The hot reload connection closed early.");
		offset += read;
	}
}

static void PrepareState()
{
	var texts = UiTexts();
	if (texts.Contains("Ada") && texts.Contains("Hello Ada") && texts.Contains("Counter 1"))
		return;
	var edit = Center("android.widget.EditText");
	var button = Center("android.widget.Button");
	Adb("shell", "input", "tap", edit.X.ToString(), edit.Y.ToString());
	Thread.Sleep(400);
	Adb("shell", "input", "text", "Ada");
	Thread.Sleep(700);
	Adb("shell", "input", "keyevent", "4");
	Thread.Sleep(400);
	button = Center("android.widget.Button");
	Adb("shell", "input", "tap", button.X.ToString(), button.Y.ToString());
	Thread.Sleep(700);
	texts = UiTexts();
	if (!texts.Contains("Ada") || !texts.Contains("Hello Ada") || !texts.Contains("Counter 1"))
		throw new InvalidOperationException("Setup did not reach Counter 1 / Ada. UI: " + string.Join(" | ", texts));
}

static (int X, int Y) Center(string className)
{
	var xml = LastUiXml();
	foreach (var node in System.Text.RegularExpressions.Regex.Matches(xml, "<node [^>]+>").Select(match => match.Value))
	{
		if (!node.Contains("class=\"" + className + "\"", StringComparison.Ordinal))
			continue;
		var match = System.Text.RegularExpressions.Regex.Match(node, "bounds=\"\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]\"");
		if (!match.Success)
			continue;
		var left = int.Parse(match.Groups[1].Value);
		var top = int.Parse(match.Groups[2].Value);
		var right = int.Parse(match.Groups[3].Value);
		var bottom = int.Parse(match.Groups[4].Value);
		return ((left + right) / 2, (top + bottom) / 2);
	}

	throw new InvalidOperationException("No " + className + " in the UI dump.");
}

static string LastUiXml()
{
	Adb("shell", "uiautomator", "dump", "/sdcard/nativepoc-ui.xml");
	var local = Path.Combine(Path.GetTempPath(), "nativepoc-ui.xml");
	Adb("pull", "/sdcard/nativepoc-ui.xml", local);
	return File.ReadAllText(local);
}

static void RestartApp()
{
	Adb("logcat", "-c");
	Adb("shell", "am", "force-stop", "com.scratch.nativepoc");
	Thread.Sleep(400);
	Adb("shell", "am", "start", "-n", LauncherComponent());
	for (var i = 0; i < 50; i++)
	{
		Thread.Sleep(200);
		var listed = AdbAllowFail("shell", "pidof", "com.scratch.nativepoc").Trim();
		var logged = AdbAllowFail("logcat", "-d", "-s", "NativePoc:I");
		if (listed.Length > 0 && logged.Contains("hotreload-listen", StringComparison.Ordinal))
		{
			ReadDeviceToken();
			var after = AdbAllowFail("logcat", "-d", "-s", "NativePoc:V");
			if (after.Contains(HotReloadSession.Hex, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The hot reload token was written to logcat.");
			ForwardAbstract();
			return;
		}
	}

	throw new InvalidOperationException("The app did not open the hot reload socket.");
}

static string LauncherComponent()
{
	var output = Adb(
		"shell",
		"cmd",
		"package",
		"resolve-activity",
		"--brief",
		"-c",
		"android.intent.category.LAUNCHER",
		"com.scratch.nativepoc");
	foreach (var line in output.Split('\n'))
	{
		var trimmed = line.Trim();
		if (trimmed.Contains('/'))
			return trimmed;
	}

	throw new InvalidOperationException("The launcher activity was not found.");
}

static void LoadToken()
{
	if (HotReloadSession.Token.Length == 16)
		return;
	if (File.Exists(HotReloadSession.TokenPath))
	{
		var saved = File.ReadAllText(HotReloadSession.TokenPath).Trim();
		if (saved.Length == 32)
		{
			HotReloadSession.Hex = saved;
			HotReloadSession.Token = Convert.FromHexString(saved);
			return;
		}
	}

	ReadDeviceToken();
}

static void ReadDeviceToken()
{
	var hex = Adb("shell", "run-as", "com.scratch.nativepoc", "cat", "files/nativepoc-hotreload.token").Trim();
	if (hex.Length != 32)
		throw new InvalidOperationException("The app hot reload token was not readable.");
	HotReloadSession.Hex = hex;
	HotReloadSession.Token = Convert.FromHexString(hex);
	File.WriteAllText(HotReloadSession.TokenPath, hex);
}

static void ForwardAbstract()
{
	LoadToken();
	AdbAllowFail("forward", "--remove", "tcp:" + Port);
	Adb("forward", "tcp:" + Port, "localabstract:npoc-hotreload");
}

static string Pid()
{
	var pid = AdbAllowFail("shell", "pidof", "com.scratch.nativepoc").Trim();
	if (pid.Length == 0)
		throw new InvalidOperationException("The app process is not running.");
	return pid;
}

static List<string> UiTexts()
{
	var xml = LastUiXml();
	var texts = new List<string>();
	var cursor = 0;
	const string marker = "text=\"";
	while (true)
	{
		var start = xml.IndexOf(marker, cursor, StringComparison.Ordinal);
		if (start < 0)
			break;
		start += marker.Length;
		var end = xml.IndexOf('"', start);
		if (end < 0)
			break;
		var text = xml[start..end];
		if (text.Length > 0)
			texts.Add(text);
		cursor = end + 1;
	}

	return texts;
}

static void Shot(string path)
{
	Adb("shell", "screencap", "-p", "/sdcard/nativepoc-shot.png");
	Adb("pull", "/sdcard/nativepoc-shot.png", path);
}

static string Adb(params string[] args)
{
	var (code, stdout, stderr) = AdbRaw(args);
	if (code != 0)
		throw new InvalidOperationException("adb " + string.Join(" ", args) + " failed: " + stderr + stdout);
	return stdout;
}

static string AdbAllowFail(params string[] args)
{
	var (_, stdout, _) = AdbRaw(args);
	return stdout;
}

static (int Code, string Stdout, string Stderr) AdbRaw(string[] args)
{
	var start = new ProcessStartInfo("adb")
	{
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		UseShellExecute = false,
	};
	foreach (var arg in args)
		start.ArgumentList.Add(arg);
	using var process = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
	var stdout = process.StandardOutput.ReadToEnd();
	var stderr = process.StandardError.ReadToEnd();
	process.WaitForExit();
	return (process.ExitCode, stdout, stderr);
}

static class HotReloadSession
{
	public static byte[] Token { get; set; } = [];

	public static string Hex { get; set; } = "";

	public static string TokenPath => Path.Combine(Path.GetTempPath(), "nativepoc-hotreload.token");
}
