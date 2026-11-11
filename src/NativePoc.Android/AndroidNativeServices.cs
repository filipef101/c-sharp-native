using System.Runtime.Versioning;
using Android.Content;
using Android.OS;
using Android.Util;

namespace NativePoc;

sealed class AndroidNativeServices : INativeServices
{
	readonly Activity _activity;
	bool _disposed;

	public AndroidNativeServices(Activity activity)
	{
		_activity = activity;
	}

	public void CopyText(string label, string text)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		CopyChecked(_activity, label, text);
	}

	internal static void CopyChecked(Activity activity, string label, string text)
	{
		var clipboard = activity.GetSystemService(Context.ClipboardService) as ClipboardManager;
		if (clipboard is null)
			throw new InvalidOperationException("Clipboard service is unavailable.");
		var focused = activity.HasWindowFocus;
		Log.Info(MainActivity.Tag, "clipboard-focus=" + (focused ? "1" : "0") + " api=" + (int)Build.VERSION.SdkInt);
		clipboard.PrimaryClip = ClipData.NewPlainText(label, text);
		var read = ReadClip(activity, clipboard);
		if (!string.Equals(read, text, StringComparison.Ordinal))
			read = ReadClip(activity, clipboard);
		if (!string.Equals(read, text, StringComparison.Ordinal) && !activity.HasWindowFocus)
		{
			var back = new Intent(activity, activity.GetType());
			back.AddFlags(ActivityFlags.ReorderToFront | ActivityFlags.SingleTop);
			activity.StartActivity(back);
			read = ReadClip(activity, clipboard);
		}

		if (!string.Equals(read, text, StringComparison.Ordinal))
		{
			var hint = activity.HasWindowFocus
				? "The service returned " + read.Length.ToString() + " characters for " + text.Length.ToString() + " written. Android 10+ only echoes the clip to the focused window."
				: "The activity window is not focused. Dismiss the share sheet or any system dialog, then copy again. Android 10+ only returns the clipboard to the focused window.";
			throw new InvalidOperationException("Clipboard round-trip failed. " + hint);
		}

		Log.Info(MainActivity.Tag, "clipboard-ok chars=" + text.Length + " text=" + text.Replace('\n', '|'));
	}

	static string ReadClip(Activity activity, ClipboardManager clipboard)
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
		return item.CoerceToText(activity)?.ToString() ?? "";
	}

	public void Pulse()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			Log.Info(MainActivity.Tag, "haptic-unavailable");
			return;
		}
		PulseOn26();
	}

	[SupportedOSPlatform("android26.0")]
	void PulseOn26()
	{
		var vibrator = VibratorOf(_activity);
		if (vibrator is null || !vibrator.HasVibrator)
		{
			Log.Info(MainActivity.Tag, "haptic-unavailable");
			return;
		}
		vibrator.Vibrate(VibrationEffect.CreateOneShot(20, VibrationEffect.DefaultAmplitude));
		Log.Info(MainActivity.Tag, "haptic-ok");
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		Log.Info(MainActivity.Tag, "native-services-disposed");
	}

	static Vibrator? VibratorOf(Activity activity)
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(31))
		{
			var manager = activity.GetSystemService(Context.VibratorManagerService) as VibratorManager;
			return manager?.DefaultVibrator;
		}
		return activity.GetSystemService(Context.VibratorService) as Vibrator;
	}
}
