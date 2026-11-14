using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using AndroidWidget = Android.Widget.ScrollView;

namespace NativePoc;

sealed class GestureColumn : LinearLayout
{
	public bool Tracking { get; private set; }

	public Action? OnReleased { get; set; }

	byte _mask;
	Action? _tap;
	Action? _longPress;
	Action? _swipe;
	Action? _longTask;
	float _downX;
	float _downY;
	bool _longSent;
	bool _moved;
	readonly int _slop;
	readonly float _density;
#if NATIVEPOC_STARTUP_MARKS
	string? _startupKey;
	bool _measureLogged;
	bool _layoutLogged;
	bool _drawLogged;
#endif

	public GestureColumn(Context context)
		: base(context)
	{
		var config = ViewConfiguration.Get(context);
		_slop = config?.ScaledTouchSlop ?? 8;
		_density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		if (_density <= 0)
			_density = 1f;
	}

	internal void MarkStartup(string key)
	{
#if NATIVEPOC_STARTUP_MARKS
		_startupKey = key;
#else
		_ = key;
#endif
	}

	public void Configure(byte mask, Action? tap, Action? longPress, Action? swipe)
	{
		_mask = mask;
		_tap = tap;
		_longPress = longPress;
		_swipe = swipe;
	}

	protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
	{
#if NATIVEPOC_STARTUP_MARKS
		var mark = _startupKey is not null && !_measureLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
#endif
		base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
#if NATIVEPOC_STARTUP_MARKS
		if (mark)
		{
			_measureLogged = true;
			StartupMarks.Took(StartupPhase.Name(_startupKey!, "measure"), started);
		}
#endif
	}

#if NATIVEPOC_STARTUP_MARKS
	protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
	{
		var mark = _startupKey is not null && !_layoutLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		base.OnLayout(changed, left, top, right, bottom);
		if (!mark)
			return;
		_layoutLogged = true;
		StartupMarks.Took(StartupPhase.Name(_startupKey!, "layout"), started);
	}

	protected override void DispatchDraw(Canvas canvas)
	{
		var mark = _startupKey is not null && !_drawLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		base.DispatchDraw(canvas);
		if (!mark)
			return;
		_drawLogged = true;
		StartupMarks.Took(StartupPhase.Name(_startupKey!, "draw"), started);
	}
#endif

	public override bool OnInterceptTouchEvent(MotionEvent? ev)
	{
		if (ev is null || _mask == 0)
			return base.OnInterceptTouchEvent(ev);
		return Track(ev);
	}

	public override bool OnTouchEvent(MotionEvent? ev)
	{
		if (ev is null || _mask == 0)
			return base.OnTouchEvent(ev);
		Track(ev);
		return true;
	}

	bool Track(MotionEvent ev)
	{
		var action = ev.ActionMasked;
		if (action == MotionEventActions.Down)
		{
			Tracking = true;
			_downX = ev.GetX();
			_downY = ev.GetY();
			_longSent = false;
			_moved = false;
			CancelLong();
			if ((_mask & Gestures.LongPress) != 0)
			{
				_longTask = () =>
				{
					if (!Tracking || _moved || _longSent)
						return;
					_longSent = true;
					_longPress?.Invoke();
				};
				PostDelayed(_longTask, 400);
			}

			return false;
		}

		var dx = ev.GetX() - _downX;
		var dy = ev.GetY() - _downY;
		var absX = Math.Abs(dx);
		var absY = Math.Abs(dy);
		if (action == MotionEventActions.Move)
		{
			if (absX > _slop || absY > _slop)
			{
				_moved = true;
				CancelLong();
			}

			if ((_mask & Gestures.Swipe) != 0 && absX > 48 * _density && absX > absY * 1.5f)
			{
				Parent?.RequestDisallowInterceptTouchEvent(true);
				return true;
			}

			return false;
		}

		if (action is not (MotionEventActions.Up or MotionEventActions.Cancel))
			return false;
		CancelLong();
		var claim = false;
		if (action == MotionEventActions.Up
			&& !_longSent
			&& (_mask & Gestures.Swipe) != 0
			&& absX > 72 * _density
			&& absX > absY * 1.5f)
		{
			claim = true;
			_swipe?.Invoke();
		}
		else if (action == MotionEventActions.Up && !_longSent && !_moved && (_mask & Gestures.Tap) != 0)
		{
			claim = true;
			_tap?.Invoke();
		}

		Tracking = false;
		var done = OnReleased;
		OnReleased = null;
		done?.Invoke();
		return claim;
	}

	void CancelLong()
	{
		if (_longTask is null)
			return;
		RemoveCallbacks(_longTask);
		_longTask = null;
	}
}

sealed class TapClick : Java.Lang.Object, View.IOnClickListener
{
	readonly Action _fire;

	public TapClick(Action fire) => _fire = fire;

	public void OnClick(View? v) => _fire();
}

