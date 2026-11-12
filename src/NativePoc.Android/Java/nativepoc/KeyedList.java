package nativepoc;

import android.content.Context;
import android.os.Process;
import android.os.SystemClock;
import android.util.DisplayMetrics;
import android.view.MotionEvent;
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
	static final int OP_READY = 8;
	static final int OP_SWIPE = 16;
	static final int OP_REFRESH = 32;

	long peer;
	boolean refresh;
	final float density;
	final Rows rows = new Rows();
	boolean bindLog;
	final long[] bindUs = new long[512];
	int bindSeen;
	int bindKept;
	boolean loggedMeasure;
	boolean loggedAgain;
	boolean loggedLayout;
	boolean loggedReady;
	boolean rowFitPosted;
	int measures;
	boolean dismissLogged;
	static final int ACTION_DISMISS = 0x0000c0de;

	public KeyedList(Context context) {
		super(context);
		LinearLayoutManager manager = new LinearLayoutManager(context);
		manager.setInitialPrefetchItemCount(0);
		setLayoutManager(manager);
		setHasFixedSize(true);
		setItemViewCacheSize(4);
		setNestedScrollingEnabled(true);
		setContentDescription("List");
		setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
		DisplayMetrics metrics = context.getResources().getDisplayMetrics();
		float found = metrics == null ? 1f : metrics.density;
		density = found <= 0 ? 1f : found;
		addOnItemTouchListener(new RowGestures());
		addOnScrollListener(new OnScrollListener() {
			@Override
			public void onScrollStateChanged(RecyclerView recyclerView, int state) {
				if (state != SCROLL_STATE_IDLE || peer == 0)
					return;
				logBinds();
				nativeSignal(peer, OP_IDLE, first());
			}
		});
	}

	public void setBindLog(boolean on) {
		bindLog = on;
	}

	public void setRefresh(boolean on) {
		refresh = on;
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
		long start = SystemClock.elapsedRealtime();
		if (View.MeasureSpec.getMode(heightMeasureSpec) == View.MeasureSpec.UNSPECIFIED) {
			DisplayMetrics metrics = getResources().getDisplayMetrics();
			int screen = metrics == null ? 0 : metrics.heightPixels;
			if (screen > 0)
				heightMeasureSpec = View.MeasureSpec.makeMeasureSpec(screen, View.MeasureSpec.AT_MOST);
		}
		super.onMeasure(widthMeasureSpec, heightMeasureSpec);
		measures++;
		long now = SystemClock.elapsedRealtime();
		if (!loggedMeasure) {
			loggedMeasure = true;
			android.util.Log.i("NativePoc", "startup clock=elapsed phase=measure t=" + now
				+ " since-process-ms=" + (now - Process.getStartElapsedRealtime())
				+ " took-ms=" + (now - start)
				+ " n=" + measures);
		} else if (!loggedAgain) {
			loggedAgain = true;
			android.util.Log.i("NativePoc", "startup clock=elapsed phase=measure-again t=" + now
				+ " since-process-ms=" + (now - Process.getStartElapsedRealtime())
				+ " took-ms=" + (now - start)
				+ " n=" + measures);
		}
	}

	@Override
	protected void onLayout(boolean changed, int l, int t, int r, int b) {
		long start = SystemClock.elapsedRealtime();
		super.onLayout(changed, l, t, r, b);
		long now = SystemClock.elapsedRealtime();
		if (!loggedLayout) {
			loggedLayout = true;
			android.util.Log.i("NativePoc", "startup clock=elapsed phase=layout t=" + now
				+ " since-process-ms=" + (now - Process.getStartElapsedRealtime())
				+ " took-ms=" + (now - start)
				+ " children=" + getChildCount());
		}
		if (!loggedReady && getChildCount() > 0 && peer != 0) {
			loggedReady = true;
			nativeSignal(peer, OP_READY, first());
		}
		if (!rowFitPosted && getChildCount() > 0) {
			rowFitPosted = true;
			post(this::fitWholeRows);
		}
	}

	void fitWholeRows() {
		int height = getHeight() - getPaddingTop() - getPaddingBottom();
		if (height <= 0 || getChildCount() == 0)
			return;
		int row = getChildAt(0).getHeight();
		if (row <= 0)
			return;
		int slack = height % row;
		if (slack == 0)
			return;
		setPadding(getPaddingLeft(), getPaddingTop(), getPaddingRight(), getPaddingBottom() + slack);
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

	void noteBind(long nanos) {
		bindSeen++;
		if (bindKept < bindUs.length)
			bindUs[bindKept++] = nanos / 1000L;
	}

	void logBinds() {
		if (!bindLog || bindKept == 0)
			return;
		long[] copy = new long[bindKept];
		System.arraycopy(bindUs, 0, copy, 0, bindKept);
		java.util.Arrays.sort(copy);
		android.util.Log.i("NativePoc", "bind-jni n=" + bindSeen
			+ " kept=" + bindKept
			+ " p50=" + percentile(copy, 0.50)
			+ " p90=" + percentile(copy, 0.90)
			+ " max=" + copy[copy.length - 1]
			+ " unit=us");
	}

	static long percentile(long[] sorted, double p) {
		int n = sorted.length;
		double h = 1 + (n - 1) * p;
		int lo = (int) Math.floor(h);
		double frac = h - lo;
		int hi = frac > 0 ? lo + 1 : lo;
		if (lo < 1)
			lo = 1;
		if (hi > n)
			hi = n;
		if (lo > n)
			lo = n;
		return Math.round(sorted[lo - 1] + frac * (sorted[hi - 1] - sorted[lo - 1]));
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
			Cell cell = new Cell(getContext());
			cell.itemView.setFocusable(true);
			cell.itemView.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
			if (android.os.Build.VERSION.SDK_INT >= 28)
				cell.itemView.setScreenReaderFocusable(true);
			cell.title.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
			cell.subtitle.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
			cell.picture.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
			cell.itemView.setAccessibilityDelegate(new RowAccess(cell));
			cell.itemView.setOnFocusChangeListener(new View.OnFocusChangeListener() {
				@Override
				public void onFocusChange(View view, boolean hasFocus) {
					if (hasFocus)
						requestChildRectangleOnScreen(view, new android.graphics.Rect(0, 0, view.getWidth(), view.getHeight()), false);
				}
			});
			return cell;
		}

		@Override
		public void onBindViewHolder(Cell holder, int position) {
			holder.itemView.setTranslationX(0);
			if (!bindLog) {
				nativeBind(peer, position, holder.title, holder.subtitle, holder.picture);
				showRow(holder);
				return;
			}
			long start = System.nanoTime();
			nativeBind(peer, position, holder.title, holder.subtitle, holder.picture);
			noteBind(System.nanoTime() - start);
			showRow(holder);
		}

		void showRow(Cell holder) {
			CharSequence text = holder.title.getText();
			holder.itemView.setContentDescription(text == null ? "" : text);
		}
	}

	final class RowAccess extends View.AccessibilityDelegate {
		final Cell cell;

		RowAccess(Cell cell) {
			this.cell = cell;
		}

		@Override
		public void onInitializeAccessibilityNodeInfo(View host, AccessibilityNodeInfo info) {
			super.onInitializeAccessibilityNodeInfo(host, info);
			int pos = cell.getBindingAdapterPosition();
			if (pos == NO_POSITION)
				pos = 0;
			info.setCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.obtain(pos, 1, 0, 1, false));
			info.addAction(new AccessibilityNodeInfo.AccessibilityAction(ACTION_DISMISS, "Dismiss"));
			if (!dismissLogged) {
				dismissLogged = true;
				android.util.Log.i("NativePoc", "a11y-dismiss action=Dismiss");
			}
		}

		@Override
		public boolean performAccessibilityAction(View host, int action, android.os.Bundle args) {
			if (action == ACTION_DISMISS) {
				int pos = cell.getBindingAdapterPosition();
				android.util.Log.i("NativePoc", "a11y-dismiss pos=" + pos);
				if (peer != 0 && pos != NO_POSITION)
					nativeSignal(peer, OP_SWIPE, pos);
				return true;
			}
			return super.performAccessibilityAction(host, action, args);
		}
	}

	final class RowGestures implements OnItemTouchListener {
		float downX;
		float downY;
		int claim;
		View child;
		int position = NO_POSITION;

		@Override
		public boolean onInterceptTouchEvent(RecyclerView recycler, MotionEvent event) {
			int action = event.getActionMasked();
			if (action == MotionEvent.ACTION_DOWN) {
				downX = event.getX();
				downY = event.getY();
				claim = 0;
				child = findChildViewUnder(event.getX(), event.getY());
				position = child == null ? NO_POSITION : getChildAdapterPosition(child);
				return false;
			}
			if (action == MotionEvent.ACTION_MOVE && claim == 0) {
				float dx = event.getX() - downX;
				float dy = event.getY() - downY;
				float slop = 48f * density;
				if (position != NO_POSITION && Math.abs(dx) > slop && Math.abs(dx) > Math.abs(dy) * 1.5f) {
					claim = 1;
					return true;
				}
				if (refresh && !canScrollVertically(-1) && dy > 64f * density && dy > Math.abs(dx) * 1.5f) {
					claim = 2;
					return true;
				}
				return false;
			}
			if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL)
				release();
			return false;
		}

		@Override
		public void onTouchEvent(RecyclerView recycler, MotionEvent event) {
			if (claim == 0)
				return;
			int action = event.getActionMasked();
			float dx = event.getX() - downX;
			float dy = event.getY() - downY;
			if (claim == 1 && child != null)
				child.setTranslationX(dx);
			if (action != MotionEvent.ACTION_UP && action != MotionEvent.ACTION_CANCEL)
				return;
			if (action == MotionEvent.ACTION_UP && peer != 0) {
				if (claim == 1 && position != NO_POSITION && Math.abs(dx) > 48f * density)
					nativeSignal(peer, OP_SWIPE, position);
				else if (claim == 2 && dy > 64f * density)
					nativeSignal(peer, OP_REFRESH, 0);
			}
			release();
		}

		@Override
		public void onRequestDisallowInterceptTouchEvent(boolean disallow) {
			if (disallow)
				release();
		}

		void release() {
			if (child != null)
				child.setTranslationX(0);
			claim = 0;
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
			subtitle.setVisibility(View.GONE);
			picture.setScaleType(ImageView.ScaleType.CENTER_CROP);
			picture.setBackgroundColor(0xFFE6E6E6);
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
