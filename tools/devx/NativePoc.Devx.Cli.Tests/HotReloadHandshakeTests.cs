using System.Text;
using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class HotReloadHandshakeTests
{
	[Fact]
	public void Socket_name_is_fixed_and_the_frame_still_starts_with_the_token()
	{
		Assert.Equal("npoc-hotreload", HotReloadHandshake.AbstractSocketName);
		Assert.DoesNotContain("0123456789abcdef", HotReloadHandshake.AbstractSocketName, StringComparison.Ordinal);
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
	public void Today_the_device_closes_the_socket_after_one_exchange()
	{
		Assert.Equal(HotReloadExchange.OneShot, HotReloadHandshake.Exchange);
	}
}
