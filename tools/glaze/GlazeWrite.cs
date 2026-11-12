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
	for (var y = 0; y < size; y++)
	{
		for (var x = 0; x < size; x++)
		{
			var cover = 0;
			for (var sy = 0; sy < 2; sy++)
			{
				for (var sx = 0; sx < 2; sx++)
				{
					if (InRing(x * 2 + sx, y * 2 + sy, size * 2))
						cover++;
				}
			}

			pixels[index++] = Mix(bgR, ringR, cover);
			pixels[index++] = Mix(bgG, ringG, cover);
			pixels[index++] = Mix(bgB, ringB, cover);
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

static bool InRing(int x, int y, int size)
{
	var dx = x - size / 2;
	var dy = y - size / 2;
	var distance = dx * (long)dx + dy * (long)dy;
	var outer = size * 42L / 100;
	var inner = size * 18L / 100;
	return distance <= outer * outer && distance >= inner * inner;
}

static byte Mix(byte background, byte ring, int cover) =>
	(byte)((background * (4 - cover) + ring * cover) / 4);

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
