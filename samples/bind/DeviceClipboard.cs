using Android.Content;
using Android.Util;

namespace NativePoc;

[JniType("android/content/ClipboardManager")]
static partial class DeviceClipboard
{
	[JniMethod("setPrimaryClip", "(Ljava/lang/String;Ljava/lang/String;)V", Instance = true, Service = "clipboard")]
	public static void Set(string label, string text)
	{
		var clipboard = Manager();
		clipboard.PrimaryClip = ClipData.NewPlainText(label, text);
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=setPrimaryClip label=" + label + " text=" + text);
	}

	[JniMethod("getPrimaryClip", "()Ljava/lang/String;")]
	public static string Get()
	{
		var text = Read(Manager());
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=getPrimaryClip text=" + text);
		return text;
	}

	static EventHandler? _clipChanged;

	static string? _lastText;

	static long _lastStamp;

	static long _lastTicks;

	[JniMethod("addPrimaryClipChangedListener", "(Ljava/lang/String;Ljava/lang/String;)V")]
	public static void Listen(string screen, string field)
	{
		var clipboard = Manager();
		if (_clipChanged is not null)
			clipboard.PrimaryClipChanged -= _clipChanged;
		var targetScreen = screen;
		var targetField = field;
		EventHandler handler = (_, _) =>
		{
			if (!string.Equals(ScreenNav.Shared.TopId(), targetScreen, StringComparison.Ordinal))
			{
				Stop();
				return;
			}

			var text = Read(clipboard);
			var stamp = Stamp(clipboard);
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			if (ClipDedupe.Repeat(_lastStamp, _lastText, _lastTicks, stamp, text, now, System.Diagnostics.Stopwatch.Frequency))
			{
				Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=addPrimaryClipChangedListener deduped=1 text=" + text);
				return;
			}

			_lastText = text;
			_lastStamp = stamp;
			_lastTicks = now;
			Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=addPrimaryClipChangedListener text=" + text);
			JniSink.Post(targetScreen, targetField, text);
		};
		_clipChanged = handler;
		clipboard.PrimaryClipChanged += handler;
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=addPrimaryClipChangedListener armed=1");
	}

	[JniMethod("removePrimaryClipChangedListener", "()V")]
	public static void Stop()
	{
		var removed = _clipChanged is not null;
		if (removed)
		{
			Manager().PrimaryClipChanged -= _clipChanged;
			_clipChanged = null;
		}

		_lastText = null;
		_lastStamp = 0;
		_lastTicks = 0;
		Log.Info("NativePoc", "jni-bind class=android/content/ClipboardManager method=removePrimaryClipChangedListener removed=" + (removed ? "1" : "0"));
	}

	static long Stamp(ClipboardManager clipboard)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
			return 0;
		return clipboard.PrimaryClip?.Description?.Timestamp ?? 0;
	}

	static ClipboardManager Manager()
	{
		var context = MainActivity.Current is { } activity
			? activity
			: Android.App.Application.Context ?? throw new InvalidOperationException("jni-bind no-context");
		return context.GetSystemService(Context.ClipboardService) as ClipboardManager
			?? throw new InvalidOperationException("jni-bind miss service=clipboard");
	}

	static string Read(ClipboardManager clipboard)
	{
		var clip = clipboard.PrimaryClip;
		if (clip is null || clip.ItemCount == 0)
			return "";
		var item = clip.GetItemAt(0);
		if (item is null)
			return "";
		var direct = item.Text?.ToString();
		if (!string.IsNullOrEmpty(direct))
			return direct;
		var activity = MainActivity.Current;
		return activity is null ? "" : item.CoerceToText(activity)?.ToString() ?? "";
	}
}
