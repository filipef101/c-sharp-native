#if NATIVEPOC_RECYCLER
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.Interop;

namespace NativePoc;

static class KeyedList
{
	const int OpCount = 1;
	const int OpId = 2;
	const int OpIdle = 4;
	const int OpReady = 8;
	const int OpSwipe = 16;
	const int OpRefresh = 32;

	static readonly SignalFn SignalSink = OnSignal;
	static readonly BindFn BindSink = OnBind;
	static readonly ConditionalWeakTable<ViewGroup, State> Live = new();
	static bool _ready;
	static IntPtr _class;
	static IntPtr _ctor;
	static IntPtr _setPeer;
	static IntPtr _first;
	static IntPtr _replace;
	static IntPtr _scroll;
	static IntPtr _setBindLog;
	static IntPtr _setRefresh;

	public static bool LogBinds { get; set; }

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate long SignalFn(IntPtr env, IntPtr clazz, long peer, int op, int arg);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void BindFn(IntPtr env, IntPtr clazz, long peer, int position, IntPtr title, IntPtr subtitle, IntPtr image);

	public static ViewGroup Create(Context context)
	{
		Ensure();
		var instance = JNIEnv.NewObject(_class, _ctor, new[] { new JValue(context.Handle) });
		var view = Java.Lang.Object.GetObject<ViewGroup>(instance, JniHandleOwnership.TransferLocalRef)
			?? throw new InvalidOperationException("KeyedList did not wrap.");
		var state = new State(view);
		Live.Add(view, state);
		JNIEnv.CallVoidMethod(view.Handle, _setPeer, new[] { new JValue(state.Peer) });
		if (LogBinds)
			JNIEnv.CallVoidMethod(view.Handle, _setBindLog, new[] { new JValue(true) });
		view.ContentDescription = "List";
		StartupMarks.AfterPresent(FirstFrame.Flush);
		StartupMarks.Phase("attach");
		return view;
	}

	public static bool Is(View view) => view is ViewGroup group && Live.TryGetValue(group, out _);

	public static void Bind(View view, IReadOnlyList<Element> rows)
	{
		if (view is not ViewGroup group || !Live.TryGetValue(group, out var state))
			throw new InvalidOperationException("List patch targeted a non-list view.");
		state.Bind(rows);
	}

	public static void SetRefresh(View view, bool enabled, Action? refresh)
	{
		if (view is not ViewGroup group || !Live.TryGetValue(group, out var state))
			return;
		state.Refresh = enabled ? refresh : null;
		JNIEnv.CallVoidMethod(view.Handle, _setRefresh, new[] { new JValue(enabled) });
	}

	static void Ensure()
	{
		if (_ready)
			return;
		var found = JNIEnv.FindClass("nativepoc/KeyedList");
		if (found == IntPtr.Zero)
			throw new InvalidOperationException("nativepoc.KeyedList was not in the dex.");
		// JNIEnv.FindClass returns a global ref. Deleting it as a local aborts the process.
		_class = JNIEnv.NewGlobalRef(found);
		if (_class == IntPtr.Zero)
			throw new InvalidOperationException("KeyedList class ref failed.");
		var klass = new JniObjectReference(_class, JniObjectReferenceType.Global);
		JniEnvironment.Types.RegisterNatives(klass, new[]
		{
			new JniNativeMethodRegistration("nativeSignal", "(JII)J", SignalSink),
			new JniNativeMethodRegistration("nativeBind", "(JILandroid/widget/TextView;Landroid/widget/TextView;Landroid/widget/ImageView;)V", BindSink),
		});
		_ctor = JNIEnv.GetMethodID(_class, "<init>", "(Landroid/content/Context;)V");
		_setPeer = JNIEnv.GetMethodID(_class, "setPeer", "(J)V");
		_first = JNIEnv.GetMethodID(_class, "first", "()I");
		_replace = JNIEnv.GetMethodID(_class, "replace", "()V");
		_scroll = JNIEnv.GetMethodID(_class, "scrollTo", "(I)V");
		_setBindLog = JNIEnv.GetMethodID(_class, "setBindLog", "(Z)V");
		_setRefresh = JNIEnv.GetMethodID(_class, "setRefresh", "(Z)V");

		if (_ctor == IntPtr.Zero || _setPeer == IntPtr.Zero || _first == IntPtr.Zero || _replace == IntPtr.Zero || _scroll == IntPtr.Zero || _setBindLog == IntPtr.Zero || _setRefresh == IntPtr.Zero)
			throw new InvalidOperationException("KeyedList is missing a Java method.");
		_ready = true;
	}

