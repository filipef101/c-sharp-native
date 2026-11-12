using Android.Views;
using Android.Widget;

namespace NativePoc;

static class NavMotion
{
	internal enum Dir
	{
		None,
		Push,
		Pop,
	}

	const int DurationMs = 180;

	static Dir _pending;
	static FrameLayout? _stage;
	static View? _current;

	internal static void Arm(Dir dir) => _pending = dir;

	public static View? Detach(ref ViewReconciler<View>? reconciler, AndroidHost host)
	{
		if (_pending == Dir.None || reconciler?.Root is not View root)
			return null;
		if (root.Parent is ViewGroup parent)
			parent.RemoveView(root);
		reconciler = new ViewReconciler<View>(host);
		return root;
	}

	public static void Present(Activity activity, View mounted, View? leaving)
	{
		var dir = _pending;
		_pending = Dir.None;
		if (_stage is null || _stage.Handle == IntPtr.Zero)
		{
			_stage = new FrameLayout(activity);
			_stage.SetClipChildren(false);
			_stage.SetClipToPadding(false);
			_stage.SetOnApplyWindowInsetsListener(new SystemBarInset());
			activity.SetContentView(_stage);
			_stage.RequestApplyInsets();
			_stage.AddView(mounted, Fill());
			_current = mounted;
			Android.Util.Log.Info(MainActivity.Tag, "set-content-view");
			StartupMarks.Phase("content-view");
			return;
		}

		if (ReferenceEquals(_current, mounted))
			return;
		if (leaving is null || dir == Dir.None || _current is null)
		{
			if (_current is not null && _current.Parent is ViewGroup parent)
				parent.RemoveView(_current);
			_stage.AddView(mounted, Fill());
			_current = mounted;
			Android.Util.Log.Info(MainActivity.Tag, "set-content-view");
			StartupMarks.Phase("content-view");
			return;
		}

		var push = dir == Dir.Push;
#if NATIVEPOC_TRANSITION
		NavTransition.Play(_stage, leaving, mounted, push);
#else
		Android.Util.Log.Info(MainActivity.Tag, "nav-slide dir=" + (push ? "push" : "pop"));
		Slide(_stage, leaving, mounted, push);
#endif
		_current = mounted;
	}

	public static void Nudge(float fraction)
	{
		if (_current is null || _current.Handle == IntPtr.Zero)
			return;
		_current.Animate()?.Cancel();
		var width = Width(_current);
		_current.TranslationX = Math.Clamp(fraction, 0f, 1f) * width;
	}

	public static void Settle()
	{
		if (_current is null || _current.Handle == IntPtr.Zero)
			return;
		_current.Animate()?.TranslationX(0)?.SetDuration(120)?.Start();
	}

	static void Slide(FrameLayout stage, View leaving, View arriving, bool push)
	{
		var width = stage.Width > 0 ? stage.Width : Width(stage);
		if (width <= 0)
			width = 1;
		if (push)
		{
			stage.AddView(leaving, 0, Fill());
			leaving.TranslationX = 0;
			stage.AddView(arriving, Fill());
			arriving.TranslationX = width;
			arriving.Animate()?.TranslationX(0)?.SetDuration(DurationMs)?.Start();
			leaving.Animate()?.TranslationX(-width / 4f)?.SetDuration(DurationMs)?.WithEndAction(new End(() => Drop(stage, leaving)))?.Start();
			return;
		}

		arriving.TranslationX = -width / 4f;
		stage.AddView(arriving, 0, Fill());
		stage.AddView(leaving, Fill());
		arriving.Animate()?.TranslationX(0)?.SetDuration(DurationMs)?.Start();
		leaving.Animate()?.TranslationX(width)?.SetDuration(DurationMs)?.WithEndAction(new End(() => Drop(stage, leaving)))?.Start();
	}

	static void Drop(FrameLayout stage, View leaving)
	{
		if (leaving.Handle != IntPtr.Zero && leaving.Parent == stage)
			stage.RemoveView(leaving);
	}

	static int Width(View view)
	{
		if (view.Width > 0)
			return view.Width;
		return view.Resources?.DisplayMetrics?.WidthPixels ?? 0;
	}

	static FrameLayout.LayoutParams Fill() =>
		new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

	sealed class SystemBarInset : Java.Lang.Object, View.IOnApplyWindowInsetsListener
	{
		bool _logged;

		public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
		{
#pragma warning disable CA1416
			var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
			v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
			StartupMarks.Phase("insets");
			if (!_logged)
			{
				_logged = true;
				Android.Util.Log.Info(
					MainActivity.Tag,
					"startup insets top=" + bars.Top.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ " bottom=" + bars.Bottom.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ " left=" + bars.Left.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ " right=" + bars.Right.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}

			return WindowInsets.Consumed;
#pragma warning restore CA1416
		}
	}

	sealed class End : Java.Lang.Object, Java.Lang.IRunnable
	{
		readonly Action _done;

		public End(Action done) => _done = done;

		public void Run() => _done();
	}
}
