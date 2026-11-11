package com.scratch.floor;

import android.app.Activity;
import android.os.Bundle;
import android.os.SystemClock;
import android.util.Log;
import android.view.View;
import android.view.ViewTreeObserver;
import android.widget.TextView;

public final class FloorActivity extends Activity {
    static final String TAG = "Floor";

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        final long process = android.os.Process.getStartElapsedRealtime();
        final long activity = SystemClock.elapsedRealtime() - process;
        Log.i(TAG, "startup mark=activity since-process-ms=" + activity);
        TextView text = new TextView(this);
        text.setText("floor");
        setContentView(text);
        final long render = SystemClock.elapsedRealtime() - process;
        Log.i(TAG, "startup mark=first-render since-process-ms=" + render);
        final View decor = getWindow().getDecorView();
        decor.getViewTreeObserver().addOnPreDrawListener(new ViewTreeObserver.OnPreDrawListener() {
            boolean done;

            @Override
            public boolean onPreDraw() {
                if (done) {
                    return true;
                }
                done = true;
                ViewTreeObserver observer = decor.getViewTreeObserver();
                if (observer.isAlive()) {
                    observer.removeOnPreDrawListener(this);
                }
                long present = SystemClock.elapsedRealtime() - process;
                Log.i(TAG, "startup mark=first-present since-process-ms=" + present);
                return true;
            }
        });
        long returned = SystemClock.elapsedRealtime() - process;
        Log.i(TAG, "startup mark=activity-return since-process-ms=" + returned);
    }
}
