using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidScroll = Android.Widget.ScrollView;

namespace NativePoc;

sealed class RowWindow : AndroidScroll
{
	const int Window = 20;

	readonly FrameLayout _canvas;
	readonly TextView[] _slots;
	readonly int _rowHeight;
	IReadOnlyList<Element> _rows = Array.Empty<Element>();
	int _origin = -1;
	bool _refreshEnabled;
	Action? _refresh;
	float _downY;
	bool _pulled;
	readonly float _density;

	public bool Tracking { get; private set; }

	public Action? OnReleased { get; set; }

	public RowWindow(Context context)
		: base(context)
	{
		_density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		if (_density <= 0)
			_density = 1f;
		_rowHeight = (int)(48 * _density);
		if (_rowHeight < 1)
			_rowHeight = 48;
		_canvas = new FrameLayout(context);
		AddView(_canvas, new LayoutParams(ViewGroup.LayoutParams.MatchParent, _rowHeight));
		_slots = new TextView[Window];
		for (var index = 0; index < _slots.Length; index++)
		{
			var text = new TextView(context);
			_slots[index] = text;
			_canvas.AddView(text, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, _rowHeight));
		}

		ViewTreeObserver?.AddOnScrollChangedListener(new ScrollListener(this));
	}

	public void ConfigureRefresh(bool enabled, Action? refresh)
	{
		_refreshEnabled = enabled;
		_refresh = refresh;
	}

	public override bool OnTouchEvent(MotionEvent? ev)
	{
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
		_canvas.LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, Math.Max(_rowHeight, rows.Count * _rowHeight));
		_origin = -1;
		Fill();
	}

	public void Fill()
	{
		var first = Math.Max(0, ScrollY / _rowHeight);
		if (first == _origin)
			return;
		_origin = first;
		for (var slot = 0; slot < _slots.Length; slot++)
		{
			var index = first + slot;
			var view = _slots[slot];
			if (index >= _rows.Count)
			{
				view.Visibility = ViewStates.Gone;
				continue;
			}

			view.Visibility = ViewStates.Visible;
			if (view.LayoutParameters is FrameLayout.LayoutParams layout)
			{
				layout.TopMargin = index * _rowHeight;
				view.LayoutParameters = layout;
			}

			var label = Label(_rows[index]);
			view.Text = label;
			view.ContentDescription = label;
		}
	}

	static string Label(Element element)
	{
		switch (element)
		{
			case Text text:
				return text.Value;
			case Column column when column.Children.Count > 0:
				return Label(column.Children[0]);
			case Button button:
				return button.Title;
			case TextField field:
				return field.Value;
			case Switch:
			case ScrollView:
			case ListView:
			case Image:
			case HostView:
				return "";
			default:
				throw new InvalidOperationException("Unknown element.");
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
