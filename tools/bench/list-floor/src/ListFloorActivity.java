package com.scratch.listfloor;

import android.app.Activity;
import android.graphics.drawable.ColorDrawable;
import android.os.Bundle;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewTreeObserver;
import android.view.accessibility.AccessibilityNodeInfo;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

/** Java RecyclerView with the same row shape as the C# feed. No C# and no Kotlin. */
public final class ListFloorActivity extends Activity {
	static final String Tag = "Floor";
	static final int Rows = 1000;
	static final int Placeholder = 0xFFE6E6E6;
	boolean logged;
	boolean drawLogged;
	long process;

	static void phase(String name, long process) {
		long now = SystemClock.elapsedRealtime();
		long since = now - process;
		if (since < 0)
			since = 0;
		Log.i(Tag, "startup clock=elapsed phase=" + name + " t=" + now + " since-process-ms=" + since);
	}

	@Override
	protected void onCreate(Bundle saved) {
		process = android.os.Process.getStartElapsedRealtime();
		long now = SystemClock.elapsedRealtime();
		long since = now - process;
		if (since < 0)
			since = 0;
		Log.i(Tag, "startup mark=activity since-process-ms=" + since);
		Log.i(Tag, "startup clock=elapsed phase=process t=" + process + " since-process-ms=0");
		phase("runtime", process);
		phase("onCreate", process);
		super.onCreate(saved);
		RecyclerView list = new RecyclerView(this);
		list.setLayoutManager(new LinearLayoutManager(this));
		list.setHasFixedSize(true);
		list.setContentDescription("List");
		list.setAdapter(new RowsAdapter());
		list.setAccessibilityDelegate(new View.AccessibilityDelegate() {
			@Override
			public void onInitializeAccessibilityNodeInfo(View host, AccessibilityNodeInfo info) {
				super.onInitializeAccessibilityNodeInfo(host, info);
				info.setClassName(RecyclerView.class.getName());
			}
		});
		setContentView(list);
		phase("render", process);
		list.getViewTreeObserver().addOnPreDrawListener(new ViewTreeObserver.OnPreDrawListener() {
			@Override
			public boolean onPreDraw() {
				if (drawLogged)
					return true;
				drawLogged = true;
				ViewTreeObserver observer = list.getViewTreeObserver();
				if (observer.isAlive())
					observer.removeOnPreDrawListener(this);
				phase("draw", process);
				Looper.myQueue().addIdleHandler(() -> {
					phase("idle", process);
					return false;
				});
				return true;
			}
		});
		list.getViewTreeObserver().addOnGlobalLayoutListener(() -> {
			if (logged)
				return;
			LinearLayoutManager manager = (LinearLayoutManager) list.getLayoutManager();
			int first = manager == null ? 0 : manager.findFirstVisibleItemPosition();
			if (first < 0 || list.getChildCount() <= 0)
				return;
			logged = true;
			long layout = SystemClock.elapsedRealtime();
			long layoutSince = layout - process;
			if (layoutSince < 0)
				layoutSince = 0;
			Log.i(Tag, "startup clock=elapsed phase=layout t=" + layout + " since-process-ms=" + layoutSince);
			Log.i(Tag, "list-first=" + first
				+ " rows=" + Rows
				+ " views=" + list.getChildCount()
				+ " since-process-ms=" + layoutSince);
		});
	}

	static final class RowsAdapter extends RecyclerView.Adapter<Cell> {
		RowsAdapter() {
			setHasStableIds(true);
		}

		@Override
		public int getItemCount() {
			return Rows;
		}

		@Override
		public long getItemId(int position) {
			return position;
		}

		@Override
		public Cell onCreateViewHolder(ViewGroup parent, int viewType) {
			return new Cell(parent);
		}

		@Override
		public void onBindViewHolder(Cell holder, int position) {
			String label = String.format(java.util.Locale.US, "Row %04d", position);
			holder.title.setText("\u2022 " + label);
			holder.subtitle.setText("");
			holder.subtitle.setVisibility(View.GONE);
			holder.picture.setImageDrawable(new ColorDrawable(Placeholder));
			holder.picture.setContentDescription("Loading image");
		}
	}

	static final class Cell extends RecyclerView.ViewHolder {
		final TextView title;
		final TextView subtitle;
		final ImageView picture;

		Cell(ViewGroup parent) {
			super(column(parent));
			LinearLayout row = (LinearLayout) itemView;
			title = (TextView) row.getChildAt(0);
			subtitle = (TextView) row.getChildAt(1);
			picture = (ImageView) row.getChildAt(2);
		}

		static LinearLayout column(ViewGroup parent) {
			android.content.Context context = parent.getContext();
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
