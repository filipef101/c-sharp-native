namespace NativePoc;

sealed class PlatformBridge : IPlatform
{
	readonly ScreenController _controller;

	public PlatformBridge(ScreenController controller)
	{
		_controller = controller;
	}

	public void CopyNonce(string nonce)
	{
		var pasteboard = UIPasteboard.General;
		pasteboard.String = nonce;
		var read = pasteboard.String;
		if (!string.Equals(read, nonce, StringComparison.Ordinal))
			throw new InvalidOperationException("Clipboard round-trip failed.");
		Console.WriteLine("NativePoc clipboard-ok " + nonce);
	}

	public void RequestRender() => _controller.RequestRender();
}
