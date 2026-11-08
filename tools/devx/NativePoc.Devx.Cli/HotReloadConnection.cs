using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NativePoc.Devx;

enum HotReloadExchange
{
	OneShot,
}

readonly record struct ApplyResult(double Milliseconds, bool Ok, string Text);

// Socket name and handshake live here. The device still names the abstract socket
// npoc-<token> and closes the accepted socket after one exchange. The adb forward
// and this session stay up for the whole watch. A later contract that splits the
// socket name from the token, or that keeps the accepted socket open, changes this
// file and not the watch loop.
static class HotReloadHandshake
{
	public const int TokenBytes = 16;
	public const HotReloadExchange Exchange = HotReloadExchange.OneShot;

	public static string AbstractSocketName(string tokenHex) =>
		"npoc-" + tokenHex.Trim().ToLowerInvariant();

	public static byte[] Token(string tokenHex)
	{
		var token = Convert.FromHexString(tokenHex.Trim());
		if (token.Length != TokenBytes)
			throw new InvalidOperationException("The hot reload token is " + token.Length + " bytes.");
		return token;
	}

	public static byte[] Frame(byte[] token, byte[] dll)
	{
		if (token.Length != TokenBytes)
			throw new InvalidOperationException("The hot reload token is " + token.Length + " bytes.");
		var frame = new byte[TokenBytes + 4 + dll.Length];
		token.AsSpan().CopyTo(frame);
		var length = dll.Length;
		frame[TokenBytes] = (byte)length;
		frame[TokenBytes + 1] = (byte)(length >> 8);
		frame[TokenBytes + 2] = (byte)(length >> 16);
		frame[TokenBytes + 3] = (byte)(length >> 24);
		dll.AsSpan().CopyTo(frame.AsSpan(TokenBytes + 4));
		return frame;
	}

	public static (bool Ok, string Text) ReadReply(Stream stream)
	{
		var header = new byte[5];
		ReadExact(stream, header);
		var ok = header[0] == 1;
		var length = header[1] | (header[2] << 8) | (header[3] << 16) | (header[4] << 24);
		if (length < 0 || length > 1024 * 1024)
			throw new InvalidOperationException("Rejected reply length " + length + ".");
		var body = new byte[length];
		ReadExact(stream, body);
		return (ok, Encoding.UTF8.GetString(body));
	}

	static void ReadExact(Stream stream, byte[] buffer)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = stream.Read(buffer, offset, buffer.Length - offset);
			if (read == 0)
				throw new EndOfStreamException("The hot reload connection closed early.");
			offset += read;
		}
	}
}

sealed class HotReloadConnection : IDisposable
{
	public const int Port = 18765;

	readonly string _serial;
	readonly byte[] _token;
	bool _forwarded;
	bool _disposed;

	public HotReloadConnection(string serial, string tokenHex)
	{
		_serial = serial;
		_token = HotReloadHandshake.Token(tokenHex);
		SocketName = HotReloadHandshake.AbstractSocketName(tokenHex);
	}

	public string SocketName { get; }

	public void EnsureForward()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_forwarded)
			return;
		ProcessRunner.Run("adb", ["-s", _serial, "forward", "--remove", "tcp:" + Port]);
		var added = ProcessRunner.Run(
			"adb",
			["-s", _serial, "forward", "tcp:" + Port, "localabstract:" + SocketName]);
		if (added.Code != 0)
			throw new InvalidOperationException("adb forward failed: " + added.Stderr + added.Stdout);
		_forwarded = true;
	}

	public ApplyResult Apply(byte[] dll)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		EnsureForward();
		var started = Stopwatch.GetTimestamp();
		var reply = Send(dll);
		var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		return new ApplyResult(elapsed, reply.Ok, reply.Text);
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		if (_forwarded)
			ProcessRunner.Run("adb", ["-s", _serial, "forward", "--remove", "tcp:" + Port]);
	}

	(bool Ok, string Text) Send(byte[] dll) =>
		HotReloadHandshake.Exchange switch
		{
			HotReloadExchange.OneShot => SendOneShot(dll),
		};

	(bool Ok, string Text) SendOneShot(byte[] dll)
	{
		using var client = new TcpClient { NoDelay = true };
		client.ReceiveTimeout = 15000;
		client.SendTimeout = 15000;
		try
		{
			client.Connect(IPAddress.Loopback, Port);
		}
		catch (SocketException ex)
		{
			throw new InvalidOperationException("Hot reload connect failed: " + ex.Message);
		}

		var stream = client.GetStream();
		stream.Write(HotReloadHandshake.Frame(_token, dll));
		stream.Flush();
		try
		{
			return HotReloadHandshake.ReadReply(stream);
		}
		catch (IOException ex)
		{
			throw new InvalidOperationException("Hot reload reply failed: " + ex.Message);
		}
	}
}
