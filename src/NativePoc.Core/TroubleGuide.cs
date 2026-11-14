using System.Text;

namespace NativePoc;

internal static class TroubleGuide
{
	public readonly record struct Entry(string Title, string Log, string Do);

	public const string Republish = "a new binding needs a republish";

	public static readonly Entry[] Entries =
	[
		new("Corrupt state", StartupGuard.StateLog("corrupt"), "Those bytes are not a checkpoint. The app discards `files/nativepoc-state.bin` and paints the counter."),
		new("Old checkpoint", StartupGuard.StateLog("old-version"), "The file was written by an older build. The app discards `files/nativepoc-state.bin` and paints the counter."),
		new("Unknown root", "startup root unknown", "The `nativepoc_root` extra is not a staged screen. Omit it, or pass a catalog id. The app paints the counter."),
		new("Stale token", "hotreload rejected reason=stale-token", "The process wrote a new `files/nativepoc-hotreload.token`. The watcher reads that file and retries. Save the screen again."),
		new("Rejected program", "startup render rejected", "The baked program is older than this interpreter. The app paints the compiled counter. Rebuild so the catalog is regenerated."),
		new("Program in the nav bundle", "state-restore nav rejected reason=program", "The activity bundle carried program bytes. Release restore keeps screen ids and bag fields only. The app paints the launch screen. Count, name, and flags still restore."),
		new("Republish needed", Republish, "A new `[JniMethod]` is not in this binary. Publish the listener again. A literal or a method that is already registered hot-reloads."),
	];

	public static bool Covers(string logged)
	{
		foreach (var entry in Entries)
		{
			if (logged.Contains(entry.Log, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	public static string Markdown()
	{
		var builder = new StringBuilder();
		builder.Append("# Troubleshooting\n\n");
		builder.Append("The listener logs these lines and still paints a screen.\n\n");
		for (var index = 0; index < Entries.Length; index++)
		{
			var entry = Entries[index];
			builder.Append("## ").Append(entry.Title).Append("\n\n");
			builder.Append('`').Append(entry.Log).Append("`\n\n");
			builder.Append(entry.Do);
			builder.Append(index + 1 == Entries.Length ? "\n" : "\n\n");
		}

		builder.Append("\n## HTTP error\n\n`http-error:`\n\n");
		builder.Append("The body is `cancelled`, `timeout`, a status code, or an exception name. `client=jni` is `HttpURLConnection`. `client=1` is the managed `HttpClient`, which is compiled only when `NativePocHttpManaged` is true. `Pop` cancels only that screen. A cancelled `Exchange` returns `http-error: cancelled` and logs one `http-cancel module=http.` line. It does not log `http-error` first. Another screen's request keeps running. `NativePocVisit` defaults to false. Setting it true compiles `samples/visit` into the showcase and turns on HTTP, location, nav, and persist. `tools/check.sh` does not set it. The Visit and Feed fixtures use `http://127.0.0.1:8094` through `adb reverse`. `10.0.2.2` does not reach a server bound to `127.0.0.1`. `Trim=true` on `System.Net.SocketsHttpHandler.Http3Support` does not remove the UTF-16 switch name from a `.so` that still contains `SocketsHttpHandler`. The default JNI `.so` has no UTF-16 `Http3`. The one ASCII `System.Net.SocketsHttpHandler.Http3Support` is the NativeAOT runtime feature-switch name table, beside `System.Net.Http.UseNativeHttpHandler` and `System.Reflection.Metadata.MetadataUpdater.IsSupported`. The JNI publish does not emit a `RuntimeHostConfigurationOption` for it. That option is compiled only when `NativePocHttpManaged` is true.\n");
		builder.Append("\n## Missing runtime provider\n\n`ClassNotFoundException: net.dot.jni.nativeaot.NativeAotRuntimeProvider`\n\n");
		builder.Append("The dex was packaged without that class. `tools/dex-has-provider.py` fails the build from `_CompileDex`, including when `_CompileToDalvik` was up to date and skipped, and `tools/check.sh` fails the signed apk. Delete `obj` and publish again if the java file was never copied. A no-change android-x64 republish on this tree logged `Skipping target \"_CompileToDalvik\"` and the gate still printed `NativeAotRuntimeProvider is in classes.dex`.\n");
		return builder.ToString();
	}
}
