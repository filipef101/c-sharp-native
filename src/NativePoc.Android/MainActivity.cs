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

	Func<AppState, IPlatform, Element> _render = Screen.Render;
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
#if DEBUG
		HotReloadServer.Start(this);
#endif
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

	public string UseRender(Func<AppState, IPlatform, Element> render)
	{
		var previous = _render;
		_render = render;
		try
		{
			RenderNow();
		}
		catch
		{
			_render = previous;
			throw;
		}
		return LastTitle;
	}

	public string LastTitle { get; private set; } = "";

	void RenderNow()
	{
		if (_reconciler is null || _platform is null)
			return;
		var previousRoot = _reconciler.Root;
		var state = AppSession.Shared.State;
		var tree = _render(state, _platform);
		_reconciler.Render(tree);
		if (_reconciler.Root is null)
			throw new InvalidOperationException("Screen did not mount a root view.");
		if (!ReferenceEquals(previousRoot, _reconciler.Root))
			SetContentView(_reconciler.Root);
		LastTitle = TitleOf(tree);
		Log.Info(Tag, "rendered count=" + state.Count + " name-length=" + state.Name.Length + " title=" + LastTitle);
	}

	static string TitleOf(Element element)
	{
		switch (element)
		{
			case Column column:
				return column.Children.Count == 0 ? "" : TitleOf(column.Children[0]);
			case Text text:
				return text.Value;
			case Button:
			case TextField:
				return "";
			default:
				throw Unknown(element);
		}
	}

	static InvalidOperationException Unknown(Element element) =>
		new("Unknown element.");
}
