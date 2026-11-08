using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using NativePoc.Packing;

[assembly: UsesPermission(Android.Manifest.Permission.Vibrate)]

namespace NativePoc;

[Activity(
	Label = "Packing",
	Exported = true,
	Name = "nativepoc.PackingActivity",
	LaunchMode = LaunchMode.SingleTop)]
public sealed class PackingActivity : Activity, IPackingHost
{
	AndroidHost? _host;
	ViewReconciler<View>? _reconciler;
	AndroidNativeServices? _services;
	bool _inRender;
	bool _renderAgain;

	public INativeServices Native => _services ?? throw new InvalidOperationException("Native services are not open.");

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		Log.Info(MainActivity.Tag, "packing-start tick=" + System.Environment.TickCount64);
		_services = new AndroidNativeServices(this);
		_host = new AndroidHost(this, () => _reconciler?.Current);
		_reconciler = new ViewReconciler<View>(_host);
		RenderNow();
	}

#pragma warning disable CA1422
	public override void OnBackPressed()
	{
		var state = PackingSession.Shared.State;
		if (state.Route == PackingRoute.Editor)
		{
			PackingActions.Back(state, this);
			return;
		}
		base.OnBackPressed();
	}
#pragma warning restore CA1422

	protected override void OnDestroy()
	{
		_reconciler?.Release();
		_reconciler = null;
		_host = null;
		_services?.Dispose();
		_services = null;
		base.OnDestroy();
	}

	public void RequestRender()
	{
		if (_inRender)
		{
			_renderAgain = true;
			return;
		}

		void Run()
		{
			do
			{
				_renderAgain = false;
				_inRender = true;
				try
				{
					RenderNow();
				}
				finally
				{
					_inRender = false;
				}
			}
			while (_renderAgain);
		}

		if (Looper.MainLooper?.IsCurrentThread == true)
			Run();
		else
			RunOnUiThread(Run);
	}

	void RenderNow()
	{
		if (_reconciler is null)
			return;
		var previousRoot = _reconciler.Root;
		var state = PackingSession.Shared.State;
		var tree = PackingScreen.Render(state, this);
		_reconciler.Render(tree);
		if (_reconciler.Root is null)
			throw new InvalidOperationException("Packing did not mount a root view.");
		if (!ReferenceEquals(previousRoot, _reconciler.Root))
		{
			SetContentView(_reconciler.Root);
			Log.Info(MainActivity.Tag, "packing-set-content-view");
		}
		Log.Info(
			MainActivity.Tag,
			"packing-rendered route=" + state.Route
			+ " packed=" + state.PackedCount
			+ " total=" + state.Items.Count
			+ " switch-deliveries=" + PackingTelemetry.SwitchDeliveries
			+ " field-deliveries=" + PackingTelemetry.FieldDeliveries);
	}
}
