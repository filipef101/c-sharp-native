using System.Globalization;
using System.Net.Http;
using System.Text;
using Android.Content;

namespace NativePoc;

static class PlatformModules
{
	static readonly HttpClient Http = new(new SocketsHttpHandler
	{
		PooledConnectionLifetime = TimeSpan.FromMinutes(1),
		ConnectTimeout = TimeSpan.FromSeconds(8),
	})
	{
		Timeout = TimeSpan.FromSeconds(8),
	};

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
			case ("feed", "Apply", 1):
				FeedJson.FillBag(args[0]);
				return;
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
				case ("http", "GetJson", 1):
					return await Send(HttpMethod.Get, args[0], null, cancel);
				case ("http", "PostJson", 2):
					return await Send(HttpMethod.Post, args[0], args[1], cancel);
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

	static async Task<string> Send(HttpMethod method, string url, string? json, CancellationToken cancel)
	{
		using var request = new HttpRequestMessage(method, url);
		if (json is not null)
			request.Content = new StringContent(json, Encoding.UTF8, "application/json");
		using var response = await Http.SendAsync(request, cancel);
		var body = await response.Content.ReadAsStringAsync(cancel);
		if (!response.IsSuccessStatusCode)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-status " + (int)response.StatusCode);
			return "http-error: " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
		}

		Android.Util.Log.Info(MainActivity.Tag, "http-" + method.Method.ToLowerInvariant() + " chars=" + body.Length.ToString(CultureInfo.InvariantCulture));
		return body;
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
