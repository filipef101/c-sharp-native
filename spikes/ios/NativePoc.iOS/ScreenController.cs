using System.Runtime.CompilerServices;

namespace NativePoc;

public sealed class ScreenController : UIViewController
{
	public static ScreenController? Current { get; private set; }

	readonly Func<AppState, IPlatform, Element> _render = Screen.Render;
	UIKitHost? _host;
	ViewReconciler<UIView>? _reconciler;
	PlatformBridge? _platform;
	bool _inRender;
	bool _renderAgain;

	public override void ViewDidLoad()
	{
		base.ViewDidLoad();
		Current = this;
		View!.BackgroundColor = UIColor.SystemBackground;
		Console.WriteLine(
			"NativePoc runtime IsDynamicCodeSupported="
			+ RuntimeFeature.IsDynamicCodeSupported
			+ " IsDynamicCodeCompiled="
			+ RuntimeFeature.IsDynamicCodeCompiled);
		_platform = new PlatformBridge(this);
		_host = new UIKitHost(() => _reconciler?.Current);
		_reconciler = new ViewReconciler<UIView>(_host);
		RenderNow();
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

		if (NSThread.IsMain)
			Run();
		else
			InvokeOnMainThread(Run);
	}

	public override void ViewDidLayoutSubviews()
	{
		base.ViewDidLayoutSubviews();
		LayoutRoot();
	}

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
			AttachRoot(_reconciler.Root);
		var title = TitleOf(tree);
		Console.WriteLine(
			"NativePoc rendered count="
			+ state.Count
			+ " name-length="
			+ state.Name.Length
			+ " title="
			+ title);
	}

	void AttachRoot(UIView root)
	{
		var view = View ?? throw new InvalidOperationException("The controller has no view.");
		var existing = view.Subviews;
		for (var index = existing.Length - 1; index >= 0; index--)
			existing[index].RemoveFromSuperview();
		root.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
		view.AddSubview(root);
		LayoutRoot();
	}

	void LayoutRoot()
	{
		if (View is null || _reconciler?.Root is not UIView root)
			return;
		var inset = View.SafeAreaInsets;
		var frame = View.Bounds;
		frame.X = inset.Left + 16;
		frame.Y = inset.Top + 16;
		frame.Width -= inset.Left + inset.Right + 32;
		frame.Height -= inset.Top + inset.Bottom + 32;
		root.Frame = frame;
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
				throw new InvalidOperationException("Unknown element.");
		}
	}
}
