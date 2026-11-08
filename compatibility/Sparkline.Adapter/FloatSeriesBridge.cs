using Com.Robinhood.Spark;
using Java.Lang;

namespace Sparkline.Adapter;

public sealed class FloatSeriesBridge : SparkAdapter
{
	readonly float[] _values;

	public FloatSeriesBridge(SparklineSeries series)
	{
		_values = series.Values.ToArray();
	}

	public override int Count => _values.Length;

	public override Java.Lang.Object GetItem(int index)
	{
		return Integer.ValueOf(index);
	}

	public override float GetY(int index)
	{
		return _values[index];
	}
}
