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
		var clipboard = _activity.GetSystemService(Context.ClipboardService) as ClipboardManager;
		if (clipboard is null)
			throw new InvalidOperationException("Clipboard service is unavailable.");
		clipboard.PrimaryClip = ClipData.NewPlainText(label, text);
		var read = clipboard.PrimaryClip?.GetItemAt(0)?.Text?.ToString() ?? "";
		if (!string.Equals(read, text, StringComparison.Ordinal))
			throw new InvalidOperationException("Clipboard round-trip failed.");
		Log.Info(MainActivity.Tag, "clipboard-ok chars=" + text.Length);
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
