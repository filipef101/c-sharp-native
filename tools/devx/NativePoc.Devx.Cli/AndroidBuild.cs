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
	];

	public static string PublishDirectory(string repo, string configuration, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "bin", configuration, "net10.0-android", rid, "publish");

	public static string LinkedAssembly(string repo, string configuration, string rid) =>
		Path.Combine(repo, "src", "NativePoc.Android", "obj", configuration, "net10.0-android", rid, "linked", "NativePoc.Android.dll");

	public static string NativeLibraryEntry(string rid) => rid switch
	{
		"android-x64" => "lib/x86_64/libNativePoc.Android.so",
		"android-arm64" => "lib/arm64-v8a/libNativePoc.Android.so",
		_ => throw new InvalidOperationException("Unsupported RID '" + rid + "'."),
	};

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
