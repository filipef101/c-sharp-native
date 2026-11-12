using Android.Content;
using Android.Util;

namespace NativePoc;

[JniType("android/content/ClipboardManager")]
static class DeviceClipboard
{
	[JniMethod("setPrimaryClip", "(Ljava/lang/String;Ljava/lang/String;)V", Instance = true, Service = "clipboard")]
	public static void Set(string label, string text)
	{
		var clipboard = Manager();
		clipboard.PrimaryClip = ClipData.NewPlainText(label, text);
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=setPrimaryClip label=" + label + " text=" + text);
	}

	[JniMethod("getPrimaryClip", "()Ljava/lang/String;")]
	public static string Get()
	{
		var text = Read(Manager());
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=getPrimaryClip text=" + text);
		return text;
	}

#pragma warning disable CA2255
	[System.Runtime.CompilerServices.ModuleInitializer]
	internal static void RegisterJni()
	{
		JniRegistry.Add("DeviceClipboard.Set", args => Set(args.Length > 0 ? args[0] : "", args.Length > 1 ? args[1] : ""));
		JniRegistry.Add("DeviceClipboard.Get", args =>
		{
			_ = args;
			return Get();
		});
	}
#pragma warning restore CA2255

	static ClipboardManager Manager()
	{
		var context = MainActivity.Current is { } activity
			? activity
			: Android.App.Application.Context ?? throw new InvalidOperationException("jni-bind no-context");
		return context.GetSystemService(Context.ClipboardService) as ClipboardManager
			?? throw new InvalidOperationException("jni-bind miss service=clipboard");
	}

	static string Read(ClipboardManager clipboard)
	{
		var clip = clipboard.PrimaryClip;
		if (clip is null || clip.ItemCount == 0)
			return "";
		var item = clip.GetItemAt(0);
		if (item is null)
			return "";
		var direct = item.Text?.ToString();
		if (!string.IsNullOrEmpty(direct))
			return direct;
		var activity = MainActivity.Current;
		return activity is null ? "" : item.CoerceToText(activity)?.ToString() ?? "";
	}
}
