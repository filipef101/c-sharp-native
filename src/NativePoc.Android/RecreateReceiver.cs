#if DEBUG
using Android.Content;

namespace NativePoc;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { "com.scratch.nativepoc.RECREATE" })]
public sealed class RecreateReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		MainActivity.Current?.Recreate();
	}
}
#endif
