using System.Runtime.CompilerServices;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace NativePoc;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
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
		if (string.Equals(Intent?.GetStringExtra("nativepoc_keyed_move"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.GreetingFirst = true;
			RenderNow();
		}
#if DEBUG
		HotReloadServer.Start(this);
#endif
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		if (intent is null)
			return;
		Intent = intent;
		if (string.Equals(intent.GetStringExtra("nativepoc_keyed_move"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.GreetingFirst = true;
			RequestRender();
		}
		if (string.Equals(intent.GetStringExtra("nativepoc_root_swap"), "1", StringComparison.Ordinal))
		{
			AppSession.Shared.State.PlainRoot = true;
			RequestRender();
		}
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
		{
			SetContentView(_reconciler.Root);
			Log.Info(Tag, "set-content-view");
		}
		LastTitle = TitleOf(tree);
		LogTree();
		Log.Info(Tag, "rendered count=" + state.Count + " name-length=" + state.Name.Length + " title=" + LastTitle);
	}

	void LogTree()
	{
		if (_reconciler?.Root is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is null)
				continue;
			var text = child is TextView view ? view.Text ?? "" : "";
			Log.Info(
				Tag,
				"tree index=" + index + " handle=" + child.Handle.ToInt64() + " class=" + child.GetType().Name + " text=" + text);
		}
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
