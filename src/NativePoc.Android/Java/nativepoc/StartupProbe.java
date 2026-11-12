package nativepoc;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.Process;
import android.os.SystemClock;
import android.system.ErrnoException;
import android.system.Os;
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
		long minflt = -1;
		long majflt = -1;
		try {
			java.io.RandomAccessFile stat = new java.io.RandomAccessFile("/proc/self/stat", "r");
			String line = stat.readLine();
			stat.close();
			if (line != null) {
				int paren = line.lastIndexOf(')');
				if (paren >= 0 && paren + 2 < line.length()) {
					String[] parts = line.substring(paren + 2).split(" ");
					if (parts.length > 9) {
						minflt = Long.parseLong(parts[7]);
						majflt = Long.parseLong(parts[9]);
					}
				}
			}
		} catch (Exception ignored) {
			minflt = -1;
			majflt = -1;
		}
		Log.i("NativePoc", "startup clock=elapsed phase=" + phase + " t=" + now + " since-process-ms=" + since
			+ " minflt=" + minflt + " majflt=" + majflt);
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
			try {
				Os.setenv("NATIVEPOC_START_ELAPSED", Long.toString(Process.getStartElapsedRealtime()), true);
			} catch (ErrnoException ignored) {
			}
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
