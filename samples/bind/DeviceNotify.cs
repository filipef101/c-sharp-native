using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;

namespace NativePoc;

[JniType("android/app/NotificationManager")]
static partial class DeviceNotify
{
	public const int RequestCode = 73;

	const string PermissionName = "android.permission.POST_NOTIFICATIONS";

	const string Channel = "bind";

	static string? _screen;

	static string? _field;

	[JniMethod("requestPermission", "(Ljava/lang/String;Ljava/lang/String;)V")]
	public static void Ask(string screen, string field)
	{
		var activity = MainActivity.Current ?? throw new InvalidOperationException("jni-bind no-context");
		_screen = screen;
		_field = field;
		if (!OperatingSystem.IsAndroidVersionAtLeast(33) || activity.CheckSelfPermission(PermissionName) == Permission.Granted)
		{
			Show(activity);
			Finish("granted");
			return;
		}

		Log.Info("NativePoc", "jni-bind class=android/app/NotificationManager method=requestPermission text=requested");
		activity.RequestPermissions([PermissionName], RequestCode);
	}

	public static void OnResult(int requestCode, string[] permissions, Permission[] grantResults)
	{
		if (requestCode != RequestCode)
			return;
		_ = permissions;
		var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
		if (granted && MainActivity.Current is { } activity)
			Show(activity);
		Finish(granted ? "granted" : "denied");
	}

	static void Finish(string text)
	{
		Log.Info("NativePoc", "jni-bind class=android/app/NotificationManager method=requestPermission text=" + text);
		if (_screen is { } screen && _field is { } field)
			JniSink.Post(screen, field, text);
	}

	static void Show(Activity activity)
	{
		var manager = activity.GetSystemService(Context.NotificationService) as NotificationManager
			?? throw new InvalidOperationException("jni-bind miss service=notification");
		if (OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			var channel = new NotificationChannel(Channel, "Bind", NotificationImportance.Default);
			manager.CreateNotificationChannel(channel);
		}

		var notification = Builder(activity)
			.SetContentTitle("Bind")
			.SetContentText("pier")
			.SetSmallIcon(Android.Resource.Drawable.StatNotifyChat)
			.Build();
		manager.Notify(RequestCode, notification);
		Log.Info("NativePoc", "jni-bind class=android/app/NotificationManager method=notify text=pier");
	}

	static Notification.Builder Builder(Activity activity)
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(26))
			return new Notification.Builder(activity, Channel);
#pragma warning disable CS0618, CA1422
		return new Notification.Builder(activity);
#pragma warning restore CS0618, CA1422
	}
}
