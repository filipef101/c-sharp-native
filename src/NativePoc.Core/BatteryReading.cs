namespace NativePoc;

internal readonly record struct BatteryReading
{
	public bool Known { get; init; }

	public int Percent { get; init; }

	public string Text => Known
		? Percent.ToString(System.Globalization.CultureInfo.InvariantCulture)
		: "unknown";

	public static BatteryReading FromCapacity(int raw) =>
		raw is >= 0 and <= 100
			? new BatteryReading { Known = true, Percent = raw }
			: new BatteryReading { Known = false, Percent = -1 };
}
