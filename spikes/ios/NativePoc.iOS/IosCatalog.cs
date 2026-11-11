namespace NativePoc;

public static class IosCatalog
{
	public static string ScreenId(byte[] program) => RecordProgram.ScreenId(program);

	public static void Install(byte[] root, params byte[][] rest)
	{
		foreach (var program in rest)
			ScreenNav.Shared.Register(program);
		ScreenNav.Shared.ShowRoot(root);
	}
}