sealed class LongClick : Java.Lang.Object, View.IOnLongClickListener
{
	readonly Action _fire;

	public LongClick(Action fire) => _fire = fire;

	public bool OnLongClick(View? v)
	{
		_fire();
		return true;
	}
}

sealed class RefreshScroll : AndroidWidget
{
	public bool Tracking { get; private set; }

	public Action? OnReleased { get; set; }

	bool _enabled;
	Action? _refresh;
	float _downY;
	bool _pulled;
	readonly float _density;

	bool _revealPosted;
#if NATIVEPOC_STARTUP_MARKS
	long _measureTick;
	long _measureEnd;
	bool _measureLogged;
	int _measurePasses;
	bool _layoutLogged;
	bool _drawLogged;
	bool _traversalLogged;
#endif

	public RefreshScroll(Context context)
		: base(context)
	{
		_density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		if (_density <= 0)
			_density = 1f;
	}

	protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
	{
#if NATIVEPOC_STARTUP_MARKS
		var mark = !_measureLogged;
		var again = !mark && _measurePasses == 1;
		var started = mark || again ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		if (mark)
		{
			_measureLogged = true;
			if (_measureTick == 0)
				_measureTick = started;
		}
#endif
		base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
#if NATIVEPOC_STARTUP_MARKS
		if (mark)
		{
			StartupMarks.Took("scroll-measure", started);
			_measureEnd = System.Diagnostics.Stopwatch.GetTimestamp();
		}
		else if (again)
			StartupMarks.Took("scroll-measure-again", started);
		_measurePasses++;
#endif
	}

	protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
	{
#if NATIVEPOC_STARTUP_MARKS
		var mark = !_layoutLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		if (mark)
			_layoutLogged = true;
		if (mark && _measureEnd != 0)
			StartupMarks.Took("relayout-gap", _measureEnd);
#endif
		base.OnLayout(changed, left, top, right, bottom);
		StartupMarks.Phase("layout");
#if NATIVEPOC_STARTUP_MARKS
		if (mark)
			StartupMarks.Took("scroll-layout", started);
		if (!_traversalLogged && _measureTick != 0)
		{
			_traversalLogged = true;
			StartupMarks.Took("traversal", _measureTick);
		}
#endif
		if (_revealPosted)
			return;
		_revealPosted = true;
		// The warmed render lands a couple of frames later and can put the
		// scroll back at the top. Reveal after that, so a control sitting on
		// the bottom edge is scrolled until 48 dp of it is on screen.
		PostDelayed(RevealClippedTargets, 400);
	}

#if NATIVEPOC_STARTUP_MARKS
	protected override void DispatchDraw(Canvas canvas)
	{
		var mark = !_drawLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		if (mark)
			_drawLogged = true;
		base.DispatchDraw(canvas);
		if (mark)
			StartupMarks.Took("scroll-draw", started);
	}
#endif

	void RevealClippedTargets()
	{
		if (Handle == IntPtr.Zero)
			return;
		var min = (int)Math.Ceiling(48 * _density);
		var viewport = new Rect();
		if (!GetGlobalVisibleRect(viewport) || viewport.Height() < min)
			return;
		var need = 0;
		MeasureClip(this, min, ref need);
		var slack = HeadingSlack(viewport);
		if (need > slack)
			need = slack;
		if (need > 0)
			ScrollBy(0, need);
		Android.Util.Log.Info(MainActivity.Tag, "a11y-reveal need=" + need.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}

	void MeasureClip(View view, int min, ref int need)
	{
		if (!ReferenceEquals(view, this) && ClippedTarget(view))
		{
			var rect = new Rect();
			if (view.GetGlobalVisibleRect(rect))
			{
				var shown = rect.Height();
				var gap = min - shown;
				if (shown > 0 && gap > 0 && view.Height >= min && gap > need)
					need = gap;
			}
		}

		if (view is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				MeasureClip(child, min, ref need);
		}
	}

	int HeadingSlack(Rect viewport)
	{
		var heading = FindHeading(this);
		if (heading is null)
			return int.MaxValue;
		var rect = new Rect();
		if (!heading.GetGlobalVisibleRect(rect) || rect.Height() <= 0)
			return 0;
		var contentTop = viewport.Top + PaddingTop;
		return Math.Max(0, rect.Top - contentTop);
	}

	static View? FindHeading(View view)
	{
		if (view is Android.Widget.TextView text && (int)Android.OS.Build.VERSION.SdkInt >= 28)
		{
#pragma warning disable CA1416
			if (text.AccessibilityHeading)
				return text;
#pragma warning restore CA1416
		}

		if (view is not ViewGroup group)
			return null;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is null)
				continue;
			var found = FindHeading(child);
			if (found is not null)
				return found;
		}

