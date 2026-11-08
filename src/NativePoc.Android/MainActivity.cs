using System.Runtime.CompilerServices;
using Android.OS;
using Android.Util;
using Android.Views;

namespace NativePoc;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
	public const string Tag = "NativePoc";

	public static MainActivity? Current { get; private set; }

	AndroidHost? _host;
	ViewReconciler<View>? _reconciler;
	PlatformBridge? _platform;
	bool _inRender;
	bool _renderAgain;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		Current = this;
		Log.Info(Tag, "activity-start tick=" + System.Environment.TickCount64);
		Log.Info(
			Tag,
			"runtime IsDynamicCodeSupported="
			+ RuntimeFeature.IsDynamicCodeSupported
			+ " IsDynamicCodeCompiled="
			+ RuntimeFeature.IsDynamicCodeCompiled);
		_platform = new PlatformBridge(this);
		_host = new AndroidHost(this, () => _reconciler?.Current);
		_reconciler = new ViewReconciler<View>(_host);
		RenderNow();
		if (_reconciler.Root is null)
			throw new InvalidOperationException("Screen did not mount a root view.");
		SetContentView(_reconciler.Root);
	}

	protected override void OnDestroy()
	{
		if (ReferenceEquals(Current, this))
			Current = null;
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
		if (_reconciler is null || _platform is null)
			return;
		var state = AppSession.Shared.State;
		_reconciler.Render(Screen.Render(state, _platform));
		Log.Info(Tag, "rendered count=" + state.Count + " name-length=" + state.Name.Length);
	}
}
