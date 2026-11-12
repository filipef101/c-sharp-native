package nativepoc;

/* One JNI entry for the activity's overrides. The generated static block
   calls this instead of ManagedPeer.registerNativeMembers when
   NativePocFastNatives is set. The managed side already holds the
   function pointers, so ART does not parse __md_methods. */
public final class ActivityNatives {
	private ActivityNatives() {
	}

	public static native void register(Class<?> type);
}
