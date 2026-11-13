using System.Globalization;
using Android.Content;
using NativePoc.Packing;

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
			case ("theme", "Scale", 1):
				ThemeRuntime.SetScale(activity, args[0]);
				return;
			case ("animate", "Play", 1):
				MotionPlayer.Replay(args[0]);
				return;
			case ("animated", "Timing", 4):
				PlayAnimated(activity, args, spring: false);
				return;
			case ("animated", "Spring", 4):
				PlayAnimated(activity, args, spring: true);
				return;
#if NATIVEPOC_HTTP
			case ("feed", "Apply", 1):
				FeedJson.FillBag(args[0]);
				return;
#endif
#if NATIVEPOC_NAV
			case ("nav", "Push", 1):
				if (activity is MainActivity navPush)
					navPush.GoPush(args[0]);
				return;
			case ("nav", "Pop", 0):
				if (activity is MainActivity navPop)
					navPop.GoBack();
				return;
#endif
			case ("jni", _, _):
				if (JniRegistry.IsBlocking(method))
					_ = Task.Run(() => JniRegistry.Call(method, args));
				else
					JniRegistry.Call(method, args);
				return;
			case ("telemetry", "FieldDeliveries", 0):
				PackingTelemetry.FieldDeliveries++;
				return;
			case ("telemetry", "SwitchDeliveries", 0):
				PackingTelemetry.SwitchDeliveries++;
				return;
			default:
				throw new InvalidOperationException("Unknown platform module " + module + "." + method + ".");
		}
	}

	static void PlayAnimated(Activity activity, string[] args, bool spring)
	{
		if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from)
			|| !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to)
			|| !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration))
		{
			Android.Util.Log.Info(MainActivity.Tag, "animated miss key=" + args[0]);
			return;
		}

		AnimatedPlayer.Play(activity, args[0], from, to, duration, spring);
	}

	public static async Task<string> DispatchAsync(Activity activity, string module, string method, string[] args, CancellationToken cancel)
	{
		try
		{
			switch (module, method, args.Length)
			{
				case ("jni", _, _):
					return await Task.Run(() => JniRegistry.Call(method, args), cancel);
#if NATIVEPOC_HTTP
				case ("http", "GetJson", 1):
					return await HttpTransport.SendAsync("GET", args[0], null, cancel);
				case ("http", "PostJson", 2):
					return await HttpTransport.SendAsync("POST", args[0], args[1], cancel);
#endif
				case ("storage", "Get", 1):
					return AppStorage.Get(activity, args[0]) ?? "";
				case ("timer", "After", 1):
					if (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms < 0 || ms > 10_000)
						return "http-error: timer";
					await Task.Delay(ms, cancel);
					return "ok";
#if NATIVEPOC_LOCATION
				case ("location", "Read", 0):
					return await LocationModule.Read(activity, request: true, cancel);
#endif
#if NATIVEPOC_PERSIST
				case ("persist", "Get", 1):
					return await Task.Run(() => PersistStore.Get(activity, args[0]), cancel);
				case ("persist", "Put", 2):
					return await Task.Run(() => PersistStore.Put(activity, args[0], args[1]), cancel);
				case ("persist", "GetRecord", 1):
					return await Task.Run(() => PersistStore.GetRecord(activity, args[0]), cancel);
				case ("persist", "PutRecord", 4):
					return await Task.Run(() => PersistStore.PutRecord(activity, args[0], args[1], args[2], args[3]), cancel);
#endif
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
