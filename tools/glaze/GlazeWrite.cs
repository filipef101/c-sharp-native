namespace NativePoc.Glaze;

static class GlazeWrite
{
	const int Size = 512;
	const int Quality = 42;

	static readonly (string Name, uint Body, uint Wash)[] Tiles =
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

	public static int Main(string[] args)
	{
		var dir = args.Length > 0 ? args[0] : ".";
		Directory.CreateDirectory(dir);
		foreach (var stale in Directory.EnumerateFiles(dir, "shelf_*.png"))
			File.Delete(stale);
		foreach (var (name, body, wash) in Tiles)
		{
			var path = Path.Combine(dir, name + ".jpg");
			JpegEncode.Write(path, Paint(body, wash), Size, Size, Quality);
			Console.WriteLine(name + " " + new FileInfo(path).Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		return 0;
	}

	static byte[] Paint(uint body, uint wash)
	{
		var rgb = new byte[Size * Size * 3];
		var seed = (int)((body >> 16) ^ (body >> 8) ^ body) & 7;
		for (var y = 0; y < Size; y++)
		{
			for (var x = 0; x < Size; x++)
			{
				var fx = x / (double)(Size - 1);
				var fy = y / (double)(Size - 1);
				var dx = fx - 0.46;
				var dy = fy - 0.42;
				var dist = Math.Sqrt(dx * dx + dy * dy) / 0.72;
				if (dist > 1)
					dist = 1;
				var pool = 1 - dist;
				pool = pool * pool * (3 - 2 * pool);
				var grain = 0.035 * Math.Sin((fx * 5.2 + seed) * Math.PI) * Math.Sin((fy * 3.4 + seed * 0.7) * Math.PI);
				var settle = Math.Pow(Math.Max(0, fy - 0.5) / 0.5, 1.35) * (1 - dist * 0.35);
				var mix = Clamp01(pool * 0.62 + grain + settle * 0.28);
				var r = Mix(Byte(body, 16), Byte(wash, 16), mix);
				var g = Mix(Byte(body, 8), Byte(wash, 8), mix);
				var b = Mix(Byte(body, 0), Byte(wash, 0), mix);
				var hi = Highlight(fx, fy);
				r = Mix(r, 255, hi);
				g = Mix(g, 255, hi);
				b = Mix(b, 255, hi);
				for (var run = 0; run < 2; run++)
				{
					var cover = Run(fx, fy, seed + run * 3, run);
					r = Mix(r, Byte(wash, 16), cover);
					g = Mix(g, Byte(wash, 8), cover);
					b = Mix(b, Byte(wash, 0), cover);
				}

				var speck = Speck(x, y);
				r = Clamp(r + speck);
				g = Clamp(g + speck);
				b = Clamp(b + speck);
				var i = (y * Size + x) * 3;
				rgb[i] = (byte)r;
				rgb[i + 1] = (byte)g;
				rgb[i + 2] = (byte)b;
			}
		}

		return rgb;
	}

	static double Run(double fx, double fy, int seed, int index)
	{
		var start = index == 0 ? 0.28 + (seed % 4) * 0.05 : 0.66 + (seed % 3) * 0.04;
		var drift = (index == 0 ? 0.14 : -0.1) * Math.Sin((fy * (1.6 + index) + seed * 0.4) * Math.PI);
		var bend = 0.03 * Math.Sin((fy * 5.0 + seed + index) * Math.PI);
		var center = start + drift + bend;
		var along = (fy - 0.02) / 0.92;
		if (along <= 0 || along >= 1)
			return 0;
		var taper = Math.Pow(1 - along, 1.35);
		var half = (index == 0 ? 0.12 : 0.07) * taper + 0.01;
		var dist = Math.Abs(fx - center) / half;
		if (dist >= 1)
			return 0;
		var edge = 1 - dist * dist;
		var head = Math.Min(1, along / 0.1);
		return edge * edge * head * (0.35 + 0.65 * taper) * (index == 0 ? 0.42 : 0.22);
	}

	static double Highlight(double fx, double fy)
	{
		var dx = (fx - 0.36) / 0.22;
		var dy = (fy - 0.30) / 0.16;
		var d = dx * dx + dy * dy;
		if (d >= 1)
			return 0;
		return (1 - d) * (1 - d) * 0.28;
	}

	static int Speck(int x, int y)
	{
		var cell = Hash(x / 6, y / 6);
		if (cell < 250)
			return 0;
		return (Hash(x, y) & 1) == 0 ? 5 : -4;
	}

	static int Mix(int a, int b, double t) => Clamp(a + (int)Math.Round((b - a) * t));

	static double Clamp01(double value)
	{
		if (value < 0)
			return 0;
		if (value > 1)
			return 1;
		return value;
	}

	static int Clamp(int value)
	{
		if (value < 0)
			return 0;
		if (value > 255)
			return 255;
		return value;
	}

	static int Byte(uint color, int shift) => (int)((color >> shift) & 0xFF);

	static int Hash(int x, int y)
	{
		var n = (uint)(x * 374761393 + y * 668265263);
		n = (n ^ (n >> 13)) * 1274126177;
		return (int)((n ^ (n >> 16)) & 255);
	}
}
