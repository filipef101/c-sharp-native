namespace NativePoc;

[ReloadCallable]
public static class CounterService
{
	public static int Next(int count) => count + 5;
}
