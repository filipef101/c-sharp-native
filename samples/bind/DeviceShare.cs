using Android.Content;
using Android.Util;

namespace NativePoc;

[JniType("android/content/Intent")]
static class DeviceShare
{
	[JniMethod("ACTION_SEND", "(Ljava/lang/String;)V")]
	public static void Send(string text)
	{
		var activity = MainActivity.Current ?? throw new InvalidOperationException("jni-bind no-activity");
		Log.Info("NativePoc", "jni-bind class=android/content/Intent method=ACTION_SEND text=" + text);
		var send = new Intent(Intent.ActionSend);
		send.SetType("text/plain");
		send.PutExtra(Intent.ExtraText, text);
		activity.StartActivity(Intent.CreateChooser(send, "Share"));
	}

#pragma warning disable CA2255
	[System.Runtime.CompilerServices.ModuleInitializer]
	internal static void RegisterJni()
	{
		JniRegistry.Add("DeviceShare.Send", args => Send(args.Length == 0 ? "" : args[0]));
	}
#pragma warning restore CA2255
}
