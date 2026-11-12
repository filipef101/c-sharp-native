using System.IO.Compression;

namespace NativePoc.Devx;

static class AndroidBuild
{
	public static string[] DebugRun(string project, string rid, string serial) =>
	[
		"build",
		project,
		"-c", "Debug",
		"-f", "net10.0-android",
		"-r", rid,
		"-t:Run",
		"-p:AdbTarget=-s " + serial,
	];

	public static string[] DebugBuild(string project, string rid) =>
	[
		"build",
		project,
		"-c", "Debug",
		"-f", "net10.0-android",
		"-r", rid,
	];

	public static string[] AppListenerPublish(string project, string rid) =>
	[
		"publish",
		project,
		"-c", "Debug",
		"-f", "net10.0-android",
		"-r", rid,
		"-p:NativePocDevListener=true",
	];

	public static string[] ListenerPublish(string project, string rid) =>
	[
		"publish",
		project,
		"-c", "Debug",
		"-f", "net10.0-android",
		"-r", rid,
		"-p:NativePocDevListener=true",
		"-p:NativePocHttp=true",
		"-p:NativePocRecycler=true",
		"-p:NativePocLocation=true",
		"-p:NativePocPersist=true",
		"-p:NativePocNav=true",
		"-p:NativePocJni=true",
	];

	public static string PublishDirectory(string repo, string configuration, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "bin", configuration, "net10.0-android", rid, "publish");

	public static string ListenerOutput(string repo, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "bin", "listener", "Debug", "net10.0-android", rid, "publish");

	public static void CleanListenerOutput(string repo)
	{
		var root = Path.Combine(repo, "src", "NativePoc.Android");
		foreach (var name in new[] { "bin", "obj" })
		{
			var path = Path.Combine(root, name, "listener");
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
	}

	public static bool DexHasRuntimeProvider(string apk)
	{
		using var zip = ZipFile.OpenRead(apk);
		var dex = zip.GetEntry("classes.dex");
		if (dex is null)
			return false;
		using var stream = dex.Open();
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.GetBuffer().AsSpan(0, (int)memory.Length).IndexOf("NativeAotRuntimeProvider"u8) >= 0;
	}

	public static string DebugDirectory(string repo, string configuration, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "bin", configuration, "net10.0-android", rid);

	public static string AppPublishDirectory(string root, string configuration, string rid) =>
		Path.Combine(root, "bin", configuration, "net10.0-android", rid, "publish");

	public static string LinkedAssembly(string repo, string configuration, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "obj", configuration, "net10.0-android", rid, "linked", "NativePoc.Android.dll");

	public static string NativeLibraryEntry(string rid) => rid switch
	{
		"android-x64" => "lib/x86_64/libNativePoc.Android.so",
		"android-arm64" => "lib/arm64-v8a/libNativePoc.Android.so",
		_ => throw new InvalidOperationException("Unsupported RID '" + rid + "'."),
	};

	public static string? ExistingSignedApk(string directory)
	{
		if (!Directory.Exists(directory))
			return null;
		var apks = Directory.GetFiles(directory, "*-Signed.apk");
		if (apks.Length != 1)
			return null;
		return apks[0];
	}

	public static string RequireSignedApk(string directory)
	{
		if (!Directory.Exists(directory))
			throw new InvalidOperationException("Missing publish directory " + directory);
		var apks = Directory.GetFiles(directory, "*-Signed.apk");
		if (apks.Length != 1)
			throw new InvalidOperationException("Expected one signed apk in " + directory + ".");
		return apks[0];
	}
}
