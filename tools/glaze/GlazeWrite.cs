using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

var dir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(dir);
(string Name, uint Background, uint Ring)[] tiles =
[
	("shelf_harbor", 0x6E97A0, 0xD5E6EA),
	("shelf_kiln", 0x4A4038, 0x8A7B6E),
	("shelf_cedar", 0xA89886, 0xE4D9CC),
	("shelf_night", 0x163A5C, 0x3E78B0),
	("shelf_field", 0xC4923A, 0xF0D48A),
	("shelf_rain", 0x3E4850, 0x8A969E),
	("shelf_linen", 0xC9C0B0, 0xF3EFE6),
	("shelf_moss", 0x8C4A34, 0xD4A090),
];
foreach (var tile in tiles)
	WritePng(Path.Combine(dir, tile.Name + ".png"), 1024, tile.Background, tile.Ring);

static void WritePng(string path, int size, uint background, uint ring)
{
	var pixels = new byte[size * size * 3];
	var bgR = (byte)((background >> 16) & 0xFF);
	var bgG = (byte)((background >> 8) & 0xFF);
	var bgB = (byte)(background & 0xFF);
	var ringR = (byte)((ring >> 16) & 0xFF);
	var ringG = (byte)((ring >> 8) & 0xFF);
	var ringB = (byte)(ring & 0xFF);
	var index = 0;
	var cx = size / 2;
	var cy = size * 46 / 100;
	var radius = size * 58L / 100;
	var radius2 = radius * radius;
	for (var y = 0; y < size; y++)
	{
		for (var x = 0; x < size; x++)
		{
			var dx = x - cx;
			var dy = y - cy;
			var dist2 = dx * (long)dx + dy * (long)dy;
			var fall = dist2 >= radius2 ? 255 : (int)(dist2 * 255 / radius2);
			fall = fall * fall / 255;
			var r = Mix(ringR, bgR, fall);
			var g = Mix(ringG, bgG, fall);
			var b = Mix(ringB, bgB, fall);
			var drip = Drip(x, y, size);
			r = Mix(r, bgR, drip);
			g = Mix(g, bgG, drip);
			b = Mix(b, bgB, drip);
			var highlight = Highlight(x, y, size);
			r = Mix(r, 255, highlight);
			g = Mix(g, 255, highlight);
			b = Mix(b, 255, highlight);
			var speck = Speck(x, y);
			pixels[index++] = Clamp(r + speck);
			pixels[index++] = Clamp(g + speck);
			pixels[index++] = Clamp(b + speck);
		}
	}

	using var raw = new MemoryStream();
	using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
	{
		var row = new byte[1 + size * 3];
		for (var y = 0; y < size; y++)
		{
			row[0] = 0;
			pixels.AsSpan(y * size * 3, size * 3).CopyTo(row.AsSpan(1));
			zlib.Write(row);
		}
	}

	var ihdr = new byte[13];
	BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), size);
	BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), size);
	ihdr[8] = 8;
	ihdr[9] = 2;
	using var output = File.Create(path);
	output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
	Chunk(output, "IHDR", ihdr);
	Chunk(output, "IDAT", raw.ToArray());
	Chunk(output, "IEND", Array.Empty<byte>());
}

static byte Mix(byte from, byte to, int cover)
{
	if (cover <= 0)
		return from;
	if (cover >= 255)
		return to;
	return (byte)((from * (255 - cover) + to * cover) / 255);
}

static byte Clamp(int value)
{
	if (value < 0)
		return 0;
	if (value > 255)
		return 255;
	return (byte)value;
}

static int Drip(int x, int y, int size)
{
	ReadOnlySpan<int> seeds = [31, 54, 71];
	var best = 0;
	for (var i = 0; i < seeds.Length; i++)
	{
		var start = size * (18 + (i * 5)) / 100;
		if (y < start)
			continue;
		var column = size * seeds[i] / 100;
		var width = size * (5 + (i * 2)) / 100;
		var adx = x - column;
		if (adx < 0)
			adx = -adx;
		if (adx >= width)
			continue;
		var length = size * (46 + (i * 10)) / 100;
		var fall = y - start;
		if (fall >= length)
			continue;
		var head = length * 22 / 100;
		if (head < 1)
			head = 1;
		var fadeIn = fall >= head ? 255 : fall * 255 / head;
		var tail = length * 35 / 100;
		if (tail < 1)
			tail = 1;
		var remain = length - fall;
		var fadeOut = remain >= tail ? 255 : remain * 255 / tail;
		var across = (width - adx) * 255 / width;
		var cover = across * fadeIn / 255 * fadeOut / 255;
		if (cover > best)
			best = cover;
	}

	return best * 150 / 255;
}

static int Highlight(int x, int y, int size)
{
	var hx = x - (size * 38 / 100);
	var hy = y - (size * 32 / 100);
	var h2 = hx * (long)hx + hy * (long)hy * 2;
	var rad = size * 16L / 100;
	var rad2 = rad * rad;
	if (h2 >= rad2)
		return 0;
	return (int)((rad2 - h2) * 140 / rad2);
}

static int Speck(int x, int y)
{
	var cell = Hash(x / 4, y / 4);
	if (cell > 248)
		return 16;
	if (cell < 6)
		return -12;
	return 0;
}

static int Hash(int x, int y)
{
	var n = (uint)(x * 374761393 + y * 668265263);
	n = (n ^ (n >> 13)) * 1274126177u;
	return (int)((n ^ (n >> 16)) & 255);
}

static void Chunk(Stream output, string type, byte[] data)
{
	Span<byte> length = stackalloc byte[4];
	BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
	output.Write(length);
	var name = Encoding.ASCII.GetBytes(type);
	output.Write(name);
	output.Write(data);
	var crcInput = new byte[name.Length + data.Length];
	name.CopyTo(crcInput, 0);
	data.CopyTo(crcInput, name.Length);
	Span<byte> crc = stackalloc byte[4];
	BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
	output.Write(crc);
}

static uint Crc32(ReadOnlySpan<byte> data)
{
	var crc = 0xFFFFFFFFu;
	foreach (var value in data)
	{
		crc ^= value;
		for (var bit = 0; bit < 8; bit++)
			crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
	}

	return crc ^ 0xFFFFFFFFu;
}
