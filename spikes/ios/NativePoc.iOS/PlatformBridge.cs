namespace NativePoc;

sealed class IosPlatform : IPlatform
{
	public void CopyNonce(string nonce) => Module("clipboard", "Copy", ["nonce", nonce]);

	public void RequestRender() => ScreenController.Current?.RequestRender();

	public void Push(string screen)
	{
		if (!ScreenNav.Shared.Push(screen))
			Console.WriteLine("NativePoc nav-miss " + screen);
		RequestRender();
	}

	public void Pop()
	{
		ScreenNav.Shared.Pop();
		RequestRender();
	}

	public void Effect(string property, string resource) =>
		Console.WriteLine("NativePoc effect " + property + " " + resource);

	public void Every(string property) => Console.WriteLine("NativePoc every " + property);

	public void Module(string module, string method, string[] args)
	{
		switch (module, method)
		{
			case ("clipboard", "Copy"):
				UIPasteboard.General.String = args.Length == 0 ? "" : args[^1];
				Console.WriteLine("NativePoc clipboard.Copy");
				return;
			case ("haptics", "Confirm"):
				Console.WriteLine("NativePoc haptics.Confirm");
				return;
			case ("share", "Text"):
				Console.WriteLine("NativePoc share.Text chars=" + (args.Length == 0 ? 0 : args[0].Length));
				return;
			case ("storage", "Set"):
				Console.WriteLine("NativePoc storage.Set");
				return;
			case ("theme", "Mode"):
				Console.WriteLine("NativePoc theme.Mode");
				return;
			default:
				throw new InvalidOperationException("Unknown platform module " + module + "." + method + ".");
		}
	}
}
