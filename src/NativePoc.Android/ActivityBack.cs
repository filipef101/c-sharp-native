using System.Runtime.Versioning;
using Android.Views;
using Android.Window;

namespace NativePoc;

static class ActivityBack
{
	static Predictive? _predictive;
	static Invoked? _invoked;
	static bool _registered;

	public static void Sync(MainActivity activity)
	{
		var nested = ScreenNav.Shared.Depth > 1;
		if (OperatingSystem.IsAndroidVersionAtLeast(34))
		{
			_predictive ??= new Predictive(activity);
			Sync(activity, _predictive, nested);
			return;
		}

		if (OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			_invoked ??= new Invoked(activity);
			Sync(activity, _invoked, nested);
		}
	}

	public static bool HandleLegacy(MainActivity activity)
	{
		if (ScreenNav.Shared.Depth <= 1)
			return false;
		Android.Util.Log.Info(MainActivity.Tag, "nav-back invoked");
		activity.GoBack();
		return true;
	}

	[SupportedOSPlatform("android33.0")]
	static void Sync(MainActivity activity, Android.Window.IOnBackInvokedCallback callback, bool nested)
	{
		var dispatcher = activity.OnBackInvokedDispatcher;
		if (nested && !_registered)
		{
			dispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault, callback);
			_registered = true;
			Android.Util.Log.Info(MainActivity.Tag, "nav-back registered depth=" + ScreenNav.Shared.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return;
		}

		if (!nested && _registered)
		{
			dispatcher.UnregisterOnBackInvokedCallback(callback);
			_registered = false;
			Android.Util.Log.Info(MainActivity.Tag, "nav-back unregistered");
		}
	}

	[SupportedOSPlatform("android33.0")]
	sealed class Invoked : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
	{
		readonly MainActivity _activity;

		public Invoked(MainActivity activity) => _activity = activity;

		public void OnBackInvoked()
		{
			Android.Util.Log.Info(MainActivity.Tag, "nav-back invoked");
			_activity.GoBack();
		}
	}

	[SupportedOSPlatform("android34.0")]
	sealed class Predictive : Java.Lang.Object, Android.Window.IOnBackAnimationCallback
	{
		readonly MainActivity _activity;

		public Predictive(MainActivity activity) => _activity = activity;

		public void OnBackStarted(Android.Window.BackEvent backEvent)
		{
			_ = backEvent;
			NavMotion.TrackStart();
			Android.Util.Log.Info(MainActivity.Tag, "nav-back started");
		}

		public void OnBackProgressed(Android.Window.BackEvent backEvent)
		{
			NavMotion.Track(backEvent.Progress);
			Android.Util.Log.Info(
				MainActivity.Tag,
				"nav-back progress=" + backEvent.Progress.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		public void OnBackCancelled()
		{
			NavMotion.TrackEnd(false);
			Android.Util.Log.Info(MainActivity.Tag, "nav-back cancel");
		}

		public void OnBackInvoked()
		{
			Android.Util.Log.Info(MainActivity.Tag, "nav-back invoked");
			NavMotion.TrackEnd(true);
		}
	}
}
