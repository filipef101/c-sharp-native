using System.Runtime.CompilerServices;

namespace NativePoc;

public sealed class ScreenController : UIViewController
{
	public static ScreenController? Current { get; private set; }

	readonly Func<AppState, IPlatform, Element> _render = Screen.Render;
	UIKitHost? _host;
	ViewReconciler<UIView>? _reconciler;
	IosPlatform? _platform;

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
		_platform = new IosPlatform();
		_host = new UIKitHost(() => _reconciler?.Current);
		_reconciler = new ViewReconciler<UIView>(_host);
		RenderNow();
	}

	public void RequestRender() => RenderNow();

	void RenderNow()
	{
		if (_reconciler is null || _platform is null || View is null)
			return;
		var previous = _reconciler.Root;
		Element tree;
		if (ScreenNav.Shared.Top() is { } top)
			tree = top.Render(_platform);
		else
			tree = _render(AppSession.Shared.State, _platform);
		_reconciler.Render(tree);
		if (_reconciler.Root is null)
			throw new InvalidOperationException("Screen did not mount a root view.");
		if (!ReferenceEquals(previous, _reconciler.Root))
		{
			previous?.RemoveFromSuperview();
			View.AddSubview(_reconciler.Root);
		}
	}
}
