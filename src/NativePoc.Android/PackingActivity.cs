using System.Runtime.Versioning;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.Window;
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
	public static PackingActivity? Current { get; private set; }

	public static void RequestCurrent() => Current?.RequestRender();

	AndroidHost? _host;
	ViewReconciler<View>? _reconciler;
	AndroidNativeServices? _services;
	PackingBackCallback? _backCallback;
	bool _inRender;
	bool _renderAgain;
	bool _insetApplied;

	public INativeServices Native => _services ?? throw new InvalidOperationException("Native services are not open.");

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		Log.Info(MainActivity.Tag, "packing-start tick=" + System.Environment.TickCount64);
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
			RegisterBackCallback();
		_services = new AndroidNativeServices(this);
		_host = new AndroidHost(this, () => _reconciler?.Current);
		_reconciler = new ViewReconciler<View>(_host);
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
#if DEBUG
		StateStore.TryLoad(this);
#endif
		RenderNow();
	}

	protected override void OnResume()
	{
		base.OnResume();
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current, () => ViewLayout.Format(_reconciler));
	}

#pragma warning disable CA1422
	public override void OnBackPressed()
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			base.OnBackPressed();
			return;
		}
		HandleSystemBack();
	}
#pragma warning restore CA1422

	void HandleSystemBack()
	{
		switch (PackingNavigation.SystemBackFor(PackingSession.Shared.State.Route))
		{
			case SystemBack.ReturnToList:
				if (_record is { } record && record.Has("Editing"))
					record.State.WriteBool("Editing", false);
				if (_model is not null)
					_model.Editing = false;
				PackingActions.Back(PackingSession.Shared.State, this);
				return;
			case SystemBack.FinishActivity:
				Finish();
				return;
			default:
				throw new InvalidOperationException("Unknown system back.");
		}
	}

	[SupportedOSPlatform("android33.0")]
	void RegisterBackCallback()
	{
		_backCallback = new PackingBackCallback(this);
		OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
			IOnBackInvokedDispatcher.PriorityDefault,
			_backCallback);
	}

	[SupportedOSPlatform("android33.0")]
	void UnregisterBackCallback()
	{
		if (_backCallback is null)
			return;
		OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backCallback);
		_backCallback.Dispose();
		_backCallback = null;
	}

	protected override void OnDestroy()
	{
		if (ReferenceEquals(Current, this))
			Current = null;
		UiSurface.Clear(this);
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
			UnregisterBackCallback();
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
			WaitForUi(Run);
	}

	void WaitForUi(Action run)
	{
		Exception? error = null;
		using var done = new ManualResetEventSlim(false);
		RunOnUiThread(() =>
		{
			try
			{
				run();
			}
			catch (Exception ex)
			{
				error = ex;
			}
			finally
			{
				done.Set();
			}
		});
		if (!done.Wait(TimeSpan.FromSeconds(5)))
			throw new InvalidOperationException("Packing did not render.");
		if (error is not null)
			throw new InvalidOperationException("Packing did not render.", error);
	}

	static RecordProgram.Bound? _record;
	PackingModel? _model;
	PackingPlatform? _platform;

	public static string UseProgram(RecordProgram.Bound program)
	{
		_record = program;
		var current = Current;
		if (current is null)
			return "packing";
		current.RequestRender();
#if DEBUG
		if (MainActivity.Current is { } main)
			DevOverlay.Clear(main);
#endif
		return "packing";
	}

	void RenderNow()
	{
		if (_reconciler is null)
			return;
		var previousRoot = _reconciler.Root;
		_platform ??= new PackingPlatform(this);
		_model ??= PackingModel.From(PackingSession.Shared.State);
		Element tree;
		if (_record is { } record)
		{
			RecordSession.Publish(record);
			tree = record.Render(_platform);
		}
		else
		{
			tree = PackingScreen.Render(_model, _platform);
			_model.CopyTo(PackingSession.Shared.State);
		}

		var state = PackingSession.Shared.State;
		_reconciler.Render(tree);
		if (_reconciler.Root is null)
			throw new InvalidOperationException("Packing did not mount a root view.");
		if (!ReferenceEquals(previousRoot, _reconciler.Root))
		{
			SetContentView(_reconciler.Root);
			Log.Info(MainActivity.Tag, "packing-set-content-view");
			PlaceBelowActionBar(_reconciler.Root);
		}
		if (_host is not null && _reconciler.Root is not null)
			_host.RestoreFocus(_reconciler.Root);
		Log.Info(
			MainActivity.Tag,
			"packing-rendered route=" + state.Route
			+ " packed=" + state.PackedCount
			+ " total=" + state.Items.Count
			+ " switch-deliveries=" + PackingTelemetry.SwitchDeliveries
			+ " field-deliveries=" + PackingTelemetry.FieldDeliveries);
#if DEBUG
		StateStore.Save(this);
#endif
	}

	void PlaceBelowActionBar(View root)
	{
		_insetApplied = false;
		var observer = root.ViewTreeObserver;
		if (observer is null)
			return;
		observer.AddOnGlobalLayoutListener(new ActionBarInset(this, root));
	}

	bool ApplyActionBarInset(View root)
	{
		if (_insetApplied)
			return true;
		var id = Resources?.GetIdentifier("action_bar_container", "id", "android") ?? 0;
		if (id == 0)
			return true;
		var bar = FindViewById(id);
		if (bar is null)
			return true;
		if (bar.Height <= 0)
			return false;
		var barLoc = new int[2];
		var rootLoc = new int[2];
		bar.GetLocationOnScreen(barLoc);
		root.GetLocationOnScreen(rootLoc);
		var overlap = barLoc[1] + bar.Height - rootLoc[1];
		if (overlap <= 0)
		{
			_insetApplied = true;
			return true;
		}
		root.SetPadding(root.PaddingLeft, root.PaddingTop + overlap, root.PaddingRight, root.PaddingBottom);
		_insetApplied = true;
		Log.Info(
			MainActivity.Tag,
			"content-inset overlap=" + overlap + " top=" + root.PaddingTop + " bar-bottom=" + (barLoc[1] + bar.Height));
		return true;
	}

	sealed class PackingBackCallback : Java.Lang.Object, IOnBackInvokedCallback
	{
		readonly PackingActivity _activity;

		public PackingBackCallback(PackingActivity activity)
		{
			_activity = activity;
		}

		public void OnBackInvoked() => _activity.HandleSystemBack();
	}

	sealed class ActionBarInset : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
	{
		readonly PackingActivity _activity;
		readonly View _root;

		public ActionBarInset(PackingActivity activity, View root)
		{
			_activity = activity;
			_root = root;
		}

		public void OnGlobalLayout()
		{
			if (!_activity.ApplyActionBarInset(_root))
				return;
			_root.ViewTreeObserver?.RemoveOnGlobalLayoutListener(this);
		}
	}
}

sealed class PackingPlatform : IPlatform
{
	readonly PackingActivity _activity;

	public PackingPlatform(PackingActivity activity)
	{
		_activity = activity;
	}

	public void CopyNonce(string nonce) => CopyText("nonce", nonce);

	public void RequestRender() => _activity.RequestRender();

	public void Pulse() => _activity.Native.Pulse();

	public void CopyText(string label, string text) => _activity.Native.CopyText(label, text);

	public void Push(string screen) => ScreenNav.Shared.Push(screen);

	public void Pop() => ScreenNav.Shared.Pop();

	public void Effect(string property, string resource) =>
		EffectRuntime.Start(_activity, property, resource, () => _activity.RequestRender());

	public void Every(string property) => ClockRuntime.Arm(property);

	public void Module(string module, string method, string[] args) =>
		PlatformModules.Dispatch(_activity, module, method, args);
}
