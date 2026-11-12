using Android.Util;
using Android.Widget;

namespace NativePoc;

[JniType("android/widget/Toast")]
static partial class DeviceToast
{
	[JniMethod("makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;")]
	public static void Show(string text)
	{
		var activity = MainActivity.Current ?? throw new InvalidOperationException("jni-bind no-activity");
		Toast.MakeText(activity, text, ToastLength.Short)?.Show();
		Log.Info("NativePoc", "jni-bind class=android/widget/Toast method=makeText text=" + text);
	}
}
