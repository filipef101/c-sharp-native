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
	static byte[]? _written;
	static int _writing;
	static bool _dirty;
	static bool _have;
	static int _epoch;
	static int _count;
	static int _flags;
	static string _name = "";

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
		var epoch = StateEpoch.Value;
		var state = AppSession.Shared.State;
		var count = state.Count;
		var name = state.Name;
		var flags = (state.GreetingFirst ? 1 : 0) | (state.PlainRoot ? 2 : 0);
		lock (Gate)
		{
			if (_have && _writing == 0 && epoch == _epoch && count == _count && flags == _flags && name == _name)
				return;
			_context = context.ApplicationContext ?? context;
			_dirty = true;
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
			Context? context;
			lock (Gate)
			{
				context = _context;
				if (!_dirty || context is null)
				{
					_writing = 0;
					return;
				}

				_dirty = false;
			}

			var epoch = StateEpoch.Value;
			var state = AppSession.Shared.State;
			var count = state.Count;
			var name = state.Name;
			var flags = (state.GreetingFirst ? 1 : 0) | (state.PlainRoot ? 2 : 0);
			byte[] bytes;
			try
			{
				bytes = StateCheckpoint.Write(state, PackingView.Capture(), ScreenNav.Shared.Checkpoint());
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException)
			{
				Log.Warn(MainActivity.Tag, "state-save " + ex.GetType().Name);
				lock (Gate)
				{
					_dirty = true;
					_writing = 0;
					return;
				}
			}

			var moved = StateEpoch.Value != epoch || state.Count != count || state.Name != name
				|| ((state.GreetingFirst ? 1 : 0) | (state.PlainRoot ? 2 : 0)) != flags;
			lock (Gate)
			{
				if (moved)
					_dirty = true;
				if (_written is not null && bytes.AsSpan().SequenceEqual(_written))
				{
					if (!moved && !_dirty)
						Remember(epoch, count, name, flags);
					if (_dirty)
						continue;
					_writing = 0;
					return;
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
			{
				lock (Gate)
				{
					if (_dirty)
						continue;
					_writing = 0;
					return;
				}
			}

			var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
			var micros = elapsed * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
			Log.Info(MainActivity.Tag, "state-save-us=" + micros.ToString(CultureInfo.InvariantCulture));
			lock (Gate)
			{
				_written = bytes;
				if (!moved && !_dirty)
					Remember(epoch, count, name, flags);
				else
					_dirty = true;
				if (_dirty)
					continue;
				_writing = 0;
				return;
			}
		}
	}

	static void Remember(int epoch, int count, string name, int flags)
	{
		_epoch = epoch;
		_count = count;
		_name = name;
		_flags = flags;
		_have = true;
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
		catch (InvalidOperationException ex)
		{
			Log.Warn(MainActivity.Tag, StartupGuard.StateLog(StartupGuard.StateReason(ex.Message)));
			Delete(context);
			return false;
		}
		catch (Exception ex) when (ex is IOException or Java.IO.IOException)
		{
			Log.Warn(MainActivity.Tag, StartupGuard.StateLog(StartupGuard.StateReason(ex.Message)));
			return false;
		}
	}
}
#endif