	static long OnSignal(IntPtr env, IntPtr clazz, long peer, int op, int arg)
	{
		try
		{
			var state = State.From(peer);
			if (state is null)
				return 0;
			switch (op)
			{
				case OpCount:
					return state.Rows.Count;
				case OpId:
					return state.Id(arg);
				case OpIdle:
					BindTimes.Log();
					state.Log(arg, arg, false);
					return 0;
				case OpReady:
					state.Log(arg, arg, false);
					StartupMarks.Phase("list");
					return 0;
				case OpSwipe:
					if ((uint)arg < (uint)state.Rows.Count)
					{
						var row = state.Rows[arg];
						Android.Util.Log.Info(MainActivity.Tag, "list-dismiss pos=" + arg.ToString(CultureInfo.InvariantCulture) + " key=" + state.KeyAt(arg));
						row.OnSwipe?.Invoke();
					}
					return 0;
				case OpRefresh:
					Android.Util.Log.Info(MainActivity.Tag, "list-refresh");
					state.Refresh?.Invoke();
					return 0;
				default:
					return 0;
			}
		}
		catch (Exception ex)
		{
			Android.Util.Log.Error(MainActivity.Tag, "list-signal " + ex.GetType().Name);
			return 0;
		}
	}

	static int _binds;

	static void OnBind(IntPtr env, IntPtr clazz, long peer, int position, IntPtr title, IntPtr subtitle, IntPtr image)
	{
		try
		{
			var state = State.From(peer);
			if (state is null || (uint)position >= (uint)state.Rows.Count)
				return;
			if (_binds == 0)
				StartupMarks.Phase("bind");
			_binds++;
			var timed = LogBinds;
			var mark = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			var row = state.Rows[position];
			var rowUs = timed ? BindTimes.Micros(mark) : 0;
			if (timed)
				mark = System.Diagnostics.Stopwatch.GetTimestamp();
			var face = RowFace.Read(row);
			var faceUs = timed ? BindTimes.Micros(mark) : 0;
			var titleView = Java.Lang.Object.GetObject<TextView>(title, JniHandleOwnership.DoNotTransfer);
			var subtitleView = Java.Lang.Object.GetObject<TextView>(subtitle, JniHandleOwnership.DoNotTransfer);
			var imageView = Java.Lang.Object.GetObject<ImageView>(image, JniHandleOwnership.DoNotTransfer);
			if (titleView is null || subtitleView is null || imageView is null)
				return;
			mark = System.Diagnostics.Stopwatch.GetTimestamp();
			RowText.Apply(titleView, face.Title);
			var description = face.Describe();
			if (description.Length > 0)
			{
				titleView.ContentDescription = description;
				if (titleView.Parent is View rowView)
					rowView.ContentDescription = description;
			}
			if (face.Subtitle.Length == 0)
			{
				if (subtitleView.Visibility != ViewStates.Gone)
					subtitleView.Visibility = ViewStates.Gone;
			}
			else
			{
				RowText.Apply(subtitleView, face.Subtitle);
				subtitleView.Visibility = ViewStates.Visible;
			}

			var textUs = BindTimes.Micros(mark);
			if (!StartupMarks.Presented)
			{
				FirstFrame.Note(textUs);
				FirstFrame.Defer(imageView, face.Picture);
			}
			else
			{
				var context = state.View.Context ?? throw new InvalidOperationException("List row has no context.");
				if (timed)
					mark = System.Diagnostics.Stopwatch.GetTimestamp();
				PictureBinder.Show(imageView, face.Picture, context);
				if (timed)
					BindTimes.Add(rowUs, faceUs, textUs, BindTimes.Micros(mark));
			}
		}
		catch (Exception ex)
		{
			Android.Util.Log.Error(MainActivity.Tag, "list-bind " + ex.GetType().Name);
		}
	}

