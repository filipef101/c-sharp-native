using Android.Content;

namespace NativePoc;

static class PlatformModules
{
	public static void Dispatch(Activity activity, string module, string method, string[] args)
	{
		switch (module, method, args.Length)
		{
			case ("clipboard", "Copy", 2):
				using (var services = new AndroidNativeServices(activity))
					services.CopyText(args[0], args[1]);
				return;
			case ("haptics", "Confirm", 0):
				using (var services = new AndroidNativeServices(activity))
					services.Pulse();
				return;
			case ("share", "Text", 1):
				Share(activity, args[0]);
				return;
			default:
				throw new InvalidOperationException("Unknown platform module " + module + "." + method + ".");
		}
	}

	static void Share(Activity activity, string text)
	{
		if (text.Length == 0)
		{
			Android.Util.Log.Info(MainActivity.Tag, "share-skipped");
			return;
		}

		Android.Util.Log.Info(MainActivity.Tag, "share-text chars=" + text.Length);
		var send = new Intent(Intent.ActionSend);
		send.SetType("text/plain");
		send.PutExtra(Intent.ExtraText, text);
		activity.StartActivity(Intent.CreateChooser(send, "Share"));
	}
}
