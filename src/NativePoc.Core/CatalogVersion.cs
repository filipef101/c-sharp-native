using System.Globalization;
using System.Text.RegularExpressions;

namespace NativePoc;

internal static class CatalogVersion
{
	public static readonly string[] Names = ["Counter", "Packing", "Settings", "Trips", "Trip", "Kit", "Web"];

	public static bool Current(ReadOnlySpan<byte> payload) =>
		payload.Length > 6
		&& payload[0] == (byte)'N'
		&& payload[1] == (byte)'P'
		&& payload[2] == (byte)'C'
		&& payload[3] == (byte)'1'
		&& payload[4] == RecordProgram.RecordFormat
		&& payload[5] == 2
		&& payload[6] == RecordProgram.OpcodeVersion;

	public static string? Problem(string source) => Problem(source, RecordProgram.OpcodeVersion);

	public static string? Problem(string source, byte expected)
	{
		foreach (var name in Names)
		{
			var bytes = Read(source, name);
			if (bytes is null)
				return name + " missing";
			if (bytes.Length <= 6 || bytes[6] != expected)
				return name + " opcode " + (bytes.Length > 6 ? bytes[6].ToString(CultureInfo.InvariantCulture) : "short");
		}

		return null;
	}

	public static byte[]? Read(string source, string name)
	{
		var match = Regex.Match(source, "static readonly byte\\[] " + name + " = \\[([^\\]]+)\\]", RegexOptions.Singleline);
		if (!match.Success)
			return null;
		var numbers = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var bytes = new byte[numbers.Length];
		for (var index = 0; index < numbers.Length; index++)
			bytes[index] = byte.Parse(numbers[index], CultureInfo.InvariantCulture);
		return bytes;
	}
}
