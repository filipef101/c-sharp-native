using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidScroll = Android.Widget.ScrollView;

namespace NativePoc;

sealed class RowWindow : AndroidScroll
{
	const int Window = 20;

	readonly FrameLayout _canvas;
	readonly RowSlot[] _slots;
	int _canvasPx = -1;
	readonly float _density;
	readonly float _fontScale;
	readonly int _line;
	readonly int _image;
	IReadOnlyList<Element> _rows = Array.Empty<Element>();
	RowPrefix? _prefix;
	int _origin = -1;
	int _stamp = -1;
	bool _refreshEnabled;
	Action? _refresh;
	float _downY;
	bool _pulled;

	public bool Tracking { get; private set; }

	public Action? OnReleased { get; set; }

	public RowWindow(Context context)
		: base(context)
	{
		_density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		if (_density <= 0)
			_density = 1f;
		_fontScale = context.Resources?.Configuration?.FontScale ?? 1f;
		if (_fontScale <= 0)
			_fontScale = 1f;
		_line = RowMetrics.Line(_density, _fontScale);
		_image = RowMetrics.ImageBlock(_density);
		_canvas = new FrameLayout(context);
		AddView(_canvas, new AndroidScroll.LayoutParams(ViewGroup.LayoutParams.MatchParent, _line));
		_slots = new RowSlot[Window];
		for (var index = 0; index < _slots.Length; index++)
		{
			var slot = new RowSlot(context, _image);
			_slots[index] = slot;
			_canvas.AddView(slot, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, _line));
		}

