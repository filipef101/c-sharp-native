namespace NativePoc;

// The managed image read calls this when its screen generation is cancelled.
// Cancelling the token aborts HttpClient.Send. Disposing the stream and the
// response aborts a Read that is already blocked on the socket.
internal static class ResponseAbort
{
	public static void Stop(CancellationTokenSource? cancel, Stream? stream, HttpResponseMessage? response)
	{
		try
		{
			cancel?.Cancel();
		}
		catch (Exception)
		{
		}

		try
		{
			stream?.Dispose();
		}
		catch (Exception)
		{
		}

		try
		{
			response?.Dispose();
		}
		catch (Exception)
		{
		}
	}
}
