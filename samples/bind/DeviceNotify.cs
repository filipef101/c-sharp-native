using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;

namespace NativePoc;

[JniType("android/app/NotificationManager")]
static partial class DeviceNotify
{
	public const int RequestCode = 73;

	const string PermissionName = "android.permission.POST_NOTIFICATIONS";

	const string Channel = "bind";

	const string Denials = "notify-denials";

	static string? _screen;

	static string? _field;

	static int _denials;

	[JniMethod("requestPermission", "(Ljava/lang/String;Ljava/lang/String;)V")]
	public static void Ask(string screen, string field)
	{
		var activity = MainActivity.Current ?? throw new InvalidOperationException("jni-bind no-context");
		_screen = screen;
		_field = field;
		_denials = AppStorage.GetInt(activity, Denials);
		if (!OperatingSystem.IsAndroidVersionAtLeast(33) || activity.CheckSelfPermission(PermissionName) == Permission.Granted)
		{
			_denials = 0;
			AppStorage.SetInt(activity, Denials, 0);
			Show(activity);
			Finish(activity, PermissionResult.Granted);
			return;
		}

		if (PermissionRules.BlocksWithoutAsking(_denials))
		{
			Finish(activity, PermissionResult.Blocked);
			return;
		}

		Log.Info("NativePoc", "jni-bind class=android/app/NotificationManager method=requestPermission text=requested");
		activity.RequestPermissions([PermissionName], RequestCode);
	}

	[JniMethod("openSettings", "()V")]
	public static void OpenSettings()
	{
		var activity = MainActivity.Current ?? throw new InvalidOperationException("jni-bind no-context");
		var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
		intent.SetData(Android.Net.Uri.Parse("package:" + activity.PackageName));
		activity.StartActivity(intent);
		Log.Info("NativePoc", "jni-bind class=android/provider/Settings method=ACTION_APPLICATION_DETAILS_SETTINGS");
	}

	public static void OnResult(int requestCode, string[] permissions, Permission[] grantResults)
	{
		if (requestCode != RequestCode || MainActivity.Current is not { } activity)
			return;
		_ = permissions;
		var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
		if (granted)
			_denials = 0;
		else
			_denials++;
		AppStorage.SetInt(activity, Denials, _denials);
		var result = PermissionRules.Classify(granted, Rationale(activity), false, _denials);
		if (result == PermissionResult.Granted)
			Show(activity);
		Finish(activity, result);
	}

	static void Finish(Activity activity, PermissionResult result)
	{
		var text = PermissionRules.Text(result);
		var rationale = Rationale(activity) ? "1" : "0";
		Log.Info(
			"NativePoc",
			"jni-bind class=android/app/NotificationManager method=requestPermission text=" + text
			+ " rationale=" + rationale
			+ " stored=" + _denials.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " denials=" + _denials.ToString(System.Globalization.CultureInfo.InvariantCulture));
		if (_screen is { } screen && _field is { } field)
			JniSink.Post(screen, field, text);
	}

	static bool Rationale(Activity activity) =>
		OperatingSystem.IsAndroidVersionAtLeast(23) && activity.ShouldShowRequestPermissionRationale(PermissionName);

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
