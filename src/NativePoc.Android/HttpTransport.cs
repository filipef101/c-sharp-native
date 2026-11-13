#if NATIVEPOC_HTTP
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

	public static async Task<string> SendAsync(string method, string url, string? json, CancellationToken cancel)
	{
		if (cancel.IsCancellationRequested)
			return "http-error: cancelled";
		try
		{
			using var request = new HttpRequestMessage(new HttpMethod(method), url);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");
			using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
			var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
			if (cancel.IsCancellationRequested)
				return "http-error: cancelled";
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
		catch (OperationCanceledException) when (cancel.IsCancellationRequested)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-cancel module=http." + method);
			return "http-error: cancelled";
		}
		catch (OperationCanceledException)
		{
			Android.Util.Log.Info(MainActivity.Tag, "http-timeout module=http." + method);
			return "http-error: timeout";
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
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			using var response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead);
			if (!response.IsSuccessStatusCode)
				return null;
			using var input = response.Content.ReadAsStream();
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			int read;
			while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
			{
				buffer.Write(chunk, 0, read);
				if (buffer.Length > 4_000_000)
					return null;
			}

			Android.Util.Log.Info(MainActivity.Tag, "image-http client=1 bytes=" + buffer.Length.ToString(CultureInfo.InvariantCulture));
			return buffer.Length == 0 ? null : buffer.ToArray();
		}
		catch (Exception ex)
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			return null;
		}
	}
}
#endif
