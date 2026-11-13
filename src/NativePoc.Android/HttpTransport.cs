#if NATIVEPOC_HTTP
#if NATIVEPOC_HTTP_MANAGED
using System.Globalization;
using System.Net.Http;
using System.Text;

namespace NativePoc;

static class HttpTransport
{
	static readonly SocketsHttpHandler Handler = new()
	{
		PooledConnectionLifetime = TimeSpan.FromMinutes(2),
		ConnectTimeout = TimeSpan.FromSeconds(8),
		UseCookies = false,
	};

	static readonly HttpClient Client = new(Handler, disposeHandler: false)
	{
		Timeout = TimeSpan.FromSeconds(8),
	};

	public static async Task<string> SendAsync(string method, string url, string? json, CancellationToken cancel, string screen)
	{
		var ticket = screen.Length == 0 ? "http" : screen;
		if (cancel.IsCancellationRequested)
			return "http-error: cancelled";
		var generation = HttpFlight.Open(ticket);
		using var registration = cancel.Register(() => HttpFlight.Cancel(ticket));
		try
		{
			using var request = new HttpRequestMessage(new HttpMethod(method), url);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");
			using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
			var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
			if (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation))
			{
				Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=http." + method);
				return "http-error: cancelled";
			}
			if (!response.IsSuccessStatusCode)
			{
				var code = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
				Android.Util.Log.Info(MainActivity.Tag, "http-status " + code);
				return "http-error: " + code;
			}

			Android.Util.Log.Info(
				MainActivity.Tag,
				"http-" + method.ToLowerInvariant() + " chars=" + body.Length.ToString(CultureInfo.InvariantCulture) + " client=1");
			return body;
		}
		catch (OperationCanceledException) when (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation))
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=http." + method);
			return "http-error: cancelled";
		}
		catch (OperationCanceledException)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-timeout module=http." + method);
			return "http-error: timeout";
		}
		catch (Exception) when (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation))
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=http." + method);
			return "http-error: cancelled";
		}
		catch (Exception ex)
		{
			var name = ex.GetType().Name;
			var reason = name.Contains("Timeout", StringComparison.Ordinal) ? "timeout" : name;
			Android.Util.Log.Info(MainActivity.Tag, "http-error module=http." + method + " " + name);
			return "http-error: " + reason;
		}
	}

	public static byte[]? GetBytes(string url)
	{
		var ticket = ScreenNav.Shared.TopId();
		if (ticket.Length == 0)
			ticket = "image";
		return GetBytes(url, ticket, HttpFlight.Join(ticket));
	}

	public static byte[]? GetBytes(string url, string ticket, int generation)
	{
		if (ticket.Length == 0)
			ticket = "image";
		if (generation <= 0)
			generation = HttpFlight.Join(ticket);
		if (HttpFlight.WasCancelled(ticket, generation))
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
			return null;
		}

		var cancel = new CancellationTokenSource();
		var live = new LiveRead();
		void Abort() => ResponseAbort.Stop(cancel, live.Stream, live.Response);
		if (!HttpFlight.BindAbort(ticket, generation, Abort))
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
			cancel.Dispose();
			return null;
		}

		HttpRequestMessage? request = null;
		try
		{
			request = new HttpRequestMessage(HttpMethod.Get, url);
			var response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
			live.Response = response;
			if (HttpFlight.WasCancelled(ticket, generation))
			{
				Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
				return null;
			}

			if (!response.IsSuccessStatusCode)
				return null;
			var input = response.Content.ReadAsStream();
			live.Stream = input;
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			int read;
			while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
			{
				if (HttpFlight.WasCancelled(ticket, generation))
				{
					Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
					return null;
				}

				buffer.Write(chunk, 0, read);
				if (buffer.Length > 4_000_000)
					return null;
			}

			if (HttpFlight.WasCancelled(ticket, generation))
			{
				Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
				return null;
			}

			Android.Util.Log.Info(MainActivity.Tag, "image-http client=1 bytes=" + buffer.Length.ToString(CultureInfo.InvariantCulture));
			return buffer.Length == 0 ? null : buffer.ToArray();
		}
		catch (Exception ex)
		{
			if (HttpFlight.WasCancelled(ticket, generation) || cancel.IsCancellationRequested)
			{
				Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
				return null;
			}

			Android.Util.Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			return null;
		}
		finally
		{
			HttpFlight.ClearAbort(ticket, Abort);
			ResponseAbort.Stop(cancel, live.Stream, live.Response);
			request?.Dispose();
			cancel.Dispose();
		}
	}

	sealed class LiveRead
	{
		public volatile Stream? Stream;

		public volatile HttpResponseMessage? Response;
	}
}
#else
namespace NativePoc;

static class HttpTransport
{
	public static Task<string> SendAsync(string method, string url, string? json, CancellationToken cancel, string screen)
	{
		var ticket = screen.Length == 0 ? "http" : screen;
		var generation = HttpFlight.Open(ticket);
		var logged = false;
		string Cancelled()
		{
			if (!logged)
			{
				logged = true;
				Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=http." + method);
			}

			return "http-error: cancelled";
		}

		if (cancel.IsCancellationRequested)
			return Task.FromResult(Cancelled());
		return Task.Run(() =>
		{
			var off = Android.OS.Looper.MyLooper() != Android.OS.Looper.MainLooper;
			Android.Util.Log.Info(MainActivity.Tag, "http-off-ui=" + (off ? "1" : "0"));
			using var registration = cancel.Register(() =>
			{
				try
				{
					HttpFlight.Cancel(ticket);
					JniRegistry.Call("PlatformHttp.Disconnect", [ticket]);
				}
				catch (Exception)
				{
				}
			});
			try
			{
				if (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation))
					return Cancelled();
				var body = JniRegistry.Call("PlatformHttp.Exchange", [method, url, json ?? "", ticket, generation.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
				if (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation) || body == "http-error: cancelled")
					return Cancelled();
				return body;
			}
			catch (Exception) when (cancel.IsCancellationRequested || HttpFlight.WasCancelled(ticket, generation))
			{
				return Cancelled();
			}
		});
	}

	public static byte[]? GetBytes(string url)
	{
		var ticket = ScreenNav.Shared.TopId();
		if (ticket.Length == 0)
			ticket = "image";
		return GetBytes(url, ticket, HttpFlight.Join(ticket));
	}

	public static byte[]? GetBytes(string url, string ticket, int generation)
	{
		if (ticket.Length == 0)
			ticket = "image";
		if (generation <= 0)
			generation = HttpFlight.Join(ticket);
		if (HttpFlight.WasCancelled(ticket, generation))
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
			return null;
		}

		var encoded = JniRegistry.Call("PlatformHttp.Read", [url, ticket, generation.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
		if (HttpFlight.WasCancelled(ticket, generation) || encoded == "http-error: cancelled")
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-abort source=" + url);
			return null;
		}

		if (encoded.Length == 0 || encoded.StartsWith("http-error:", StringComparison.Ordinal))
			return null;
		try
		{
			var bytes = Convert.FromBase64String(encoded);
			return bytes.Length == 0 ? null : bytes;
		}
		catch (FormatException)
		{
			return null;
		}
	}
}
#endif
#endif
