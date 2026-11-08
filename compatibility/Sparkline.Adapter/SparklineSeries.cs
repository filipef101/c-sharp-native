namespace Sparkline.Adapter;

public sealed class SparklineSeries
{
	readonly float[] _values;

	SparklineSeries(float[] values)
	{
		_values = values;
		var min = values[0];
		var max = values[0];
		for (var i = 1; i < values.Length; i++)
		{
			if (values[i] < min)
				min = values[i];
			if (values[i] > max)
				max = values[i];
		}
		Min = min;
		Max = max;
	}

	public IReadOnlyList<float> Values => _values;

	public float Min { get; }

	public float Max { get; }

	public static SparklineSeries FromValues(IReadOnlyList<float> values)
	{
		if (values.Count == 0)
			throw new ArgumentException("A sparkline needs at least one value.", nameof(values));
		var copy = new float[values.Count];
		for (var i = 0; i < values.Count; i++)
			copy[i] = values[i];
		return new SparklineSeries(copy);
	}
}
