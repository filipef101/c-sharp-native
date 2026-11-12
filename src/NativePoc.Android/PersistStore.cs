using Android.Content;

namespace NativePoc;

static class PersistStore
{
	public static string Get(Context context, string key)
	{
		if (!TryPath(context, key, out var path) || !File.Exists(path))
			return "";
		var value = File.ReadAllText(path);
		Android.Util.Log.Info(MainActivity.Tag, "persist-get key=" + key + " chars=" + value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return value;
	}

	public static string Put(Context context, string key, string value)
	{
		if (!TryPath(context, key, out var path))
			return "http-error: persist";
		Write(path, value);
		Android.Util.Log.Info(MainActivity.Tag, "persist-put key=" + key + " chars=" + value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return "ok";
	}

	public static string GetRecord(Context context, string key)
	{
		var text = Get(context, key);
		if (text.Length == 0)
			return "";
		if (!PersistCodec.TryDecode(text, out var record))
			return "";
		Android.Util.Log.Info(MainActivity.Tag, "persist-record-load key=" + key + " id=" + record.Id);
		return text;
	}

	public static string PutRecord(Context context, string key, string id, string title, string note)
	{
		var encoded = PersistCodec.Encode(new PersistRecord(id, title, note));
		var wrote = Put(context, key, encoded);
		if (wrote != "ok")
			return wrote;
		Android.Util.Log.Info(MainActivity.Tag, "persist-record key=" + key + " id=" + id);
		return encoded;
	}

	static void Write(string path, string value)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		var temp = path + ".tmp";
		File.WriteAllText(temp, value);
		File.Move(temp, path, overwrite: true);
	}

	static bool TryPath(Context context, string key, out string path)
	{
		path = "";
		if (key.Length == 0 || key.Length > 64)
			return false;
		foreach (var ch in key)
		{
			var ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch is '.' or '_' or '-';
			if (!ok)
				return false;
		}

		var root = context.FilesDir?.AbsolutePath;
		if (string.IsNullOrEmpty(root))
			return false;
		path = Path.Combine(root, "nativepoc-persist", key);
		return true;
	}
}
