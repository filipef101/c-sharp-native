#if DEBUG
using Android.Content;
using Android.Util;

namespace NativePoc;

static class StateStore
{
	const string FileName = "nativepoc-state.bin";

	public static void Delete(Context context)
	{
		try
		{
			context.DeleteFile(FileName);
		}
		catch (Exception ex) when (ex is IOException or Java.IO.IOException)
		{
			Log.Warn(MainActivity.Tag, "state-delete " + ex.GetType().Name);
		}
	}

	public static void Save(Context context)
	{
		try
		{
			var bytes = StateCheckpoint.Write(AppSession.Shared.State, PackingView.Capture(), ScreenNav.Shared.Checkpoint());
			using var output = context.OpenFileOutput(FileName, FileCreationMode.Private)
				?? throw new IOException("The state file was not opened.");
			output.Write(bytes, 0, bytes.Length);
			output.Flush();
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or Java.IO.IOException)
		{
			Log.Warn(MainActivity.Tag, "state-save " + ex.GetType().Name);
		}
	}

	public static bool TryLoad(Context context)
	{
		try
		{
			using var input = context.OpenFileInput(FileName)
				?? throw new IOException("The state file was not opened.");
			using var memory = new MemoryStream();
			input.CopyTo(memory);
			var bytes = memory.ToArray();
			StateCheckpoint.ReadInto(bytes, AppSession.Shared.State);
			if (StateCheckpoint.TryReadPacking(bytes, out var packing))
				PackingView.Apply(packing);
			if (StateCheckpoint.TryReadNav(bytes, out var nav))
				ScreenNav.Shared.Restore(nav);
			Log.Info(MainActivity.Tag, "state-loaded bytes=" + bytes.Length);
			return true;
		}
		catch (Java.IO.FileNotFoundException)
		{
			return false;
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or Java.IO.IOException)
		{
			Log.Warn(MainActivity.Tag, "state-load " + ex.GetType().Name);
			return false;
		}
	}
}
#endif
