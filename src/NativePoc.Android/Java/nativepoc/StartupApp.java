package nativepoc;

import android.app.Application;
import android.content.Context;

/** Marks around Application startup. Only packaged when startup marks are on. */
public final class StartupApp extends Application {
	@Override
	protected void attachBaseContext(Context base) {
		StartupProbe.mark("java-attach");
		super.attachBaseContext(base);
		StartupProbe.mark("java-attach-end");
	}

	@Override
	public void onCreate() {
		StartupProbe.mark("java-oncreate");
		super.onCreate();
		StartupProbe.mark("java-oncreate-end");
	}
}
