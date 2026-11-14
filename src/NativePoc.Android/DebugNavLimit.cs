using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NativePoc;

static class DebugNavLimit
{
	const string Property = "debug.nativepoc.nav_limit";

	internal static int OrDefault(int fallback)
	{
		var forced = Read();
		if (forced > 0 && forced < fallback)
			return forced;
		return fallback;
	}

	static int Read()
	{
		var buffer = new byte[92];
		int written;
		try
		{
			written = SystemPropertyGet(Property, buffer);
		}
		catch (DllNotFoundException)
		{
			return 0;
		}
		catch (EntryPointNotFoundException)
		{
			return 0;
		}

		if (written <= 0)
			return 0;
		if (written > buffer.Length)
			written = buffer.Length;
		var text = Encoding.UTF8.GetString(buffer, 0, written);
		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : 0;
	}

	[DllImport("libc", EntryPoint = "__system_property_get", CharSet = CharSet.Ansi)]
	static extern int SystemPropertyGet(string name, byte[] value);
}
