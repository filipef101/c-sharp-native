using System.Text;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class HotReloadHandshakeTests
{
	[Fact]
	public void Socket_name_is_the_package_and_the_frame_still_starts_with_the_token()
	{
		Assert.Equal("npoc-com.scratch.hello", HotReloadHandshake.SocketFor("com.scratch.hello"));
		Assert.Equal("npoc-com.scratch.nativepoc", HotReloadHandshake.SocketFor("com.scratch.nativepoc"));
		Assert.NotEqual(HotReloadHandshake.PortFor("com.scratch.hello"), HotReloadHandshake.PortFor("com.scratch.nativepoc"));
		Assert.InRange(HotReloadHandshake.PortFor("com.scratch.hello"), 20000, 29999);
		var token = HotReloadHandshake.Token("0123456789ABCDEF0123456789ABCDEF");
		var frame = HotReloadHandshake.Frame(token, [1]);
		Assert.Equal(token, frame[..16]);
	}

	[Fact]
	public void Frame_is_the_token_then_the_length_then_the_payload()
	{
		var token = HotReloadHandshake.Token("000102030405060708090a0b0c0d0e0f");
		var dll = new byte[] { 9, 8, 7 };
		var frame = HotReloadHandshake.Frame(token, dll);

		Assert.Equal(23, frame.Length);
		Assert.Equal(token, frame[..16]);
		Assert.Equal(3, frame[16] | (frame[17] << 8) | (frame[18] << 16) | (frame[19] << 24));
		Assert.Equal(dll, frame[20..]);
	}

	[Fact]
	public void Reply_reads_the_five_byte_header_and_the_utf8_body()
	{
		var message = Encoding.UTF8.GetBytes("Counter 0 collectible=True");
		var header = new byte[5];
		header[0] = 1;
		header[1] = (byte)message.Length;
		using var stream = new MemoryStream();
		stream.Write(header);
		stream.Write(message);
		stream.Position = 0;

		var reply = HotReloadHandshake.ReadReply(stream);

		Assert.True(reply.Ok);
		Assert.Equal("Counter 0 collectible=True", reply.Text);
	}

	[Fact]
	public void Stale_owned_forwards_drop_the_retired_socket_and_keep_the_other_package()
	{
		var listing = """
			emulator-5554 tcp:18765 localabstract:npoc-hotreload
			emulator-5554 tcp:21639 localabstract:npoc-com.scratch.nativepoc
			emulator-5554 tcp:22268 localabstract:npoc-com.scratch.hello
			emulator-5554 tcp:19991 localabstract:keep-me
			""";
		var stale = HotReloadForwards.StalePorts(listing, "emulator-5554", 21639, "npoc-com.scratch.nativepoc");
		Assert.Equal(new[] { 18765 }, stale);

		var devices = ProcessRunner.Run("adb", ["devices"]);
		if (devices.Code != 0 || !devices.Stdout.Contains("emulator-5554\tdevice", StringComparison.Ordinal))
			return;
		ProcessRunner.Run("adb", ["-s", "emulator-5554", "forward", "tcp:18765", "localabstract:npoc-hotreload"]);
		ProcessRunner.Run("adb", ["-s", "emulator-5554", "forward", "tcp:19991", "localabstract:keep-me"]);
		try
		{
			HotReloadHandshake.DropStaleForwards("emulator-5554", 21639, "npoc-com.scratch.nativepoc");
			var after = ProcessRunner.Run("adb", ["-s", "emulator-5554", "forward", "--list"]);
			Assert.Equal(0, after.Code);
			Assert.DoesNotContain("tcp:18765", after.Stdout, StringComparison.Ordinal);
			Assert.DoesNotContain("npoc-hotreload", after.Stdout, StringComparison.Ordinal);
			Assert.Contains("localabstract:keep-me", after.Stdout, StringComparison.Ordinal);
		}
		finally
		{
			ProcessRunner.Run("adb", ["-s", "emulator-5554", "forward", "--remove", "tcp:19991"]);
		}
	}

	[Fact]
	public void Today_the_device_closes_the_socket_after_one_exchange()
	{
		Assert.Equal(HotReloadExchange.OneShot, HotReloadHandshake.Exchange);
	}
}