	sealed class State
	{
		readonly GCHandle _handle;

		public State(ViewGroup view)
		{
			View = view;
			Rows = Array.Empty<Element>();
			_handle = GCHandle.Alloc(this);
		}

		public ViewGroup View { get; }

		public IReadOnlyList<Element> Rows { get; private set; }

		public Action? Refresh { get; set; }

		public long Peer => GCHandle.ToIntPtr(_handle).ToInt64();

		public static State? From(long peer)
		{
			if (peer == 0)
				return null;
			try
			{
				var handle = GCHandle.FromIntPtr((IntPtr)peer);
				return handle.Target as State;
			}
			catch (Exception)
			{
				return null;
			}
		}

		public void Bind(IReadOnlyList<Element> rows)
		{
			var saved = JNIEnv.CallIntMethod(View.Handle, _first);
			if (saved < 0)
				saved = 0;
			var anchor = "";
			if (saved > 0 && (uint)saved < (uint)Rows.Count)
				anchor = KeyAt(saved);
			Rows = rows;
			var keep = saved;
			if (anchor.Length > 0)
			{
				var found = IndexOf(anchor);
				if (found >= 0)
					keep = found;
			}
			if (rows.Count == 0)
				keep = 0;
			else if (keep >= rows.Count)
				keep = rows.Count - 1;
			JNIEnv.CallVoidMethod(View.Handle, _replace);
			var held = keep;
			View.Post(() => Hold(held));
		}

		int IndexOf(string key)
		{
			for (var index = 0; index < Rows.Count; index++)
			{
				if (KeyAt(index) == key)
					return index;
			}
			return -1;
		}

		public long Id(int index)
		{
			var key = KeyAt(index);
			ulong hash = 14695981039346656037;
			foreach (var ch in key)
			{
				hash ^= ch;
				hash *= 1099511628211;
			}

			return (long)hash;
		}

		public void Log(int first, int saved, bool restored)
		{
			var built = Rows switch
			{
				RecordProgram.WindowList window => window.Built,
				DeferredList deferred => deferred.Built,
				_ => Rows.Count,
			};
			var since = Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;
			Android.Util.Log.Info(
				MainActivity.Tag,
				"list-first=" + first.ToString(CultureInfo.InvariantCulture)
				+ " saved=" + saved.ToString(CultureInfo.InvariantCulture)
				+ " restored=" + (restored ? "1" : "0")
				+ " rows=" + Rows.Count.ToString(CultureInfo.InvariantCulture)
				+ " built=" + built.ToString(CultureInfo.InvariantCulture)
				+ " views=" + View.ChildCount.ToString(CultureInfo.InvariantCulture)
				+ " since-process-ms=" + since.ToString(CultureInfo.InvariantCulture));
		}

		void Hold(int saved)
		{
			StartupMarks.Phase("hold");
			var seen = JNIEnv.CallIntMethod(View.Handle, _first);
			if (seen < 0)
				seen = 0;
			var restored = saved > 0 && seen <= 0;
			if (restored)
				JNIEnv.CallVoidMethod(View.Handle, _scroll, new[] { new JValue(saved) });
			Log(restored ? saved : seen, saved, restored);
		}

		internal string KeyAt(int index)
		{
			if ((uint)index >= (uint)Rows.Count)
				return index.ToString(CultureInfo.InvariantCulture);
			if (Rows is DeferredList deferred)
				return deferred.KeyAt(index);
			if (Rows is RecordProgram.WindowList window)
			{
				for (var field = 0; field < 8; field++)
				{
					var cell = window.Cell(index, field);
					if (cell.Length > 0)
						return cell;
				}
			}
			else
			{
				var key = Rows[index].Key;
				if (key is { Length: > 0 })
					return key;
			}

			return index.ToString(CultureInfo.InvariantCulture);
		}
	}

	static class FirstFrame
	{
		static int _binds;
		static long _textUs;
		static bool _flushed;
		static readonly List<Pending> _images = new();

		readonly record struct Pending(ImageView Image, string Source);

