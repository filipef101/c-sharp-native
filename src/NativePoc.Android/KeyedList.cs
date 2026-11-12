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
		view.ContentDescription = "List";
		return view;
	}

	public static bool Is(View view) => view is ViewGroup group && Live.TryGetValue(group, out _);

	public static void Bind(View view, IReadOnlyList<Element> rows)
	{
		if (view is not ViewGroup group || !Live.TryGetValue(group, out var state))
			throw new InvalidOperationException("List patch targeted a non-list view.");
		state.Bind(rows);
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

		if (_ctor == IntPtr.Zero || _setPeer == IntPtr.Zero || _first == IntPtr.Zero || _replace == IntPtr.Zero || _scroll == IntPtr.Zero)
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
					state.Log(arg, arg, false);
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

	static void OnBind(IntPtr env, IntPtr clazz, long peer, int position, IntPtr title, IntPtr subtitle, IntPtr image)
	{
		try
		{
			var state = State.From(peer);
			if (state is null || (uint)position >= (uint)state.Rows.Count)
				return;
			var face = RowFace.Read(state.Rows[position]);
			var titleView = Java.Lang.Object.GetObject<TextView>(title, JniHandleOwnership.DoNotTransfer);
			var subtitleView = Java.Lang.Object.GetObject<TextView>(subtitle, JniHandleOwnership.DoNotTransfer);
			var imageView = Java.Lang.Object.GetObject<ImageView>(image, JniHandleOwnership.DoNotTransfer);
			if (titleView is null || subtitleView is null || imageView is null)
				return;
			titleView.Text = face.Title;
			titleView.ContentDescription = face.Title;
			if (face.Subtitle.Length == 0)
			{
				subtitleView.Text = "";
				subtitleView.Visibility = ViewStates.Gone;
			}
			else
			{
				subtitleView.Text = face.Subtitle;
				subtitleView.ContentDescription = face.Subtitle;
				subtitleView.Visibility = ViewStates.Visible;
			}

			var context = state.View.Context ?? throw new InvalidOperationException("List row has no context.");
			PictureBinder.Show(imageView, face.Picture, context);
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
			Rows = rows;
			JNIEnv.CallVoidMethod(View.Handle, _replace);
			var keep = saved;
			View.Post(() => Hold(keep));
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
			var built = Rows is RecordProgram.WindowList window ? window.Built : Rows.Count;
			Android.Util.Log.Info(
				MainActivity.Tag,
				"list-first=" + first.ToString(CultureInfo.InvariantCulture)
				+ " saved=" + saved.ToString(CultureInfo.InvariantCulture)
				+ " restored=" + (restored ? "1" : "0")
				+ " rows=" + Rows.Count.ToString(CultureInfo.InvariantCulture)
				+ " built=" + built.ToString(CultureInfo.InvariantCulture)
				+ " views=" + View.ChildCount.ToString(CultureInfo.InvariantCulture));
		}

		void Hold(int saved)
		{
			var seen = JNIEnv.CallIntMethod(View.Handle, _first);
			if (seen < 0)
				seen = 0;
			var restored = saved > 0 && seen <= 0;
			if (restored)
				JNIEnv.CallVoidMethod(View.Handle, _scroll, new[] { new JValue(saved) });
			Log(restored ? saved : seen, saved, restored);
		}

		string KeyAt(int index)
		{
			if ((uint)index >= (uint)Rows.Count)
				return index.ToString(CultureInfo.InvariantCulture);
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
}
#endif
