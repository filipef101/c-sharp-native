#if DEBUG
using System.Globalization;
using Android.Content;
using Android.Util;

namespace NativePoc;

static class StateStore
{
	const string FileName = "nativepoc-state.bin";
	static readonly object Gate = new();
	static Context? _context;
	static byte[]? _latest;
	static byte[]? _written;
	static int _writing;

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
		var bytes = StateCheckpoint.Write(AppSession.Shared.State, PackingView.Capture(), ScreenNav.Shared.Checkpoint());
		lock (Gate)
		{
			_context = context.ApplicationContext ?? context;
			_latest = bytes;
			if (_writing == 1)
				return;
			_writing = 1;
		}

		ThreadPool.QueueUserWorkItem(static _ => WriteQueued());
	}

	static void WriteQueued()
	{
		while (true)
		{
			byte[]? bytes;
			Context? context;
			lock (Gate)
			{
				bytes = _latest;
				context = _context;
				_latest = null;
				if (bytes is not null && _written is not null && bytes.AsSpan().SequenceEqual(_written))
					bytes = null;
				if (bytes is null || context is null)
				{
					if (_latest is null)
					{
						_writing = 0;
						return;
					}

					continue;
				}
			}

			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			var wrote = false;
			try
			{
				using var output = context.OpenFileOutput(FileName, FileCreationMode.Private)
					?? throw new IOException("The state file was not opened.");
				output.Write(bytes, 0, bytes.Length);
				output.Flush();
				wrote = true;
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException or Java.IO.IOException)
			{
				Log.Warn(MainActivity.Tag, "state-save " + ex.GetType().Name);
			}

			if (!wrote)
				continue;
			var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
			var micros = elapsed * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
			Log.Info(MainActivity.Tag, "state-save-us=" + micros.ToString(CultureInfo.InvariantCulture));
			lock (Gate)
			{
				_written = bytes;
				if (_latest is null || _latest.AsSpan().SequenceEqual(bytes))
				{
					_latest = null;
					_writing = 0;
					return;
				}
			}
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
				NavCatalog.ApplyCheckpoint(nav);
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