		ViewTreeObserver?.AddOnScrollChangedListener(new ScrollListener(this));
	}

	public void ConfigureRefresh(bool enabled, Action? refresh)
	{
		_refreshEnabled = enabled;
		_refresh = refresh;
	}

	protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
	{
		if (MeasureSpec.GetMode(heightMeasureSpec) == MeasureSpecMode.Unspecified)
		{
			var screen = Context?.Resources?.DisplayMetrics?.HeightPixels ?? 0;
			if (screen > 0)
				heightMeasureSpec = MeasureSpec.MakeMeasureSpec(screen, MeasureSpecMode.AtMost);
		}

		base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
	}

	public override bool OnInterceptTouchEvent(MotionEvent? ev)
	{
		if (ev is not null && ev.ActionMasked == MotionEventActions.Down)
			Parent?.RequestDisallowInterceptTouchEvent(true);
		return base.OnInterceptTouchEvent(ev);
	}

	public override bool OnTouchEvent(MotionEvent? ev)
	{
		if (ev is not null && ev.ActionMasked == MotionEventActions.Down)
			Parent?.RequestDisallowInterceptTouchEvent(true);
		if (ev is not null && _refreshEnabled)
			Track(ev);
		return base.OnTouchEvent(ev);
	}

	void Track(MotionEvent ev)
	{
		var action = ev.ActionMasked;
		if (action == MotionEventActions.Down)
		{
			Tracking = true;
			_downY = ev.GetY();
			_pulled = false;
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

	public void Bind(IReadOnlyList<Element> rows)
	{
		_rows = rows;
		_prefix = new RowPrefix(rows.Count, _line);
		_origin = -1;
		_stamp = -1;
		_canvasPx = -1;
		for (var slot = 0; slot < _slots.Length; slot++)
			_slots[slot].Reset();
		Fill();
	}

	public void Fill()
	{
		var first = FirstIndex();
		var stamp = first;
		var last = Math.Min(_rows.Count, first + _slots.Length);
		for (var index = first; index < last; index++)
		{
			var face = RowFace.Read(_rows[index]);
			var height = RowMetrics.Height(face, _density, _fontScale);
			_prefix?.Set(index, height);
			stamp = unchecked(stamp * 31 + height);
			stamp = unchecked(stamp * 31 + face.Key.GetHashCode(StringComparison.Ordinal));
			stamp = unchecked(stamp * 31 + face.Title.GetHashCode(StringComparison.Ordinal));
			stamp = unchecked(stamp * 31 + face.Subtitle.GetHashCode(StringComparison.Ordinal));
			stamp = unchecked(stamp * 31 + face.Picture.GetHashCode(StringComparison.Ordinal));
		}

		if (first == _origin && stamp == _stamp)
			return;
		_origin = first;
		_stamp = stamp;
		var canvasHeight = CanvasHeight();
		Log.Info(
			"NativePoc",
			"row-window view=" + Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " canvas=" + canvasHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " scrollY=" + ScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " first=" + first.ToString(System.Globalization.CultureInfo.InvariantCulture));
		if (canvasHeight != _canvasPx)
		{
			_canvasPx = canvasHeight;
			_canvas.LayoutParameters = new AndroidScroll.LayoutParams(ViewGroup.LayoutParams.MatchParent, canvasHeight);
			_canvas.SetMinimumHeight(canvasHeight);
		}

		var current = new int[_slots.Length];
		for (var slot = 0; slot < _slots.Length; slot++)
			current[slot] = _slots[slot].Shown;
		var plan = RowWindowPlan.Assign(current, first, _rows.Count, _slots.Length);
		for (var slot = 0; slot < _slots.Length; slot++)
		{
			var index = plan[slot];
			var view = _slots[slot];
			if (index < 0)
			{
				view.Reset();
				continue;
			}

			view.Visibility = ViewStates.Visible;
			var face = RowFace.Read(_rows[index]);
			var height = _prefix?.HeightAt(index) ?? _line;
			var top = _prefix?.Y(index) ?? 0;
			if (view.LayoutParameters is FrameLayout.LayoutParams layout
				&& (layout.TopMargin != top || layout.Height != height))
			{
				layout.TopMargin = top;
				layout.Height = height;
				view.LayoutParameters = layout;
			}

			var content = Content(face);
			if (view.Shown == index && view.Content == content)
				continue;
			view.Show(face, index, content, Context);
		}
	}

	static int Content(RowFace face)
	{
		var stamp = face.Key.GetHashCode(StringComparison.Ordinal);
		stamp = unchecked(stamp * 31 + face.Title.GetHashCode(StringComparison.Ordinal));
		stamp = unchecked(stamp * 31 + face.Subtitle.GetHashCode(StringComparison.Ordinal));
		stamp = unchecked(stamp * 31 + face.Picture.GetHashCode(StringComparison.Ordinal));
		return stamp;
	}

	int FirstIndex()
	{
		if (_prefix is null || _rows.Count == 0)
			return 0;
		return _prefix.IndexAt(Math.Max(0, ScrollY));
	}

	int CanvasHeight()
	{
		if (_rows.Count == 0 || _prefix is null)
			return _line;
		return Math.Max(_line, _prefix.Total);
	}

	sealed class RowSlot : LinearLayout
	{
		readonly TextView _title;
		readonly TextView _subtitle;
		readonly ImageView _picture;
		readonly int _image;

		public string Key = "";

		public int Shown = -1;

		public int Content;

		public RowSlot(Context context, int image)
			: base(context)
		{
			_image = image;
			Orientation = Orientation.Vertical;
			ImportantForAccessibility = ImportantForAccessibility.No;
			_title = new TextView(context);
			_subtitle = new TextView(context);
			_picture = new ImageView(context);
			_title.SetTextSize(ComplexUnitType.Sp, 16);
			_subtitle.SetTextSize(ComplexUnitType.Sp, 14);
			AddView(_title, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
			AddView(_subtitle, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
			AddView(_picture, new LayoutParams(ViewGroup.LayoutParams.MatchParent, image));
		}

		public void Reset()
		{
			if (Shown < 0 && Visibility == ViewStates.Gone)
				return;
			Shown = -1;
			Content = 0;
			Key = "";
			Visibility = ViewStates.Gone;
		}

		public void Show(RowFace face, int index, int content, Context? context)
		{
			Shown = index;
			Content = content;
			Key = face.Key;
			_title.Text = face.Title;
			var description = face.Describe();
			_title.ContentDescription = description;
			ContentDescription = description;
			if (face.Subtitle.Length == 0)
			{
				_subtitle.Text = "";
				_subtitle.Visibility = ViewStates.Gone;
			}
			else
			{
				_subtitle.Text = face.Subtitle;
				_subtitle.ContentDescription = face.Subtitle;
				_subtitle.Visibility = ViewStates.Visible;
			}

			if (face.Picture.Length == 0)
			{
				_picture.SetImageDrawable(null);
				_picture.ContentDescription = null;
				_picture.Visibility = ViewStates.Gone;
				return;
			}

			PictureBinder.Show(_picture, face.Picture, context ?? throw new InvalidOperationException("List row has no context."));
			if (_picture.LayoutParameters is LayoutParams layout)
			{
				layout.Height = _image;
				_picture.LayoutParameters = layout;
			}
		}
	}

	sealed class ScrollListener : Java.Lang.Object, ViewTreeObserver.IOnScrollChangedListener
	{
		readonly RowWindow _window;

		public ScrollListener(RowWindow window)
		{
			_window = window;
		}

		public void OnScrollChanged() => _window.Fill();
	}
}
