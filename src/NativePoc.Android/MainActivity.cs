using System.Runtime.CompilerServices;
using System.Text;
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
	string _programSource = "";
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
		UiSurface.Bind(this, () => _reconciler?.Current);
#if DEBUG
		StateStore.TryLoad(this);
#endif
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

	protected override void OnResume()
	{
		base.OnResume();
		Current = this;
		UiSurface.Bind(this, () => _reconciler?.Current);
	}

	protected override void OnDestroy()
	{
		if (ReferenceEquals(Current, this))
			Current = null;
		UiSurface.Clear(this);
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
		var previousSource = _programSource;
		_programSource = "";
		try
		{
			return ApplyRender(render);
		}
		catch
		{
			_programSource = previousSource;
			throw;
		}
	}

	public string UseProgram(ScreenProgram.Bound program)
	{
		var previousSource = _programSource;
		_programSource = program.SourceHash;
		try
		{
			return ApplyRender(program.Render);
		}
		catch
		{
			_programSource = previousSource;
			throw;
		}
	}

	public string Answer(byte op, byte[] payload)
	{
		switch (op)
		{
			case ScreenQuery.Tree:
				return "source=" + _programSource + "\n" + ScreenSnapshot.FormatTree(CurrentTree());
			case ScreenQuery.State:
				return ScreenSnapshot.FormatState(AppSession.Shared.State) + PackingView.Capture().Format();
			case ScreenQuery.Restore:
				StateCheckpoint.ReadInto(payload.AsSpan(6), AppSession.Shared.State);
				if (StateCheckpoint.TryReadPacking(payload.AsSpan(6), out var packing))
					PackingView.Apply(packing);
				RequestRender();
				PackingActivity.RequestCurrent();
				return "restored";
			case ScreenQuery.Press:
				return ElementIds.Invoke(RequireTree(), Encoding.UTF8.GetString(payload.AsSpan(6)), null);
			case ScreenQuery.Enter:
				var body = Encoding.UTF8.GetString(payload.AsSpan(6));
				var split = body.IndexOf('\n');
				if (split <= 0)
					throw new InvalidOperationException("Enter needs an element id.");
				return ElementIds.Invoke(RequireTree(), body[..split], body[(split + 1)..]);
			default:
				throw new InvalidOperationException("Unknown screen query.");
		}
	}

	Element? CurrentTree() => UiSurface.Current() ?? _reconciler?.Current;

	Element RequireTree() => CurrentTree() ?? throw new InvalidOperationException("No screen is current.");

	string ApplyRender(Func<AppState, IPlatform, Element> render)
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
#if DEBUG
		StateStore.Save(this);
#endif
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
			case Switch:
				return "";
			default:
				throw Unknown(element);
		}
	}

	static InvalidOperationException Unknown(Element element) =>
		new("Unknown element.");
}
