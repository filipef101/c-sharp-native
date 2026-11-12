using System.Diagnostics;
using Android.Util;

namespace NativePoc;

[JniType("android/os/SystemClock")]
static partial class DeviceBench
{
	const int Count = 1000;

	[JniMethod("elapsedRealtime", "()J")]
	public static void Direct() => Run("compiled", () =>
	{
		for (var index = 0; index < Count; index++)
			DeviceBattery.Sample();
	});

	[JniMethod("elapsedRealtime", "()J")]
	public static void Registry() => Run("registry", () =>
	{
		var empty = System.Array.Empty<string>();
		for (var index = 0; index < Count; index++)
			JniRegistry.Call("DeviceBattery.Sample", empty);
	});

	[JniMethod("elapsedRealtime", "()J")]
	public static void Measure()
	{
		Direct();
		Registry();
	}

	static void Run(string path, Action body)
	{
		Task.Run(() =>
		{
			var start = Stopwatch.GetTimestamp();
			body();
			var us = (Stopwatch.GetTimestamp() - start) * 1_000_000 / Stopwatch.Frequency;
			Log.Info(
				"NativePoc",
				"jni-bench n=1000 path=" + path + " us=" + us.ToString(System.Globalization.CultureInfo.InvariantCulture) + " note=swiftshader-irrelevant");
			JniSink.Post("bind", "Note", path);
		});
	}
}
