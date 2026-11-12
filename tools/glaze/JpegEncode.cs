namespace NativePoc.Glaze;

static class JpegEncode
{
	public static void Write(string path, byte[] rgb, int width, int height, int quality)
	{
		using var stream = File.Create(path);
		Write(stream, rgb, width, height, quality);
	}

	public static void Write(Stream stream, byte[] rgb, int width, int height, int quality)
	{
		if (width < 1 || height < 1 || rgb.Length < width * height * 3)
			throw new InvalidOperationException("The glaze bitmap is short.");
		var luma = Scale(Luma, quality);
		var chroma = Scale(Chroma, quality);
		var writer = new Writer(stream);
		writer.Marker(0xD8);
		writer.App0();
		writer.Quant(0, luma);
		writer.Quant(1, chroma);
		writer.Frame(width, height);
		writer.Huffman(0, 0, DcLumaBits, DcLumaVals);
		writer.Huffman(1, 0, AcLumaBits, AcLumaVals);
		writer.Huffman(0, 1, DcChromaBits, DcChromaVals);
		writer.Huffman(1, 1, AcChromaBits, AcChromaVals);
		writer.Scan();
		var dcY = writer.Codes(DcLumaBits, DcLumaVals);
		var acY = writer.Codes(AcLumaBits, AcLumaVals);
		var dcC = writer.Codes(DcChromaBits, DcChromaVals);
		var acC = writer.Codes(AcChromaBits, AcChromaVals);
		var prevY = 0;
		var prevCb = 0;
		var prevCr = 0;
		for (var by = 0; by < height; by += 8)
		{
			for (var bx = 0; bx < width; bx += 8)
			{
				var y = new int[64];
				var cb = new int[64];
				var cr = new int[64];
				Sample(rgb, width, height, bx, by, y, cb, cr);
				prevY = writer.Block(y, luma, prevY, dcY, acY);
				prevCb = writer.Block(cb, chroma, prevCb, dcC, acC);
				prevCr = writer.Block(cr, chroma, prevCr, dcC, acC);
			}
		}

		writer.Flush();
		writer.Marker(0xD9);
	}

	static void Sample(byte[] rgb, int width, int height, int ox, int oy, int[] y, int[] cb, int[] cr)
	{
		for (var row = 0; row < 8; row++)
		{
			var sy = Math.Min(oy + row, height - 1);
			for (var col = 0; col < 8; col++)
			{
				var sx = Math.Min(ox + col, width - 1);
				var i = (sy * width + sx) * 3;
				var r = rgb[i];
				var g = rgb[i + 1];
				var b = rgb[i + 2];
				var yy = (19595 * r + 38470 * g + 7471 * b) >> 16;
				var uu = 128 + ((-11059 * r - 21709 * g + 32768 * b) >> 16);
				var vv = 128 + ((32768 * r - 27439 * g - 5329 * b) >> 16);
				var n = row * 8 + col;
				y[n] = Clamp(yy) - 128;
				cb[n] = Clamp(uu) - 128;
				cr[n] = Clamp(vv) - 128;
			}
		}
	}

	static int Clamp(int value)
	{
		if (value < 0)
			return 0;
		if (value > 255)
			return 255;
		return value;
	}

	static int[] Scale(int[] table, int quality)
	{
		if (quality < 1)
			quality = 1;
		if (quality > 100)
			quality = 100;
		var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
		var next = new int[64];
		for (var i = 0; i < 64; i++)
		{
			var value = (table[i] * scale + 50) / 100;
			if (value < 1)
				value = 1;
			if (value > 255)
				value = 255;
			next[i] = value;
		}

		return next;
	}

	sealed class Writer
	{
		readonly Stream _stream;
		ulong _acc;
		int _bits;

		public Writer(Stream stream) => _stream = stream;

		public void Marker(int code)
		{
			_stream.WriteByte(0xFF);
			_stream.WriteByte((byte)code);
		}

