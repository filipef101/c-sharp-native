using System.Globalization;
using Android.Provider;
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

	static Dir _pending;
	static FrameLayout? _stage;
	static View? _current;
	static View? _under;
	static bool _drag;
	static bool _skip;
	static bool _ending;
	static float _fraction;

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
			_stage = new NavStage(activity);
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
		if (_skip)
		{
			_skip = false;
			_drag = false;
			DropUnder();
			if (_current is not null && _current.Parent is ViewGroup held)
				held.RemoveView(_current);
			mounted.TranslationX = 0;
			_stage.AddView(mounted, Fill());
			_current = mounted;
			return;
		}

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

	public static void Nudge(float fraction) => Track(fraction);

	public static void Settle() => TrackEnd(false);

	internal static bool Dragging => _drag;

	internal static void TrackStart()
	{
		if (_drag || _stage is null || _stage.Handle == IntPtr.Zero || _stage.Context is not MainActivity activity)
			return;
		_drag = true;
		_fraction = 0;
		try
		{
			_under = activity.PaintBelow();
		}
		catch (Exception ex)
		{
			_under = null;
			Android.Util.Log.Info(MainActivity.Tag, "nav-swipe under-fail " + ex.GetType().Name);
		}

		if (_under is not null)
		{
			var width = Width(_stage);
			_under.TranslationX = width > 0 ? -width * 0.28f : 0;
			_stage.AddView(_under, 0, Fill());
		}

		Android.Util.Log.Info(
			MainActivity.Tag,
			"nav-swipe start under=" + (_under is null ? "0" : "1")
			+ " id=" + ScreenNav.Shared.UnderId());
	}

	internal static void Track(float fraction)
	{
		if (!_drag || _current is null || _current.Handle == IntPtr.Zero)
			return;
		_current.Animate()?.Cancel();
		_fraction = Math.Clamp(fraction, 0f, 1f);
		var width = Width(_current);
		_current.TranslationX = _fraction * width;
		if (_under is not null && _under.Handle != IntPtr.Zero)
			_under.TranslationX = (_fraction - 1f) * width * 0.28f;
	}

	internal static void TrackEnd(bool commit)
	{
		if (_ending)
			return;
		if (!_drag)
		{
			if (commit && _stage?.Context is MainActivity activity)
				activity.GoBack();
			return;
		}

		_ending = true;

		var pace = Pace();
		var ms = MotionContract.SettleMs(_fraction, commit, pace.Animator, pace.Motion);
		Android.Util.Log.Info(
			MainActivity.Tag,
			"nav-swipe " + (commit ? "commit" : "cancel")
			+ " progress=" + _fraction.ToString("0.00", CultureInfo.InvariantCulture)
			+ " scale=" + pace.Animator.ToString("0.0##", CultureInfo.InvariantCulture)
			+ " motion=" + pace.Motion.ToString("0.0##", CultureInfo.InvariantCulture)
			+ " ms=" + ms.ToString(CultureInfo.InvariantCulture)
			+ " instant=" + (pace.Instant ? "1" : "0"));
		var current = _current;
		var under = _under;
		var width = current is null ? 0 : Width(current);
		void Finish()
		{
			try
			{
				if (commit && _stage?.Context is MainActivity activity && ScreenNav.Shared.Depth > 1)
				{
					_skip = true;
					activity.GoBack();
				}

				if (_drag)
				{
					_skip = false;
					ReleaseDrag();
				}
			}
			finally
			{
				_ending = false;
			}
		}

		if (ms <= 0 || current is null || current.Handle == IntPtr.Zero)
		{
			if (current is not null && current.Handle != IntPtr.Zero)
				current.TranslationX = commit ? width : 0;
			Finish();
			return;
		}

		current.Animate()?.TranslationX(commit ? width : 0)?.SetDuration(ms)?.WithEndAction(new End(Finish))?.Start();
		if (under is not null && under.Handle != IntPtr.Zero)
			under.Animate()?.TranslationX(commit ? 0 : -width * 0.28f)?.SetDuration(ms)?.Start();
	}

	static void ReleaseDrag()
	{
		_drag = false;
		_fraction = 0;
		if (_current is not null && _current.Handle != IntPtr.Zero)
			_current.TranslationX = 0;
		DropUnder();
	}

	static void DropUnder()
	{
		var under = _under;
		_under = null;
		if (under is not null && under.Handle != IntPtr.Zero && under.Parent is ViewGroup parent)
			parent.RemoveView(under);
	}

	readonly struct PaceResult
	{
		public float Animator { get; init; }
		public float Motion { get; init; }
		public bool Instant { get; init; }
		public int Ms { get; init; }
	}

	static PaceResult Pace()
	{
		var animator = 1f;
		var motion = 1f;
		if (OperatingSystem.IsAndroidVersionAtLeast(17) && _stage?.Context?.ContentResolver is { } resolver)
		{
			animator = Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f);
			motion = Settings.Global.GetFloat(resolver, Settings.Global.TransitionAnimationScale, 1f);
		}

		var instant = MotionScale.Instant(animator, motion);
		return new PaceResult
		{
			Animator = animator,
			Motion = motion,
			Instant = instant,
			Ms = instant ? 0 : MotionScale.DurationMs(animator, motion),
		};
	}

	static void Slide(FrameLayout stage, View leaving, View arriving, bool push)
	{
		var pace = Pace();
		var ms = MotionContract.TransitionMs(pace.Animator, pace.Motion);
		var width = stage.Width > 0 ? stage.Width : Width(stage);
		if (width <= 0)
			width = 1;
		if (push)
		{
			stage.AddView(leaving, 0, Fill());
			leaving.TranslationX = 0;
			stage.AddView(arriving, Fill());
			arriving.TranslationX = width;
			arriving.Animate()?.TranslationX(0)?.SetDuration(ms)?.Start();
			leaving.Animate()?.TranslationX(-width / 4f)?.SetDuration(ms)?.WithEndAction(new End(() => Drop(stage, leaving)))?.Start();
			return;
		}

		arriving.TranslationX = -width / 4f;
		stage.AddView(arriving, 0, Fill());
		stage.AddView(leaving, Fill());
		arriving.Animate()?.TranslationX(0)?.SetDuration(ms)?.Start();
		leaving.Animate()?.TranslationX(width)?.SetDuration(ms)?.WithEndAction(new End(() => Drop(stage, leaving)))?.Start();
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

	sealed class NavStage : FrameLayout
	{
		float _x0;
		float _y0;
		long _t0;
		bool _mine;

		public NavStage(Android.Content.Context context) : base(context)
		{
		}

		public override bool OnInterceptTouchEvent(MotionEvent? ev)
		{
			if (ev is null || ScreenNav.Shared.Depth <= 1 || Dragging)
				return base.OnInterceptTouchEvent(ev);
			if (ev.ActionMasked == MotionEventActions.Down)
			{
				_x0 = ev.GetX();
				_y0 = ev.GetY();
				_t0 = Environment.TickCount64;
				_mine = false;
				return false;
			}

			if (ev.ActionMasked == MotionEventActions.Move && !_mine && _x0 <= EdgePx())
			{
				var dx = ev.GetX() - _x0;
				var dy = ev.GetY() - _y0;
				if (dx > EdgePx() && dx > Math.Abs(dy) * 1.2f)
				{
					_mine = true;
					TrackStart();
					Track(dx / Math.Max(1, Width));
					return true;
				}
			}

			return false;
		}

		public override bool OnTouchEvent(MotionEvent? ev)
		{
			if (!_mine || ev is null)
				return base.OnTouchEvent(ev);
			var dx = Math.Max(0f, ev.GetX() - _x0);
			var fraction = dx / Math.Max(1, Width);
			var action = ev.ActionMasked;
			if (action is MotionEventActions.Up or MotionEventActions.Cancel)
			{
				var dt = Math.Max(1, Environment.TickCount64 - _t0);
				var vx = dx / dt * 1000f;
				_mine = false;
				Track(fraction);
				var commit = action != MotionEventActions.Cancel && MotionContract.Commits(fraction, vx);
				TrackEnd(commit);
				return true;
			}

			Track(fraction);
			return true;
		}

		int EdgePx()
		{
			var density = Resources?.DisplayMetrics?.Density ?? 1f;
			if (density <= 0)
				density = 1f;
			return (int)(24 * density);
		}
	}

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
