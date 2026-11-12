using Android.Content;
using Android.OS;
using Android.Util;

namespace NativePoc;

[JniType("android/os/BatteryManager")]
static partial class DeviceBattery
{
	[JniMethod("getIntProperty", "(I)I", Blocking = true)]
	public static string Level()
	{
		var reading = BatteryReading.FromCapacity(ReadCapacity());
		Log.Info("NativePoc", "jni-bind class=android/os/BatteryManager method=getIntProperty known=" + (reading.Known ? "1" : "0") + " text=" + reading.Text);
		return reading.Text;
	}

	[JniMethod("getIntProperty", "(I)I")]
	public static string Sample() => BatteryReading.FromCapacity(ReadCapacity()).Text;

	[JniMethod("getIntProperty", "(I)I")]
	public static void Read(string screen, string field)
	{
		var targetScreen = screen;
		var targetField = field;
		Task.Run(() =>
		{
			var text = Level();
			JniSink.Post(targetScreen, targetField, text);
		});
	}

	[JniMethod("EXTRA_PLUGGED", "(I)I")]
	public static void Plugged(string screen, string field)
	{
		var targetScreen = screen;
		var targetField = field;
		Task.Run(() =>
		{
			var text = ReadPlugged();
			Log.Info("NativePoc", "jni-bind class=android/os/BatteryManager method=EXTRA_PLUGGED text=" + text);
			JniSink.Post(targetScreen, targetField, text);
		});
	}

	static int ReadCapacity()
	{
		var manager = Host().GetSystemService(Context.BatteryService) as BatteryManager;
		return manager?.GetIntProperty((int)BatteryProperty.Capacity) ?? -1;
	}

	static string ReadPlugged()
	{
		var context = Host();
		using var filter = new IntentFilter(Intent.ActionBatteryChanged);
		var sticky = context.RegisterReceiver(null, filter);
		var plugged = sticky?.GetIntExtra(BatteryManager.ExtraPlugged, 0) ?? 0;
		return plugged == 0 ? "battery" : "plugged";
	}

	static Context Host() =>
		MainActivity.Current is { } activity
			? activity
			: Android.App.Application.Context ?? throw new InvalidOperationException("jni-bind no-context");
}
