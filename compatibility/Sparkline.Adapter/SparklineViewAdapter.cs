using Android.Content;
using Android.Util;
using Android.Views;
using Com.Robinhood.Spark;
using Java.Lang;

namespace Sparkline.Adapter;

public sealed class SparklineViewAdapter : IDisposable
{
	public const string LogTag = "SparkProbe";

	readonly SparkView _view;
	readonly FloatSeriesBridge _points;
	readonly ScrubBridge _scrub;
	readonly SparklineSeries _series;
	int _lastIndex = -1;
	bool _lastEnded;
	bool _unsubscribed;
	bool _disposed;

	public SparklineViewAdapter(Context context, SparklineSeries series, int instanceId)
	{
		_series = series;
		InstanceId = instanceId;
		_points = new FloatSeriesBridge(series);
		_scrub = new ScrubBridge(this);
		var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		_view = new SparkView(context)
		{
			LineColor = unchecked((int)0xFF111111),
			LineWidth = 3f * density,
			FillType = 0,
			ScrubEnabled = true,
			ScrubLineColor = unchecked((int)0xFFB45309),
			ScrubLineWidth = 2f * density,
			ContentDescription = "sparkline",
			Adapter = _points,
			ScrubListener = _scrub,
		};
		Log.Info(LogTag, "listener-set id=" + instanceId + " once=true");
	}

	public int InstanceId { get; }

	public SparklineSeries Series => _series;

	public View View => _view;

	public event Action<SparklineScrub>? Scrubbed;

	public void Receive(Java.Lang.Object? value)
	{
		if (_unsubscribed)
		{
			Log.Info(LogTag, "raw-after-unsubscribe id=" + InstanceId);
			return;
		}
		if (value is null)
		{
			Log.Info(LogTag, "raw ended id=" + InstanceId);
			if (_lastEnded)
			{
				Log.Info(LogTag, "suppress-duplicate ended id=" + InstanceId);
				return;
			}
			_lastEnded = true;
			_lastIndex = -1;
			Scrubbed?.Invoke(new SparklineScrub(-1, 0f, true));
			return;
		}
		if (value is not Integer boxed)
		{
			Log.Info(LogTag, "raw-unparsed id=" + InstanceId + " type=" + value.GetType().FullName);
			return;
		}
		var index = boxed.IntValue();
		Log.Info(LogTag, "raw index=" + index + " id=" + InstanceId);
		if (index == _lastIndex)
		{
			Log.Info(LogTag, "suppress-duplicate index=" + index + " id=" + InstanceId);
			return;
		}
		_lastEnded = false;
		_lastIndex = index;
		var point = _series.Values[index];
		Scrubbed?.Invoke(new SparklineScrub(index, point, false));
	}

	public void Unsubscribe()
	{
		if (_unsubscribed)
			return;
		_unsubscribed = true;
		_view.ScrubListener = null;
		var cleared = _view.ScrubListener is null;
		Log.Info(LogTag, "unsubscribe id=" + InstanceId + " listener-null=" + cleared);
		_scrub.Detach();
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		Unsubscribe();
		_disposed = true;
		Scrubbed = null;
		_points.Dispose();
		_scrub.Dispose();
		_view.Dispose();
		Log.Info(LogTag, "dispose id=" + InstanceId);
	}
}
