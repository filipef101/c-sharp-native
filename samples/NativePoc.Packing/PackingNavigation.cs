namespace NativePoc.Packing;

public enum SystemBack
{
	ReturnToList = 1,
	FinishActivity = 2,
}

public static class PackingNavigation
{
	public static SystemBack SystemBackFor(PackingRoute route)
	{
		switch (route)
		{
			case PackingRoute.Editor:
				return SystemBack.ReturnToList;
			case PackingRoute.List:
				return SystemBack.FinishActivity;
			default:
				return Unknown(route);
		}
	}

	static SystemBack Unknown(PackingRoute route) =>
		throw new InvalidOperationException("Unknown packing route " + route + ".");
}
