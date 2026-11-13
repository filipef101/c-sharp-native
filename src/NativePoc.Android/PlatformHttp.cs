#if NATIVEPOC_HTTP && !NATIVEPOC_HTTP_MANAGED
using System.Globalization;
using System.Text;
using Android.Util;
using Java.Net;

namespace NativePoc;

// java.net.HttpURLConnection through the JNI binding registry.
// A screen still calls Http.GetJson. The host runs Exchange off the UI thread.
// iOS would map the same registry entry to NSURLSession. That host is not built here.
[JniType("java/net/HttpURLConnection")]
static partial class PlatformHttp
{
	const int JsonCap = 1_000_000;

	const int ByteCap = 4_000_000;

	[JniMethod("getResponseCode", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)Ljava/lang/String;", Blocking = true)]
	public static string Exchange(string method, string url, string json, string ticket, int generation)
	{
		HttpURLConnection? connection = null;
		try
		{
			if (HttpFlight.WasCancelled(ticket, generation))
				return "http-error: cancelled";
			if (string.IsNullOrEmpty(url))
				return "http-error: url";
			connection = Track(ticket, generation, url);
			if (connection is null)
				return "http-error: cancelled";
			connection.RequestMethod = method.Length == 0 ? "GET" : method;
			if (json.Length > 0)
			{
				connection.DoOutput = true;
				connection.SetRequestProperty("Content-Type", "application/json; charset=utf-8");
				var payload = Encoding.UTF8.GetBytes(json);
				using var output = connection.OutputStream;
				if (output is null)
					return "http-error: output";
				output.Write(payload, 0, payload.Length);
				output.Flush();
			}

			var code = (int)connection.ResponseCode;
			if (code is < 200 or > 299)
			{
				Log.Info(MainActivity.Tag, "http-status " + code.ToString(CultureInfo.InvariantCulture));
				Log.Info(MainActivity.Tag, "jni-bind class=java/net/HttpURLConnection method=getResponseCode code=" + code.ToString(CultureInfo.InvariantCulture));
				return "http-error: " + code.ToString(CultureInfo.InvariantCulture);
			}

			using var input = connection.InputStream;
			var body = ReadText(input, JsonCap);
			if (body is null)
				return "http-error: large";
			Log.Info(
				MainActivity.Tag,
				"jni-bind class=java/net/HttpURLConnection method=getInputStream chars=" + body.Length.ToString(CultureInfo.InvariantCulture));
			Log.Info(
				MainActivity.Tag,
				"http-" + (method.Length == 0 ? "get" : method.ToLowerInvariant()) + " chars=" + body.Length.ToString(CultureInfo.InvariantCulture) + " client=jni");
			return body;
		}
		catch (Exception ex)
		{
			if (HttpFlight.WasCancelled(ticket, generation))
				return "http-error: cancelled";
			var name = ex.GetType().Name;
			var reason = name.Contains("Timeout", StringComparison.Ordinal) ? "timeout" : name;
			Log.Info(MainActivity.Tag, "http-error module=http." + method + " " + name);
			Log.Info(MainActivity.Tag, "jni-bind class=java/net/HttpURLConnection method=getResponseCode error=" + name);
			return "http-error: " + reason;
		}
		finally
		{
			Release(connection);
		}
	}

	[JniMethod("getInputStream", "(Ljava/lang/String;)Ljava/lang/String;", Blocking = true)]
	public static string Read(string url)
	{
		HttpURLConnection? connection = null;
		try
		{
			connection = Connect(url);
			connection.RequestMethod = "GET";
			var code = (int)connection.ResponseCode;
			if (code is < 200 or > 299)
				return "http-error: " + code.ToString(CultureInfo.InvariantCulture);
			using var input = connection.InputStream;
			var bytes = ReadBytes(input, ByteCap);
			if (bytes is null)
				return "http-error: large";
			Log.Info(MainActivity.Tag, "jni-bind class=java/net/HttpURLConnection method=getInputStream bytes=" + bytes.Length.ToString(CultureInfo.InvariantCulture));
			Log.Info(MainActivity.Tag, "image-http client=jni bytes=" + bytes.Length.ToString(CultureInfo.InvariantCulture));
			return Convert.ToBase64String(bytes);
		}
		catch (Exception ex)
		{
			Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			return "http-error: " + ex.GetType().Name;
		}
		finally
		{
			Release(connection);
		}
	}

	[JniMethod("disconnect", "(Ljava/lang/String;)V")]
	public static void Disconnect(string ticket)
	{
		HttpURLConnection[] live;
		lock (HttpFlight.Gate)
		{
			HttpFlight.CancelCore(ticket);
			if (!Live.Remove(ticket, out var set) || set.Count == 0)
				return;
			live = new HttpURLConnection[set.Count];
			set.CopyTo(live);
		}

		Abort(live);
	}

	static readonly Dictionary<string, HashSet<HttpURLConnection>> Live = new(StringComparer.Ordinal);

	static HttpURLConnection Connect(string url)
	{
		var connection = (HttpURLConnection)new URL(url).OpenConnection()!;
		connection.ConnectTimeout = 8000;
		connection.ReadTimeout = 8000;
		connection.InstanceFollowRedirects = true;
		return connection;
	}

	static HttpURLConnection? Track(string ticket, int generation, string url)
	{
		var connection = Connect(url);
		lock (HttpFlight.Gate)
		{
			if (HttpFlight.CancelledCore(ticket, generation))
			{
				try
				{
					connection.Disconnect();
				}
				catch (Exception)
				{
				}

				return null;
			}

			if (!Live.TryGetValue(ticket, out var set))
			{
				set = new HashSet<HttpURLConnection>();
				Live[ticket] = set;
			}

			set.Add(connection);
			HttpFlight.AttachCore(ticket, generation);
		}

		return connection;
	}

	static void Release(HttpURLConnection? connection)
	{
		if (connection is null)
			return;
		lock (HttpFlight.Gate)
		{
			foreach (var set in Live.Values)
				set.Remove(connection);
		}

		try
		{
			connection.Disconnect();
		}
		catch (Exception)
		{
		}
	}

	static void Abort(HttpURLConnection[] live)
	{
		foreach (var connection in live)
		{
			try
			{
				connection.Disconnect();
			}
			catch (Exception)
			{
			}
		}
	}

	static string? ReadText(Stream? input, int cap)
	{
		if (input is null)
			return "";
		using var buffer = new MemoryStream();
		var chunk = new byte[8192];
		int read;
		while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
		{
			buffer.Write(chunk, 0, read);
			if (buffer.Length > cap)
				return null;
		}

		return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
	}

	static byte[]? ReadBytes(Stream? input, int cap)
	{
		if (input is null)
			return [];
		using var buffer = new MemoryStream();
		var chunk = new byte[8192];
		int read;
		while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
		{
			buffer.Write(chunk, 0, read);
			if (buffer.Length > cap)
				return null;
		}

		return buffer.ToArray();
	}
}
#endif
