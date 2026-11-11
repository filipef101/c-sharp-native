using Android.Content;

namespace NativePoc;

static class AppStorage
{
	const string File = "nativepoc-settings";

	public static void Set(Context context, string key, string value)
	{
		var editor = context.GetSharedPreferences(File, FileCreationMode.Private)?.Edit()
			?? throw new InvalidOperationException("Settings storage is not open.");
		editor.PutString(key, value);
		if (!editor.Commit())
			throw new InvalidOperationException("Settings storage did not commit.");
		Android.Util.Log.Info(MainActivity.Tag, "storage-set key=" + key + " chars=" + value.Length);
	}

	public static string? Get(Context context, string key)
	{
		var value = context.GetSharedPreferences(File, FileCreationMode.Private)?.GetString(key, null);
		if (value is not null)
			Android.Util.Log.Info(MainActivity.Tag, "storage-load key=" + key + " chars=" + value.Length);
		return value;
	}
}
