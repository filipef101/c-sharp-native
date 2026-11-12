using System.Globalization;
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
			case ("storage", "Set", 2):
				AppStorage.Set(activity, args[0], args[1]);
				return;
			case ("theme", "Mode", 1):
				ThemeRuntime.Set(activity, args[0]);
				return;
			case ("animate", "Play", 1):
				MotionPlayer.Replay(args[0]);
				return;
#if NATIVEPOC_HTTP
			case ("feed", "Apply", 1):
				FeedJson.FillBag(args[0]);
				return;
#endif
			default:
				throw new InvalidOperationException("Unknown platform module " + module + "." + method + ".");
		}
	}

	public static async Task<string> DispatchAsync(Activity activity, string module, string method, string[] args, CancellationToken cancel)
	{
		try
		{
			switch (module, method, args.Length)
			{
#if NATIVEPOC_HTTP
				case ("http", "GetJson", 1):
					return await Task.Run(() => HttpTransport.Send("GET", args[0], null, cancel), cancel);
				case ("http", "PostJson", 2):
					return await Task.Run(() => HttpTransport.Send("POST", args[0], args[1], cancel), cancel);
#endif
				case ("storage", "Get", 1):
					return AppStorage.Get(activity, args[0]) ?? "";
				case ("timer", "After", 1):
					if (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms < 0 || ms > 10_000)
						return "http-error: timer";
					await Task.Delay(ms, cancel);
					return "ok";
				default:
					return "http-error: unknown " + module + "." + method;
			}
		}
		catch (OperationCanceledException) when (cancel.IsCancellationRequested)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=" + module + "." + method);
			return "http-error: cancelled";
		}
		catch (OperationCanceledException)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-timeout module=" + module + "." + method);
			return "http-error: timeout";
		}
		catch (Exception ex)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-error module=" + module + "." + method + " " + ex.GetType().Name);
			return "http-error: " + ex.GetType().Name;
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
