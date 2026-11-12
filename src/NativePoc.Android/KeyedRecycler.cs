#if NATIVEPOC_RECYCLER
using System.Globalization;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;

namespace NativePoc;

sealed class KeyedRecycler : RecyclerView
{
	readonly RowAdapter _rows;

	public KeyedRecycler(Context context)
		: base(context)
	{
		SetLayoutManager(new LinearLayoutManager(context));
		_rows = new RowAdapter(context);
		_rows.HasStableIds = true;
		SetAdapter(_rows);
		SetItemViewCacheSize(8);
		NestedScrollingEnabled = true;
		AddOnScrollListener(new IdleLog(this));
	}

	public void Bind(IReadOnlyList<Element> rows)
	{
		var saved = First();
		_rows.Set(rows);
		Post(() => Hold(saved));
	}

	public int First()
	{
		if (GetLayoutManager() is LinearLayoutManager linear)
		{
			var index = linear.FindFirstVisibleItemPosition();
			return index < 0 ? 0 : index;
		}

		return 0;
	}

	void Hold(int saved)
	{
		var seen = First();
		var restored = saved > 0 && seen <= 0;
		if (restored)
			ScrollToPosition(saved);
		Log.Info(
			MainActivity.Tag,
			"list-first=" + (restored ? saved : seen).ToString(CultureInfo.InvariantCulture)
			+ " saved=" + saved.ToString(CultureInfo.InvariantCulture)
			+ " restored=" + (restored ? "1" : "0")
			+ " rows=" + _rows.ItemCount.ToString(CultureInfo.InvariantCulture)
			+ " built=" + _rows.Built.ToString(CultureInfo.InvariantCulture)
			+ " views=" + ChildCount.ToString(CultureInfo.InvariantCulture));
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

	sealed class IdleLog : OnScrollListener
	{
		readonly KeyedRecycler _list;

		public IdleLog(KeyedRecycler list) => _list = list;

		public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
		{
			if (newState == ScrollStateIdle)
				_list.Hold(_list.First());
		}
	}

	sealed class RowAdapter : Adapter
	{
		readonly Context _context;
		IReadOnlyList<Element> _rows = Array.Empty<Element>();

		public RowAdapter(Context context) => _context = context;

		public int Built => _rows is RecordProgram.WindowList window ? window.Built : _rows.Count;

		public override int ItemCount => _rows.Count;

		public void Set(IReadOnlyList<Element> rows)
		{
			_rows = rows;
			NotifyDataSetChanged();
		}

		public override long GetItemId(int position)
		{
			var key = KeyAt(position);
			ulong hash = 14695981039346656037;
			foreach (var ch in key)
			{
				hash ^= ch;
				hash *= 1099511628211;
			}

			return (long)hash;
		}

		public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType) =>
			new RowHolder(new RowCell(_context));

		public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
		{
			if (holder is not RowHolder row)
				return;
			row.Show(RowFace.Read(_rows[position]));
		}

		string KeyAt(int index)
		{
			if (_rows is RecordProgram.WindowList window)
			{
				for (var field = 0; field < 8; field++)
				{
					var cell = window.Cell(index, field);
					if (cell.Length > 0)
						return cell;
				}
			}
			else
			{
				var key = _rows[index].Key;
				if (key is { Length: > 0 })
					return key;
			}

			return index.ToString(CultureInfo.InvariantCulture);
		}
	}

	sealed class RowHolder : RecyclerView.ViewHolder
	{
		readonly RowCell _cell;

		public RowHolder(RowCell cell)
			: base(cell) => _cell = cell;

		public void Show(RowFace face) => _cell.Show(face);
	}

	sealed class RowCell : LinearLayout
	{
		readonly TextView _title;
		readonly TextView _subtitle;
		readonly ImageView _picture;

		public RowCell(Context context)
			: base(context)
		{
			Orientation = Orientation.Vertical;
			_title = new TextView(context);
			_subtitle = new TextView(context);
			_picture = new ImageView(context);
			_title.SetTextSize(ComplexUnitType.Sp, 16);
			_subtitle.SetTextSize(ComplexUnitType.Sp, 14);
			var image = RowMetrics.ImageBlock(context.Resources?.DisplayMetrics?.Density ?? 1f);
			AddView(_title, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
			AddView(_subtitle, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
			AddView(_picture, new LayoutParams(ViewGroup.LayoutParams.MatchParent, image));
		}

		public void Show(RowFace face)
		{
			_title.Text = face.Title;
			_title.ContentDescription = face.Title;
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

			var id = Context?.Resources?.GetIdentifier(face.Picture, "drawable", Context.PackageName) ?? 0;
			if (id == 0)
				throw new InvalidOperationException("Drawable " + face.Picture + " was not found.");
			_picture.SetImageResource(id);
			_picture.ContentDescription = face.Picture;
			_picture.Visibility = ViewStates.Visible;
		}
	}
}
#endif
