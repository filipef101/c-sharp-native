#if NATIVEPOC_LOCATION
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using AndroidLocation = Android.Locations.Location;

namespace NativePoc;

static class LocationModule
{
	const int RequestCode = 41;
	const string Prefs = "nativepoc-location";
	const string Asked = "asked";

	static TaskCompletionSource<string>? _pending;

	public static void OnResult(Activity activity, int requestCode, string[] permissions, Permission[] grantResults)
	{
		_ = permissions;
		if (requestCode != RequestCode)
			return;
		MarkAsked(activity);
		var granted = false;
		foreach (var result in grantResults)
		{
			if (result == Permission.Granted)
				granted = true;
		}

		var should = activity.ShouldShowRequestPermissionRationale(Android.Manifest.Permission.AccessFineLocation);
		var access = LocationAccessRules.Classify(granted, should, asked: true);
		var pending = _pending;
		_pending = null;
		if (pending is null)
			return;
		if (access != LocationAccess.Granted)
		{
			pending.TrySetResult(Finish(Location.Format(new LocationFix(access, 0, 0, 0))));
			return;
		}

		_ = ReadFix(activity, CancellationToken.None).ContinueWith(
			done => pending.TrySetResult(done.IsCompletedSuccessfully ? done.Result : Finish("denied")),
			TaskScheduler.Default);
	}

	public static Task<string> Read(Activity activity, bool request, CancellationToken cancel)
	{
		var granted = IsGranted(activity);
		var should = activity.ShouldShowRequestPermissionRationale(Android.Manifest.Permission.AccessFineLocation);
		var asked = WasAsked(activity);
		if (!granted && request)
		{
			var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending = pending;
			MarkAsked(activity);
			Android.Util.Log.Info(MainActivity.Tag, "location request");
			activity.RequestPermissions(
				[Android.Manifest.Permission.AccessFineLocation, Android.Manifest.Permission.AccessCoarseLocation],
				RequestCode);
			return Wait(pending, cancel);
		}

		var access = LocationAccessRules.Classify(granted, should, asked);
		if (access != LocationAccess.Granted)
			return Task.FromResult(Finish(Location.Format(new LocationFix(access, 0, 0, 0))));
		return ReadFix(activity, cancel);
	}

	static async Task<string> Wait(TaskCompletionSource<string> pending, CancellationToken cancel)
	{
		var finished = await Task.WhenAny(pending.Task, Task.Delay(8000, cancel));
		if (finished == pending.Task)
			return await pending.Task;
		_pending = null;
		return Finish("denied");
	}

	static bool IsGranted(Activity activity) =>
		activity.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted
		|| activity.CheckSelfPermission(Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;

	static bool WasAsked(Activity activity) =>
		activity.GetSharedPreferences(Prefs, FileCreationMode.Private)?.GetBoolean(Asked, false) ?? false;

	static void MarkAsked(Activity activity) =>
		activity.GetSharedPreferences(Prefs, FileCreationMode.Private)?.Edit()?.PutBoolean(Asked, true)?.Apply();

	static Task<string> ReadFix(Activity activity, CancellationToken cancel)
	{
		var manager = activity.GetSystemService(Context.LocationService) as LocationManager;
		if (manager is null)
			return Task.FromResult(Finish(Location.Format(new LocationFix(LocationAccess.Granted, 0, 0, 0))));
		var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var reported = 0;
		var requestedAt = SystemClock.ElapsedRealtimeNanos();
		OneFix listener = null!;
		listener = new OneFix(location =>
		{
			if (location.ElapsedRealtimeNanos < requestedAt)
				return;
			if (Interlocked.Exchange(ref reported, 1) != 0)
				return;
			manager.RemoveUpdates(listener);
			pending.TrySetResult(Finish(FormatFix(location)));
		});
		var provider = manager.IsProviderEnabled(LocationManager.GpsProvider)
			? LocationManager.GpsProvider
			: LocationManager.NetworkProvider;
		try
		{
			manager.RequestLocationUpdates(provider, 0, 0, listener, Looper.MainLooper);
		}
		catch (Exception)
		{
			return Task.FromResult(Fallback(manager));
		}

		_ = Task.Delay(4000, cancel).ContinueWith(_ =>
		{
			if (Interlocked.Exchange(ref reported, 1) != 0)
				return;
			manager.RemoveUpdates(listener);
			pending.TrySetResult(Fallback(manager));
		}, TaskScheduler.Default);
		return pending.Task;
	}

	static string Fallback(LocationManager manager)
	{
		var last = Last(manager);
		return last is null
			? Finish(Location.Format(new LocationFix(LocationAccess.Granted, 0, 0, 0)))
			: Finish(FormatFix(last));
	}

	static AndroidLocation? Last(LocationManager manager)
	{
		AndroidLocation? best = null;
		foreach (var provider in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
		{
			try
			{
				var fix = manager.GetLastKnownLocation(provider);
				if (fix is null)
					continue;
				if (best is null || fix.Time > best.Time)
					best = fix;
			}
			catch (Exception)
			{
				_ = provider;
			}
		}

		return best;
	}

	static string FormatFix(AndroidLocation fix)
	{
		var accuracy = fix.HasAccuracy ? fix.Accuracy : 0;
		return Location.Format(new LocationFix(LocationAccess.Granted, fix.Latitude, fix.Longitude, accuracy));
	}

	static string Finish(string wire)
	{
		Android.Util.Log.Info(MainActivity.Tag, "location result " + wire);
		return wire;
	}

	sealed class OneFix : Java.Lang.Object, ILocationListener
	{
		readonly Action<AndroidLocation> _done;

		public OneFix(Action<AndroidLocation> done) => _done = done;

		public void OnLocationChanged(AndroidLocation location) => _done(location);

		public void OnProviderDisabled(string provider)
		{
			_ = provider;
		}

		public void OnProviderEnabled(string provider)
		{
			_ = provider;
		}

		public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
		{
			_ = provider;
			_ = status;
			_ = extras;
		}
	}
}
#endif
