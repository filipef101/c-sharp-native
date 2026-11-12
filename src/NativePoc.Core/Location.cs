using System.Globalization;

namespace NativePoc;

public enum LocationAccess
{
	Granted,
	Denied,
	PermanentlyDenied,
}

public readonly record struct LocationFix(LocationAccess Access, double Latitude, double Longitude, float AccuracyMeters);

public static class LocationAccessRules
{
	public static LocationAccess Classify(bool granted, bool shouldShowRationale, bool asked)
	{
		if (granted)
			return LocationAccess.Granted;
		if (!asked || shouldShowRationale)
			return LocationAccess.Denied;
		return LocationAccess.PermanentlyDenied;
	}
}

public static class Location
{
	public static Task<string> Read(IPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return platform.ModuleAsync("location", "Read", []);
	}

	public static string Format(LocationFix fix) => fix.Access switch
	{
		LocationAccess.Granted => string.Create(CultureInfo.InvariantCulture, $"granted {fix.Latitude} {fix.Longitude} {fix.AccuracyMeters}"),
		LocationAccess.Denied => "denied",
		LocationAccess.PermanentlyDenied => "permanently-denied",
		_ => throw new InvalidOperationException("Rejected location result."),
	};

	public static LocationFix Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text == "denied")
			return new LocationFix(LocationAccess.Denied, 0, 0, 0);
		if (text == "permanently-denied")
			return new LocationFix(LocationAccess.PermanentlyDenied, 0, 0, 0);
		if (text.StartsWith("granted ", StringComparison.Ordinal))
		{
			var parts = text.Split(' ');
			if (parts.Length == 4
				&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
				&& double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
				&& float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var accuracy))
				return new LocationFix(LocationAccess.Granted, latitude, longitude, accuracy);
		}

		throw new InvalidOperationException("Rejected location result.");
	}
}
