using Android.Util;
using Com.Robinhood.Spark;

namespace Sparkline.Adapter;

public sealed class ScrubBridge : Java.Lang.Object, SparkView.IOnScrubListener
{
	SparklineViewAdapter? _owner;

	public ScrubBridge(SparklineViewAdapter owner)
	{
		_owner = owner;
	}

	public void Detach()
	{
		_owner = null;
	}

	public void OnScrubbed(Java.Lang.Object? value)
	{
		var owner = _owner;
		if (owner is null)
		{
			Log.Info(SparklineViewAdapter.LogTag, "raw-after-unsubscribe");
			return;
		}
		owner.Receive(value);
	}
}