		return null;
	}

	static bool ClippedTarget(View view) =>
		view is Android.Widget.ImageView || view is Android.Widget.Switch || view.Clickable || view.LongClickable;

	public void Configure(bool enabled, Action? refresh)
	{
		_enabled = enabled;
		_refresh = refresh;
	}

	public override bool OnInterceptTouchEvent(MotionEvent? ev)
	{
		if (ev is not null && _enabled)
			Track(ev);
		return base.OnInterceptTouchEvent(ev);
	}

	public override bool OnTouchEvent(MotionEvent? ev)
	{
		if (ev is not null && _enabled)
			Track(ev);
		return base.OnTouchEvent(ev);
	}

	void Track(MotionEvent ev)
	{
		var action = ev.ActionMasked;
		if (action == MotionEventActions.Down || !Tracking)
		{
			Tracking = true;
			_downY = ev.GetY();
			_pulled = false;
			if (action == MotionEventActions.Down)
				return;
		}

		if (action == MotionEventActions.Move && ScrollY <= 0 && ev.GetY() - _downY > 64 * _density)
		{
			_pulled = true;
			return;
		}

		if (action is not (MotionEventActions.Up or MotionEventActions.Cancel))
			return;
		if (action == MotionEventActions.Up && _pulled && ScrollY <= 0)
			_refresh?.Invoke();
		Tracking = false;
		_pulled = false;
		var done = OnReleased;
		OnReleased = null;
		done?.Invoke();
	}
}

static class StartupPhase
{
	public static string Name(string key, string part)
	{
		if (key == "stats")
			return "stat-" + part;
		if (key == "row-0")
			return "grid-" + part;
		return key + "-" + part;
	}
}

sealed class StartupButton : Android.Widget.Button
{
	readonly ColorDrawable _plain;
	Drawable? _frameworkBackground;
	Drawable? _frameworkForeground;
	StateListAnimator? _frameworkAnimator;
	float _frameworkElevation;
	bool _keepFramework = true;

	public StartupButton(Context context)
		: base(context)
	{
		// Theme.Material's button is a rounded nine-patch plus a raised shadow.
		// Those shaders compile inside the first frame. Keep the drawables and
		// put them back after the reported frame. A replacement background wins.
		_plain = new ColorDrawable(Color.Transparent);
		_frameworkBackground = Background;
		_frameworkForeground = Foreground;
		_frameworkAnimator = StateListAnimator;
		_frameworkElevation = Elevation;
		SetBackgroundResource(0);
		Background = _plain;
		BackgroundTintList = null;
		Foreground = null;
		StateListAnimator = null;
		Elevation = 0;
		if (StartupMarks.Presented)
			RestoreFrameworkChrome();
		else
			StartupMarks.AfterPresent(RestoreFrameworkChrome);
	}

	internal void DropFrameworkChrome()
	{
		_keepFramework = false;
		_frameworkBackground = null;
		_frameworkForeground = null;
		_frameworkAnimator = null;
	}

	void RestoreFrameworkChrome()
	{
		if (!_keepFramework || Handle == IntPtr.Zero || !StillPlain())
			return;
		_keepFramework = false;
		if (_frameworkBackground is not null)
			Background = _frameworkBackground;
		if (Foreground is null && _frameworkForeground is not null)
			Foreground = _frameworkForeground;
		if (StateListAnimator is null && _frameworkAnimator is not null)
			StateListAnimator = _frameworkAnimator;
		Elevation = _frameworkElevation;
		_frameworkBackground = null;
		_frameworkForeground = null;
		_frameworkAnimator = null;
	}

	bool StillPlain()
	{
		var background = Background;
		return background is not null && _plain.Handle != IntPtr.Zero && background.Handle == _plain.Handle;
	}

#if NATIVEPOC_STARTUP_MARKS
	string? _startupKey;
	bool _measureLogged;
	bool _layoutLogged;
	bool _drawLogged;
#endif

	internal void MarkStartup(string key)
	{
#if NATIVEPOC_STARTUP_MARKS
		_startupKey = key;
#else
		_ = key;
#endif
	}

#if NATIVEPOC_STARTUP_MARKS
	protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
	{
		var mark = _startupKey is not null && !_measureLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
		if (!mark)
			return;
		_measureLogged = true;
		StartupMarks.Took(StartupPhase.Name(_startupKey!, "measure"), started);
	}

	protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
	{
		var mark = _startupKey is not null && !_layoutLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		base.OnLayout(changed, left, top, right, bottom);
		if (!mark)
			return;
		_layoutLogged = true;
		StartupMarks.Took(StartupPhase.Name(_startupKey!, "layout"), started);
	}

	protected override void OnDraw(Canvas canvas)
	{
		var mark = _startupKey is not null && !_drawLogged;
		var started = mark ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		base.OnDraw(canvas);
		if (!mark)
			return;
		_drawLogged = true;
		StartupMarks.Took(StartupPhase.Name(_startupKey!, "draw"), started);
	}
#endif
}
