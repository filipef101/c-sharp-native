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

// The device writes files/nativepoc-hotreload.socket with npoc- plus the package.
// The host port comes from that package, so two listeners can forward at once.
// The first 16 bytes are the token. Each accepted socket still closes after one
// exchange, so this session keeps the adb forward and opens a TCP connection per push.
static class HotReloadHandshake
{
	public const int TokenBytes = 16;
	public const HotReloadExchange Exchange = HotReloadExchange.OneShot;

	public static string SocketFor(string package) => HotReloadAddress.SocketFor(package);

	public static int PortFor(string package) => HotReloadAddress.PortFor(package);

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
	readonly string _serial;
	readonly byte[] _token;
	readonly int _port;
	bool _forwarded;
	bool _disposed;

	public HotReloadConnection(string serial, string tokenHex, string? socketName = null, int port = 0)
	{
		_serial = serial;
		_token = HotReloadHandshake.Token(tokenHex);
		var package = LaunchCommand.Package;
		SocketName = socketName is { Length: > 0 } ? socketName : HotReloadHandshake.SocketFor(package);
		_port = port > 0 ? port : HotReloadHandshake.PortFor(package);
	}

	public string SocketName { get; }

	public int Port => _port;

	public void EnsureForward()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_forwarded)
			return;
		ProcessRunner.Run("adb", ["-s", _serial, "forward", "--remove", "tcp:" + _port]);
		var added = ProcessRunner.Run(
			"adb",
			["-s", _serial, "forward", "tcp:" + _port, "localabstract:" + SocketName]);
		if (added.Code != 0)
			throw new InvalidOperationException("adb forward failed: " + added.Stderr + added.Stdout);
		Console.WriteLine("cnative: hotreload socket=" + SocketName + " port=" + _port.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
			client.Connect(IPAddress.Loopback, _port);
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
