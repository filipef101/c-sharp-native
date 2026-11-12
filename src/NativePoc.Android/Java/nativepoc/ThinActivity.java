package nativepoc;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;
import android.widget.TextView;

/* A handwritten activity in the same process and the same .so as the
   generated crc64 activity. Its static init calls one JNI method and does
   not register a managed peer. */
public final class ThinActivity extends Activity {
	static native void thinMark();

	static {
		StartupProbe.mark("thin-class");
		thinMark();
	}

	public ThinActivity() {
		super();
		StartupProbe.mark("thin-ctor");
	}

	@Override
	protected void onCreate(Bundle saved) {
		StartupProbe.mark("thin-oncreate");
		super.onCreate(saved);
		TextView view = new TextView(this);
		view.setText("thin");
		setContentView(view);
		/* Flushes prior_p50. The number is not a startup sample. */
		Log.i("NativePoc", "startup mark=activity since-process-ms=0 thin=1");
	}
}
