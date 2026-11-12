#if NATIVEPOC_HTTP
using System.Globalization;
using System.Text;
using Java.Net;

namespace NativePoc;

static class HttpTransport
{
	public static string Send(string method, string url, string? json, CancellationToken cancel)
	{
		if (cancel.IsCancellationRequested)
			return "http-error: cancelled";
		HttpURLConnection? connection = null;
		CancellationTokenRegistration registration = default;
		try
		{
			connection = (HttpURLConnection)new URL(url).OpenConnection()!;
			connection.RequestMethod = method;
			connection.ConnectTimeout = 8000;
			connection.ReadTimeout = 8000;
			var open = connection;
			registration = cancel.Register(() =>
			{
				try
				{
					open.Disconnect();
				}
				catch (Exception)
				{
				}
			});
			if (json is not null)
			{
				var bytes = Encoding.UTF8.GetBytes(json);
				connection.DoOutput = true;
				connection.SetRequestProperty("Content-Type", "application/json");
				using var output = connection.OutputStream;
				output?.Write(bytes, 0, bytes.Length);
			}

			var code = (int)connection.ResponseCode;
			if (cancel.IsCancellationRequested)
				return "http-error: cancelled";
			var body = Read(code is >= 200 and <= 299 ? connection.InputStream : connection.ErrorStream);
			if (cancel.IsCancellationRequested)
				return "http-error: cancelled";
			if (code is < 200 or > 299)
			{
				Android.Util.Log.Info(MainActivity.Tag, "http-status " + code.ToString(CultureInfo.InvariantCulture));
				return "http-error: " + code.ToString(CultureInfo.InvariantCulture);
			}

			Android.Util.Log.Info(MainActivity.Tag, "http-" + method.ToLowerInvariant() + " chars=" + body.Length.ToString(CultureInfo.InvariantCulture));
			return body;
		}
		catch (Exception) when (cancel.IsCancellationRequested)
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
		finally
		{
			registration.Dispose();
			try
			{
				connection?.Disconnect();
			}
			catch (Exception)
			{
			}
		}
	}

	static string Read(System.IO.Stream? stream)
	{
		if (stream is null)
			return "";
		using (stream)
		{
			using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
			return reader.ReadToEnd();
		}
	}
}
#endif
