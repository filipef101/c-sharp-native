using Android.Content;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;

namespace NativePoc;

sealed class AndroidHost : IElementHost<View>
{
	readonly Activity _activity;
	readonly Func<Element?> _current;
	readonly Dictionary<View, Listener> _listeners = new();
	bool _applyingText;

	public AndroidHost(Activity activity, Func<Element?> current)
	{
		_activity = activity;
		_current = current;
	}

	public View Create(ElementKind kind)
	{
		switch (kind)
		{
			case ElementKind.Column:
				return new LinearLayout(_activity)
				{
					Orientation = Orientation.Vertical,
				};
			case ElementKind.Text:
				return new TextView(_activity);
			case ElementKind.Button:
				return new AndroidButton(_activity);
			case ElementKind.TextField:
				return new EditText(_activity);
			default:
				throw UnknownKind(kind);
		}
	}

	public void Insert(View parent, int index, View child)
	{
		if (parent is not ViewGroup group)
			throw new InvalidOperationException("Parent is not a view group.");
		child.LayoutParameters = new LinearLayout.LayoutParams(
			ViewGroup.LayoutParams.MatchParent,
			ViewGroup.LayoutParams.WrapContent);
		group.AddView(child, index);
	}

	public void Remove(View parent, int index)
	{
		if (parent is not ViewGroup group)
			throw new InvalidOperationException("Parent is not a view group.");
		group.RemoveViewAt(index);
	}

	public void SetText(View view, string value)
	{
		if (view is not TextView text)
			throw new InvalidOperationException("Text patch targeted a non-text view.");
		text.Text = value;
	}

	public void SetButton(View view, string title, string accessibilityLabel)
	{
		if (view is not AndroidButton button)
			throw new InvalidOperationException("Button patch targeted a non-button view.");
		button.Text = title;
		button.ContentDescription = accessibilityLabel;
	}

	public void SetTextField(View view, string value, string placeholder, string accessibilityLabel)
	{
		if (view is not EditText edit)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		Log.Info(MainActivity.Tag, "field-handle " + edit.Handle.ToInt64());
		if (!string.Equals(edit.Hint, placeholder, StringComparison.Ordinal))
			edit.Hint = placeholder;
		edit.ContentDescription = accessibilityLabel;
		var current = edit.Text ?? "";
		if (string.Equals(current, value, StringComparison.Ordinal))
			return;
		_applyingText = true;
		try
		{
			edit.Text = value;
		}
		finally
		{
			_applyingText = false;
		}
	}

	public void OnMounted(NodePath path, View view)
	{
		if (path.Length == 0)
		{
			var pad = (int)(16 * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));
			view.SetPadding(pad, pad, pad, pad);
			view.LayoutParameters = new ViewGroup.LayoutParams(
				ViewGroup.LayoutParams.MatchParent,
				ViewGroup.LayoutParams.MatchParent);
		}

		Attach(path, view, logMount: true);
	}

	public void Retarget(NodePath path, View view)
	{
		if (!_listeners.ContainsKey(view))
			return;
		Attach(path, view, logMount: false);
	}

	public void OnUnmounted(NodePath path, View view)
	{
		Detach(view);
		view.Dispose();
	}

	void Attach(NodePath path, View view, bool logMount)
	{
		Detach(view);
		if (view is AndroidButton button)
		{
			EventHandler handler = (_, _) =>
			{
				if (CurrentElement(path) is Button current)
					current.OnPress();
			};
			button.Click += handler;
			_listeners[view] = new Listener { Click = handler };
			if (logMount)
			{
				Log.Info(
					MainActivity.Tag,
					"button-mounted handle=" + button.Handle.ToInt64() + " tick=" + Environment.TickCount64);
			}
			return;
		}

		if (view is EditText edit)
		{
			EventHandler<TextChangedEventArgs> handler = (_, args) =>
			{
				if (_applyingText)
					return;
				if (CurrentElement(path) is TextField field)
					field.OnChange(args.Text?.ToString() ?? "");
			};
			edit.TextChanged += handler;
			_listeners[view] = new Listener { TextChanged = handler };
		}
	}

	void Detach(View view)
	{
		if (!_listeners.Remove(view, out var listener))
			return;
		if (view is AndroidButton button && listener.Click is not null)
			button.Click -= listener.Click;
		if (view is EditText edit && listener.TextChanged is not null)
			edit.TextChanged -= listener.TextChanged;
	}

	sealed class Listener
	{
		public EventHandler? Click { get; init; }

		public EventHandler<TextChangedEventArgs>? TextChanged { get; init; }
	}

	Element? CurrentElement(NodePath path)
	{
		var root = _current();
		if (root is null)
			return null;
		return TreeDiff.At(root, path);
	}

	static InvalidOperationException UnknownKind(ElementKind kind) =>
		new("Unknown element kind.");
}

sealed class PlatformBridge : IPlatform
{
	readonly MainActivity _activity;

	public PlatformBridge(MainActivity activity)
	{
		_activity = activity;
	}

	public void CopyNonce(string nonce)
	{
		var clipboard = _activity.GetSystemService(Context.ClipboardService) as Android.Content.ClipboardManager;
		if (clipboard is null)
			throw new InvalidOperationException("Clipboard service is unavailable.");
		clipboard.PrimaryClip = ClipData.NewPlainText("nonce", nonce);
		var read = clipboard.PrimaryClip?.GetItemAt(0)?.Text?.ToString() ?? "";
		if (!string.Equals(read, nonce, StringComparison.Ordinal))
			throw new InvalidOperationException("Clipboard round-trip failed.");
		Log.Info(MainActivity.Tag, "clipboard-ok " + nonce);
	}

	public void RequestRender() => _activity.RequestRender();
}