		public void App0()
		{
			Marker(0xE0);
			var body = new byte[] { 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
			_stream.Write(body);
		}

		public void Quant(int id, int[] table)
		{
			Marker(0xDB);
			U16(67);
			_stream.WriteByte((byte)id);
			for (var i = 0; i < 64; i++)
				_stream.WriteByte((byte)table[Zigzag[i]]);
		}

		public void Frame(int width, int height)
		{
			Marker(0xC0);
			U16(17);
			_stream.WriteByte(8);
			U16(height);
			U16(width);
			_stream.WriteByte(3);
			_stream.WriteByte(1);
			_stream.WriteByte(0x11);
			_stream.WriteByte(0);
			_stream.WriteByte(2);
			_stream.WriteByte(0x11);
			_stream.WriteByte(1);
			_stream.WriteByte(3);
			_stream.WriteByte(0x11);
			_stream.WriteByte(1);
		}

		public void Huffman(int tableClass, int id, byte[] bits, byte[] values)
		{
			Marker(0xC4);
			U16(2 + 1 + 16 + values.Length);
			_stream.WriteByte((byte)((tableClass << 4) | id));
			_stream.Write(bits);
			_stream.Write(values);
		}

		public void Scan()
		{
			Marker(0xDA);
			U16(12);
			_stream.WriteByte(3);
			_stream.WriteByte(1);
			_stream.WriteByte(0x00);
			_stream.WriteByte(2);
			_stream.WriteByte(0x11);
			_stream.WriteByte(3);
			_stream.WriteByte(0x11);
			_stream.WriteByte(0);
			_stream.WriteByte(63);
			_stream.WriteByte(0);
		}

		public (int[] Code, int[] Len) Codes(byte[] bits, byte[] values)
		{
			var code = new int[256];
			var len = new int[256];
			var cursor = 0;
			var next = 0;
			for (var size = 1; size <= 16; size++)
			{
				for (var n = 0; n < bits[size - 1]; n++)
				{
					code[values[cursor]] = next;
					len[values[cursor]] = size;
					cursor++;
					next++;
				}

				next <<= 1;
			}

			return (code, len);
		}

		public int Block(int[] samples, int[] quant, int prev, (int[] Code, int[] Len) dc, (int[] Code, int[] Len) ac)
		{
			var freq = Dct(samples, quant);
			var diff = freq[0] - prev;
			var (cat, extra) = Amp(diff);
			Put(dc.Code[cat], dc.Len[cat]);
			Put(extra, cat);
			var run = 0;
			for (var i = 1; i < 64; i++)
			{
				var value = freq[Zigzag[i]];
				if (value == 0)
				{
					run++;
					continue;
				}

				while (run >= 16)
				{
					Put(ac.Code[0xF0], ac.Len[0xF0]);
					run -= 16;
				}

				(cat, extra) = Amp(value);
				if (cat > 10)
				{
					cat = 10;
					extra &= 0x3FF;
				}

				var symbol = (run << 4) | cat;
				Put(ac.Code[symbol], ac.Len[symbol]);
				Put(extra, cat);
				run = 0;
			}

			if (run > 0)
				Put(ac.Code[0], ac.Len[0]);
			return freq[0];
		}

		public void Flush()
		{
			if (_bits <= 0)
				return;
			var pad = 8 - _bits;
			_acc = (_acc << pad) | ((1UL << pad) - 1);
			_bits = 8;
			var b = (int)((_acc >> 0) & 0xFF);
			Raw(b);
			if (b == 0xFF)
				Raw(0);
			_bits = 0;
			_acc = 0;
		}

		void Put(int value, int length)
		{
			if (length <= 0)
				return;
			_acc = (_acc << length) | (uint)value;
			_bits += length;
			while (_bits >= 8)
			{
				_bits -= 8;
				var b = (int)((_acc >> _bits) & 0xFF);
				Raw(b);
				if (b == 0xFF)
					Raw(0);
			}
		}

		void Raw(int value) => _stream.WriteByte((byte)value);

		void U16(int value)
		{
			_stream.WriteByte((byte)(value >> 8));
			_stream.WriteByte((byte)value);
		}
	}

	static int[] Dct(int[] samples, int[] quant)
	{
		var freq = new int[64];
		for (var v = 0; v < 8; v++)
		{
			var cv = v == 0 ? 0.7071067811865476 : 1d;
			for (var u = 0; u < 8; u++)
			{
				var cu = u == 0 ? 0.7071067811865476 : 1d;
				var sum = 0d;
				for (var y = 0; y < 8; y++)
				{
					for (var x = 0; x < 8; x++)
						sum += samples[y * 8 + x] * Cos[x, u] * Cos[y, v];
				}

				var q = (int)Math.Round(sum * 0.25 * cu * cv / quant[v * 8 + u]);
				if (q > 1023)
					q = 1023;
				if (q < -1023)
					q = -1023;
				freq[v * 8 + u] = q;
			}
		}

		return freq;
	}

	static (int Cat, int Extra) Amp(int value)
	{
		var mag = value < 0 ? -value : value;
		var cat = 0;
		while ((1 << cat) <= mag && cat < 11)
			cat++;
		var extra = value < 0 ? value + (1 << cat) - 1 : value;
		return (cat, extra);
	}

	static readonly double[,] Cos = BuildCos();

	static double[,] BuildCos()
	{
		var table = new double[8, 8];
		for (var i = 0; i < 8; i++)
		{
			for (var u = 0; u < 8; u++)
				table[i, u] = Math.Cos((2 * i + 1) * u * Math.PI / 16.0);
		}

		return table;
	}

	static readonly int[] Zigzag =
	[
		0, 1, 8, 16, 9, 2, 3, 10,
		17, 24, 32, 25, 18, 11, 4, 5,
		12, 19, 26, 33, 40, 48, 41, 34,
		27, 20, 13, 6, 7, 14, 21, 28,
		35, 42, 49, 56, 57, 50, 43, 36,
		29, 22, 15, 23, 30, 37, 44, 51,
		58, 59, 52, 45, 38, 31, 39, 46,
		53, 60, 61, 54, 47, 55, 62, 63,
	];

	static readonly int[] Luma =
	[
		16, 11, 10, 16, 24, 40, 51, 61,
		12, 12, 14, 19, 26, 58, 60, 55,
		14, 13, 16, 24, 40, 57, 69, 56,
		14, 17, 22, 29, 51, 87, 80, 62,
		18, 22, 37, 56, 68, 109, 103, 77,
		24, 35, 55, 64, 81, 104, 113, 92,
		49, 64, 78, 87, 103, 121, 120, 101,
		72, 92, 95, 98, 112, 100, 103, 99,
	];

	static readonly int[] Chroma =
	[
		17, 18, 24, 47, 99, 99, 99, 99,
		18, 21, 26, 66, 99, 99, 99, 99,
		24, 26, 56, 99, 99, 99, 99, 99,
		47, 66, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
	];

	static readonly byte[] DcLumaBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
	static readonly byte[] DcLumaVals = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
	static readonly byte[] DcChromaBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
	static readonly byte[] DcChromaVals = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
	static readonly byte[] AcLumaBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d];
	static readonly byte[] AcLumaVals =
	[
		0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
		0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
		0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
		0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
		0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
		0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
		0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
		0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
		0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
		0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
		0xf9, 0xfa,
	];
	static readonly byte[] AcChromaBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
	static readonly byte[] AcChromaVals =
	[
		0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
		0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
		0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
		0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
		0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
		0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
		0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
		0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
		0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
		0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
		0xf9, 0xfa,
	];
}
