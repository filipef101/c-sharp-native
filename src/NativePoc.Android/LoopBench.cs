using System.Diagnostics;
using System.Globalization;
using Android.Util;

namespace NativePoc;

static class LoopBench
{
	public static int Run()
	{
		var n = 0;
		for (var i = 0; i < 1000; i++)
			n++;
		return n;
	}

	public static void Measure()
	{
		var clock = Stopwatch.StartNew();
		var result = Run();
		clock.Stop();
		var micros = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
		Log.Info(
			MainActivity.Tag,
			"loop-compiled us=" + micros.ToString(CultureInfo.InvariantCulture)
			+ " result=" + result.ToString(CultureInfo.InvariantCulture));
	}
}
