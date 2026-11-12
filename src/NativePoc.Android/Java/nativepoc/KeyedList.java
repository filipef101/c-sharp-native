package nativepoc;

import android.content.Context;
import android.os.Process;
import android.os.SystemClock;
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
	static final int OP_READY = 8;

	long peer;
	final Rows rows = new Rows();
	boolean bindLog;
	final long[] bindUs = new long[512];
	int bindSeen;
	int bindKept;
	boolean loggedMeasure;
	boolean loggedLayout;
	boolean loggedReady;

	public KeyedList(Context context) {
		super(context);
		setLayoutManager(new LinearLayoutManager(context));
		setHasFixedSize(true);
		setItemViewCacheSize(4);
		setNestedScrollingEnabled(true);
		setContentDescription("List");
		setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
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
		if (!loggedMeasure) {
			loggedMeasure = true;
			long now = SystemClock.elapsedRealtime();
			android.util.Log.i("NativePoc", "startup clock=elapsed phase=measure t=" + now
				+ " since-process-ms=" + (now - Process.getStartElapsedRealtime())
				+ " took-ms=" + (now - start));
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
			return new Cell(getContext());
		}

		@Override
		public void onBindViewHolder(Cell holder, int position) {
			if (!bindLog) {
				nativeBind(peer, position, holder.title, holder.subtitle, holder.picture);
				return;
			}
			long start = System.nanoTime();
			nativeBind(peer, position, holder.title, holder.subtitle, holder.picture);
			noteBind(System.nanoTime() - start);
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
