package nativepoc;

import android.content.Context;
import android.util.DisplayMetrics;
import android.view.View;
import android.view.ViewGroup;
import android.view.accessibility.AccessibilityNodeInfo;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

public final class KeyedList extends RecyclerView {
	static final int OP_COUNT = 1;
	static final int OP_ID = 2;
	static final int OP_IDLE = 4;

	long peer;
	final Rows rows = new Rows();

	public KeyedList(Context context) {
		super(context);
		setLayoutManager(new LinearLayoutManager(context));
		setItemViewCacheSize(8);
		setNestedScrollingEnabled(true);
		setContentDescription("List");
		setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
		addOnScrollListener(new OnScrollListener() {
			@Override
			public void onScrollStateChanged(RecyclerView recyclerView, int state) {
				if (state == SCROLL_STATE_IDLE && peer != 0)
					nativeSignal(peer, OP_IDLE, first());
			}
		});
	}

	public void setPeer(long peer) {
		this.peer = peer;
		rows.setHasStableIds(true);
		setAdapter(rows);
	}

	public int first() {
		LayoutManager manager = getLayoutManager();
		if (manager instanceof LinearLayoutManager) {
			int index = ((LinearLayoutManager) manager).findFirstVisibleItemPosition();
			return index < 0 ? 0 : index;
		}
		return 0;
	}

	public void replace() {
		RecyclerView.Adapter<?> adapter = getAdapter();
		if (adapter != null)
			adapter.notifyDataSetChanged();
	}

	public void scrollTo(int index) {
		scrollToPosition(index);
	}

	@Override
	protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
		if (View.MeasureSpec.getMode(heightMeasureSpec) == View.MeasureSpec.UNSPECIFIED) {
			DisplayMetrics metrics = getResources().getDisplayMetrics();
			int screen = metrics == null ? 0 : metrics.heightPixels;
			if (screen > 0)
				heightMeasureSpec = View.MeasureSpec.makeMeasureSpec(screen, View.MeasureSpec.AT_MOST);
		}
		super.onMeasure(widthMeasureSpec, heightMeasureSpec);
	}

	@Override
	public void onInitializeAccessibilityNodeInfo(AccessibilityNodeInfo info) {
		super.onInitializeAccessibilityNodeInfo(info);
		int count = peer == 0 ? 0 : (int) nativeSignal(peer, OP_COUNT, 0);
		info.setClassName("androidx.recyclerview.widget.RecyclerView");
		info.setCollectionInfo(AccessibilityNodeInfo.CollectionInfo.obtain(count, 1, false));
		info.setScrollable(true);
		info.setContentDescription("List");
	}

	static native long nativeSignal(long peer, int op, int arg);

	static native void nativeBind(long peer, int position, TextView title, TextView subtitle, ImageView image);

	final class Rows extends RecyclerView.Adapter<Cell> {
		@Override
		public int getItemCount() {
			if (peer == 0)
				return 0;
			return (int) nativeSignal(peer, OP_COUNT, 0);
		}

		@Override
		public long getItemId(int position) {
			return nativeSignal(peer, OP_ID, position);
		}

		@Override
		public Cell onCreateViewHolder(ViewGroup parent, int viewType) {
			return new Cell(getContext());
		}

		@Override
		public void onBindViewHolder(Cell holder, int position) {
			nativeBind(peer, position, holder.title, holder.subtitle, holder.picture);
		}
	}

	static final class Cell extends RecyclerView.ViewHolder {
		final TextView title;
		final TextView subtitle;
		final ImageView picture;

		Cell(Context context) {
			super(column(context));
			LinearLayout row = (LinearLayout) itemView;
			title = (TextView) row.getChildAt(0);
			subtitle = (TextView) row.getChildAt(1);
			picture = (ImageView) row.getChildAt(2);
		}

		static LinearLayout column(Context context) {
			LinearLayout row = new LinearLayout(context);
			row.setOrientation(LinearLayout.VERTICAL);
			int match = ViewGroup.LayoutParams.MATCH_PARENT;
			int wrap = ViewGroup.LayoutParams.WRAP_CONTENT;
			row.setLayoutParams(new RecyclerView.LayoutParams(match, wrap));
			TextView title = new TextView(context);
			TextView subtitle = new TextView(context);
			ImageView picture = new ImageView(context);
			title.setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 16);
			subtitle.setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 14);
			picture.setScaleType(ImageView.ScaleType.CENTER_CROP);
			float density = context.getResources().getDisplayMetrics().density;
			if (density <= 0)
				density = 1f;
			int image = (int) Math.ceil(48 * density);
			row.addView(title, new LinearLayout.LayoutParams(match, wrap));
			row.addView(subtitle, new LinearLayout.LayoutParams(match, wrap));
			row.addView(picture, new LinearLayout.LayoutParams(match, image));
			return row;
		}
	}
}
