using System.Security.Cryptography;

namespace NativePoc.Devx;

static class DeviceInstall
{
	public static string HashFile(string path)
	{
		var hash = SHA256.HashData(File.ReadAllBytes(path));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	public static bool SameHash(string localHex, string deviceText)
	{
		if (localHex.Length != 64)
			return false;
		var text = deviceText.Trim();
		if (text.Length < 64)
			return false;
		var token = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
		return token.Length == 64 && string.Equals(token, localHex, StringComparison.OrdinalIgnoreCase);
	}

	public static string? PackagePath(string pmPathText)
	{
		string? found = null;
		foreach (var line in pmPathText.Split('\n'))
		{
			var trimmed = line.Trim().TrimEnd('\r');
			if (!trimmed.StartsWith("package:", StringComparison.Ordinal))
				continue;
			var path = trimmed["package:".Length..];
			if (path.EndsWith("base.apk", StringComparison.Ordinal))
				return path;
			found ??= path;
		}

		return found;
	}
}
