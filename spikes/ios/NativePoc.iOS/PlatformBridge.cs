using CoreLocation;
using Foundation;

namespace NativePoc;

sealed class IosServices : IIosServices
{
	readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);

	public void Copy(string label, string text)
	{
		_ = label;
		UIPasteboard.General.String = text;
		Console.WriteLine("NativePoc clipboard.Copy");
	}

	public void Haptic() => Console.WriteLine("NativePoc haptics.Confirm");

	public void Share(string text) => Console.WriteLine("NativePoc share.Text chars=" + text.Length);

	public void Set(string key, string value)
	{
		_storage[key] = value;
		Console.WriteLine("NativePoc storage.Set");
	}

	public string Get(string key)
	{
		Console.WriteLine("NativePoc storage.Get");
		return _storage.TryGetValue(key, out var value) ? value : "";
	}

	public void Theme(string mode) => Console.WriteLine("NativePoc theme.Mode");

	public void Play(string key) => Console.WriteLine("NativePoc animate.Play " + key);

	public string Http(string method, string url, string? body) => IosNet.Send(method, url, body);

	public string Location()
	{
		Console.WriteLine("NativePoc location.Read");
		return IosLocation.Read();
	}

	public void RememberRow(string screen, object row)
	{
		_ = row;
		Console.WriteLine("NativePoc push-row " + screen);
	}

	public void WriteBack(string screen) => Console.WriteLine("NativePoc write-back " + screen);
}

static class IosNet
{
	public static string Send(string method, string url, string? body)
	{
		Console.WriteLine("NativePoc http." + method);
		using var request = new NSMutableUrlRequest(NSUrl.FromString(url) ?? throw new InvalidOperationException("Rejected URL."))
		{
			HttpMethod = method == "PostJson" ? "POST" : "GET",
		};
		if (body is not null)
			request.Body = NSData.FromString(body);
		using var session = NSUrlSession.SharedSession;
		string? result = null;
		Exception? failure = null;
		var wait = new ManualResetEventSlim(false);
		var task = session.CreateDataTask(request, (data, response, error) =>
		{
			try
			{
				if (error is not null)
				{
					result = "http-error: " + error.LocalizedDescription;
					return;
				}

				if (response is NSHttpUrlResponse http && (http.StatusCode < 200 || http.StatusCode > 299))
				{
					result = "http-error: " + http.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
					return;
				}

				result = data is null ? "" : NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() ?? "";
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			finally
			{
				wait.Set();
			}
		});
		task.Resume();
		wait.Wait();
		if (failure is not null)
			return "http-error: " + failure.GetType().Name;
		return result ?? "";
	}
}

static class IosLocation
{
	public static string Read()
	{
		var manager = new CLLocationManager();
		var fix = manager.Location;
		if (fix is null)
			return "denied";
		return Location.Format(new LocationFix(
			LocationAccess.Granted,
			fix.Coordinate.Latitude,
			fix.Coordinate.Longitude,
			(float)fix.HorizontalAccuracy));
	}
}

sealed class IosPlatform : IPlatform
{
	readonly IosServices _services = new();

	public void CopyNonce(string nonce) => CopyText("nonce", nonce);

	public void CopyText(string label, string text) => IosBridge.CopyText(_services, label, text);

	public void RequestRender() => ScreenController.Current?.RequestRender();

	public void Push(string screen)
	{
		if (!ScreenNav.Shared.Push(screen))
			Console.WriteLine("NativePoc nav-miss " + screen);
		/* Authored only. This project was not compiled. The Linux host records
		   the same heading focus in IosLogicSurface. */
		Console.WriteLine("nav-focus heading=" + screen);
		RequestRender();
	}

	public void Push(string screen, object row) => IosBridge.PushRow(_services, screen, row, Push);

	public void WriteBack() => IosBridge.WriteBack(_services, ScreenNav.Shared.TopId());

	public void Pop()
	{
		ScreenNav.Shared.Pop();
		RequestRender();
	}

	public void Effect(string property, string resource) =>
		Console.WriteLine("NativePoc effect " + property + " " + resource);

	public void Every(string property) => Console.WriteLine("NativePoc every " + property);

	public void Module(string module, string method, string[] args) =>
		IosBridge.Module(_services, module, method, args);

	public Task<string> ModuleAsync(string module, string method, string[] args) =>
		IosBridge.ModuleAsync(_services, module, method, args);

	public void Continue(Task<string> task, Action<string> resume) =>
		IosBridge.Continue(task, resume);
}