		public static void Note(long textUs)
		{
			if (_flushed)
				return;
			_binds++;
			_textUs += textUs;
		}

		public static void Defer(ImageView image, string source)
		{
			if (_flushed)
			{
				var context = image.Context;
				if (context is not null)
					PictureBinder.Show(image, source, context);
				return;
			}

			_images.Add(new Pending(image, source));
		}

		public static void Flush()
		{
			if (_flushed)
				return;
			_flushed = true;
#if NATIVEPOC_STARTUP_MARKS
			var mark = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
			foreach (var pending in _images)
			{
				if (pending.Image.Handle == IntPtr.Zero)
					continue;
				var context = pending.Image.Context;
				if (context is null)
					continue;
				PictureBinder.Show(pending.Image, pending.Source, context);
			}

#if NATIVEPOC_STARTUP_MARKS
			StartupMarks.Frame(_binds, _textUs, BindTimes.Micros(mark));
#endif
			_images.Clear();
		}
	}

	static class BindTimes
	{
		const int Cap = 512;
		static int _seen;
		static int _kept;
		static readonly long[] _row = new long[Cap];
		static readonly long[] _face = new long[Cap];
		static readonly long[] _text = new long[Cap];
		static readonly long[] _image = new long[Cap];

		public static long Micros(long started)
		{
			var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
			return ticks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
		}

		public static void Add(long row, long face, long text, long image)
		{
			_seen++;
			if (_kept >= Cap)
				return;
			_row[_kept] = row;
			_face[_kept] = face;
			_text[_kept] = text;
			_image[_kept] = image;
			_kept++;
		}

		public static void Log()
		{
			if (!KeyedList.LogBinds || _kept == 0)
				return;
			Android.Util.Log.Info(
				MainActivity.Tag,
				"bind-part n=" + _seen.ToString(CultureInfo.InvariantCulture)
				+ " kept=" + _kept.ToString(CultureInfo.InvariantCulture)
				+ " row=" + Band(_row)
				+ " face=" + Band(_face)
				+ " text=" + Band(_text)
				+ " image=" + Band(_image)
				+ " unit=us");
		}

		static string Band(long[] source)
		{
			var copy = new long[_kept];
			Array.Copy(source, copy, _kept);
			Array.Sort(copy);
			return "p50:" + Percentile(copy, 0.50).ToString(CultureInfo.InvariantCulture)
				+ "/p90:" + Percentile(copy, 0.90).ToString(CultureInfo.InvariantCulture)
				+ "/max:" + copy[copy.Length - 1].ToString(CultureInfo.InvariantCulture);
		}

		static long Percentile(long[] sorted, double p)
		{
			var n = sorted.Length;
			var h = 1 + (n - 1) * p;
			var lo = (int)Math.Floor(h);
			var frac = h - lo;
			var hi = frac > 0 ? lo + 1 : lo;
			if (lo < 1)
				lo = 1;
			if (hi > n)
				hi = n;
			if (lo > n)
				lo = n;
			return (long)Math.Round(sorted[lo - 1] + frac * (sorted[hi - 1] - sorted[lo - 1]));
		}
	}

	static class RowText
	{
		const int Cap = 256;
		static readonly ConditionalWeakTable<TextView, string> Last = new();
		static readonly Dictionary<string, Java.Lang.String> Java = new(StringComparer.Ordinal);
		static readonly Queue<string> Order = new();

		public static void Apply(TextView view, string text)
		{
			if (Last.TryGetValue(view, out var previous) && string.Equals(previous, text, StringComparison.Ordinal))
				return;
			// One Java string for this text. A later bind of the same characters reuses it.
			// The row description is set by the caller. This only updates the visible text.
			view.TextFormatted = Cached(text);
			Last.AddOrUpdate(view, text);
		}

		static Java.Lang.String Cached(string text)
		{
			if (Java.TryGetValue(text, out var hit))
				return hit;
			var created = new Java.Lang.String(text);
			if (Java.Count >= Cap && Order.Count > 0)
			{
				var old = Order.Dequeue();
				if (Java.Remove(old, out var dropped))
					dropped.Dispose();
			}

			Java[text] = created;
			Order.Enqueue(text);
			return created;
		}
	}
}
#endif
