package nativepoc;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.Process;
import android.os.SystemClock;
import android.util.Log;

/** Timestamps on SystemClock.elapsedRealtime around NativeAotRuntimeProvider. */
public final class StartupProbe {
	private StartupProbe() {
	}

	static void mark(String phase) {
		long process = Process.getStartElapsedRealtime();
		long now = SystemClock.elapsedRealtime();
		long since = now - process;
		if (since < 0)
			since = 0;
		Log.i("NativePoc", "startup clock=elapsed phase=" + phase + " t=" + now + " since-process-ms=" + since);
	}

	abstract static class Stub extends ContentProvider {
		@Override
		public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
			return null;
		}

		@Override
		public String getType(Uri uri) {
			return null;
		}

		@Override
		public Uri insert(Uri uri, ContentValues values) {
			return null;
		}

		@Override
		public int delete(Uri uri, String selection, String[] selectionArgs) {
			return 0;
		}

		@Override
		public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
			return 0;
		}
	}

	/** initOrder above NativeAotRuntimeProvider, so this runs before System.loadLibrary. */
	public static final class Before extends Stub {
		static {
			mark("java-class");
		}

		@Override
		public boolean onCreate() {
			mark("before-runtime");
			return true;
		}
	}

	/** initOrder below NativeAotRuntimeProvider, so this runs after JNI_OnLoad and managed startup. */
	public static final class After extends Stub {
		@Override
		public boolean onCreate() {
			mark("after-runtime");
			return true;
		}
	}
}
