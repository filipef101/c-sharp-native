using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

const int Port = 18765;
const int Samples = 10;
var root = RepoRoot();
var screenFile = Path.Combine(root, "src", "NativePoc.Screen", "Screen.cs");
var original = File.ReadAllText(screenFile);
var logPath = Path.Combine(root, "evidence", "hot-reload-inplace.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
var log = new StringBuilder();
try
{
	var references = References(root);
	Adb("forward", "tcp:" + Port, "tcp:" + Port);
	var warmup = Compile(original, references);
	var warmupReply = Send(warmup);
	Line("warmup bytes=" + warmup.Length + " reply=" + warmupReply);
	PrepareState();
	var pid = Pid();
	Line("pid-before=" + pid);
	var samples = new List<double>();
	for (var i = 0; i < Samples; i++)
	{
		var prefix = "H" + i + " ";
		var source = WithPrefix(original, prefix);
		var started = Stopwatch.GetTimestamp();
		File.WriteAllText(screenFile, source);
		var dll = Compile(source, references);
		var reply = Send(dll);
		var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		var pidNow = Pid();
		var texts = UiTexts();
		var expected = prefix + "1";
		var ok = reply.StartsWith(expected + " ", StringComparison.Ordinal)
			&& pidNow == pid
			&& texts.Contains(expected)
			&& texts.Contains("Ada")
			&& texts.Contains("Hello Ada");
		Line(
			"sample=" + i
			+ " ms=" + elapsedMs.ToString("F1")
			+ " pid=" + pidNow
			+ " reply=" + reply
			+ " ui=" + string.Join(" | ", texts)
			+ " ok=" + ok);
		if (!ok)
			throw new InvalidOperationException("Sample " + i + " did not stay in place.");
		samples.Add(elapsedMs);
		if (i == 0 || i == Samples - 1)
			Shot(Path.Combine(root, "evidence", "hot-reload-h" + i + ".png"));
	}

	var ordered = samples.OrderBy(value => value).ToArray();
	var median = (ordered[4] + ordered[5]) / 2;
	Line("median-ms=" + median.ToString("F1"));
	Line("samples-ms=" + string.Join(",", samples.Select(value => value.ToString("F1"))));
}
finally
{
	File.WriteAllText(screenFile, original);
	File.WriteAllText(logPath, log.ToString());
	Console.Write(log.ToString());
}

void Line(string text)
{
	log.AppendLine(text);
	Console.WriteLine(text);
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
	stream.Write(header);
	stream.Write(dll);
	stream.Flush();
	var response = new byte[5];
	ReadExact(stream, response);
	var ok = response[0] == 1;
	var length = response[1] | (response[2] << 8) | (response[3] << 16) | (response[4] << 24);
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
	if (texts.Contains("Ada") && texts.Contains("Hello Ada") && texts.Any(text => text.EndsWith(" 1", StringComparison.Ordinal)))
		return;
	Adb("shell", "input", "tap", "540", "427");
	Thread.Sleep(300);
	Adb("shell", "input", "text", "Ada");
	Thread.Sleep(600);
	Adb("shell", "input", "keyevent", "4");
	Thread.Sleep(300);
	Adb("shell", "input", "tap", "540", "549");
	Thread.Sleep(600);
	texts = UiTexts();
	if (!texts.Contains("Ada") || !texts.Contains("Hello Ada") || !texts.Contains("Counter 1"))
		throw new InvalidOperationException("Setup did not reach Counter 1 / Ada. UI: " + string.Join(" | ", texts));
}

static string Pid()
{
	var pid = Adb("shell", "pidof", "com.scratch.nativepoc").Trim();
	if (pid.Length == 0)
		throw new InvalidOperationException("The app process is not running.");
	return pid;
}

static List<string> UiTexts()
{
	Adb("shell", "uiautomator", "dump", "/sdcard/nativepoc-ui.xml");
	var local = Path.Combine(Path.GetTempPath(), "nativepoc-ui.xml");
	Adb("pull", "/sdcard/nativepoc-ui.xml", local);
	var xml = File.ReadAllText(local);
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
	if (process.ExitCode != 0)
		throw new InvalidOperationException("adb " + string.Join(" ", args) + " failed: " + stderr + stdout);
	return stdout;
}
