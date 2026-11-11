using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;
using AndroidScroll = Android.Widget.ScrollView;
using AndroidSwitch = Android.Widget.Switch;

namespace NativePoc;

sealed class AndroidHost : IElementHost<View>
{
	readonly Activity _activity;
	readonly Func<Element?> _current;
	readonly Dictionary<View, Listener> _listeners = new();
	bool _applyingText;
	bool _applyingSwitch;

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
				return new AndroidField(_activity);
			case ElementKind.Switch:
				return new AndroidSwitch(_activity);
			case ElementKind.Scroll:
				return new AndroidScroll(_activity)
				{
					FillViewport = true,
				};
			case ElementKind.List:
				return new RowWindow(_activity);
			case ElementKind.Image:
				return new ImageView(_activity)
				{
					LayoutParameters = new ViewGroup.LayoutParams(Dp(24), Dp(24)),
				};
			default:
				throw UnknownKind(kind);
		}
	}

	public void Insert(View parent, int index, View child)
	{
		if (parent is not ViewGroup group)
			throw new InvalidOperationException("Parent is not a view group.");
		child.LayoutParameters = parent is AndroidScroll
			? new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
			: new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
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
		text.ContentDescription = value;
	}

	public void SetButton(View view, string title, string accessibilityLabel)
	{
		if (view is not AndroidButton button)
			throw new InvalidOperationException("Button patch targeted a non-button view.");
		button.Text = title;
		button.ContentDescription = accessibilityLabel;
	}

	public void RestoreFocus(View root) => FieldFocus.Restore(root);

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
		FieldFocus.Frozen = true;
		_applyingText = true;
		try
		{
			edit.Text = value;
		}
		finally
		{
			_applyingText = false;
			FieldFocus.Frozen = false;
		}
	}

	public void SetStyle(View view, Style style)
	{
		var density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
		int Px(int dp) => (int)(dp * density);
		if (style.Padding != 0)
		{
			var pad = Px(style.Padding);
			view.SetPadding(pad, pad, pad, pad);
		}

		if (view.LayoutParameters is ViewGroup.MarginLayoutParams margin)
		{
			if (style.Margin != 0)
			{
				var edge = Px(style.Margin);
				margin.SetMargins(edge, edge, edge, edge);
			}

			if (margin is LinearLayout.LayoutParams linear && style.Grow > 0)
			{
				linear.Height = 0;
				linear.Weight = style.Grow;
			}

			view.LayoutParameters = margin;
		}

		if (view is LinearLayout row)
		{
			if (style.Gap > 0)
			{
				var gap = Px(style.Gap);
				var divider = new GradientDrawable();
				divider.SetColor(Android.Graphics.Color.Transparent);
				divider.SetSize(gap, gap);
				row.SetDividerDrawable(divider);
				row.ShowDividers = ShowDividers.Middle;
			}

			row.SetGravity(style.Align switch
			{
				Align.Center => GravityFlags.CenterHorizontal,
				Align.End => GravityFlags.End,
				Align.Stretch => GravityFlags.FillHorizontal,
				Align.Start => GravityFlags.Start,
				_ => throw new InvalidOperationException("Unknown alignment."),
			});
		}

		if (view is TextView text)
		{
			text.Gravity = style.Align switch
			{
				Align.Center => GravityFlags.CenterHorizontal,
				Align.End => GravityFlags.End,
				Align.Stretch => GravityFlags.FillHorizontal,
				Align.Start => GravityFlags.Start,
				_ => throw new InvalidOperationException("Unknown alignment."),
			};
			if (style.FontSize > 0)
				text.SetTextSize(ComplexUnitType.Sp, style.FontSize);
			if (style.Weight == FontWeight.Bold)
				text.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
			if (style.Color != 0)
				text.SetTextColor(new Android.Graphics.Color(unchecked((int)style.Color)));
		}

		if (style.Background != 0 || style.Radius > 0)
		{
			var drawable = new GradientDrawable();
			drawable.SetCornerRadius(Px(style.Radius));
			drawable.SetColor(style.Background == 0
				? Android.Graphics.Color.Transparent
				: new Android.Graphics.Color(unchecked((int)style.Background)));
			view.Background = drawable;
		}
	}

	public void SetImage(View view, string source)
	{
		if (view is not ImageView image)
			throw new InvalidOperationException("Image patch targeted a non-image view.");
		var id = _activity.Resources?.GetIdentifier(source, "drawable", _activity.PackageName) ?? 0;
		if (id == 0)
			throw new InvalidOperationException("Drawable " + source + " was not found.");
		image.SetImageResource(id);
		image.ContentDescription = source;
		if (image.LayoutParameters is ViewGroup.LayoutParams layout)
		{
			layout.Width = Dp(24);
			layout.Height = Dp(24);
			image.LayoutParameters = layout;
		}
	}

	int Dp(int value)
	{
		var density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
		return (int)(value * density);
	}

	public void SetList(View view, IReadOnlyList<Element> rows)
	{
		if (view is not RowWindow window)
			throw new InvalidOperationException("List patch targeted a non-list view.");
		window.Bind(rows);
	}

	public void SetSwitch(View view, bool value, string accessibilityLabel)
	{
		if (view is not AndroidSwitch toggle)
			throw new InvalidOperationException("Switch patch targeted a non-switch view.");
		toggle.ContentDescription = accessibilityLabel;
		if (toggle.Checked == value)
			return;
		_applyingSwitch = true;
		try
		{
			toggle.Checked = value;
		}
		finally
		{
			_applyingSwitch = false;
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
		if (view.Parent is ViewGroup group)
			group.RemoveView(view);
		view.Dispose();
	}

	void Attach(NodePath path, View view, bool logMount)
	{
		Detach(view);
		switch (Slot(view))
		{
			case HostInput.Button:
				AttachButton(path, view, logMount);
				return;
			case HostInput.TextField:
				AttachField(path, view);
				return;
			case HostInput.Switch:
				AttachSwitch(path, view, logMount);
				return;
			case HostInput.None:
				return;
			default:
				throw UnknownInput();
		}
	}

	void AttachButton(NodePath path, View view, bool logMount)
	{
		if (view is not AndroidButton button)
			return;
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
	}

	void AttachField(NodePath path, View view)
	{
		if (view is not EditText edit)
			return;
		EventHandler<TextChangedEventArgs> handler = (_, args) =>
		{
			if (_applyingText)
				return;
			if (CurrentElement(path) is TextField field)
				field.OnChange(args.Text?.ToString() ?? "");
		};
		edit.TextChanged += handler;
		_listeners[view] = new Listener { TextChanged = handler };
		if (edit is AndroidField field)
		{
			field.Key = CurrentElement(path)?.Key;
			field.FocusChange += (_, args) =>
			{
				if (!args.HasFocus || field.Key is null)
					return;
				FieldFocus.Note(field.Key, field.SelectionStart, field.SelectionEnd);
			};
		}
	}

	void AttachSwitch(NodePath path, View view, bool logMount)
	{
		if (view is not AndroidSwitch toggle)
			return;
		EventHandler<CompoundButton.CheckedChangeEventArgs> handler = (_, args) =>
		{
			if (_applyingSwitch)
				return;
			if (CurrentElement(path) is Switch current)
				current.OnChange(args.IsChecked);
		};
		toggle.CheckedChange += handler;
		_listeners[view] = new Listener { Checked = handler };
		if (logMount)
		{
			Log.Info(
				MainActivity.Tag,
				"switch-subscribe handle=" + toggle.Handle.ToInt64());
		}
	}

	void Detach(View view)
	{
		if (!_listeners.Remove(view, out var listener))
			return;
		switch (Slot(view))
		{
			case HostInput.Button:
				if (listener.Click is not null && view is AndroidButton button)
					button.Click -= listener.Click;
				return;
			case HostInput.TextField:
				if (listener.TextChanged is not null && view is EditText edit)
					edit.TextChanged -= listener.TextChanged;
				return;
			case HostInput.Switch:
				if (listener.Checked is not null && view is AndroidSwitch toggle)
				{
					toggle.CheckedChange -= listener.Checked;
					Log.Info(MainActivity.Tag, "switch-unsubscribe handle=" + toggle.Handle.ToInt64());
				}
				return;
			case HostInput.None:
				return;
			default:
				throw UnknownInput();
		}
	}

	static HostInput Slot(View view) =>
		HostDispatch.Choose(view.GetType(), typeof(AndroidButton), typeof(EditText), typeof(AndroidSwitch));

	static InvalidOperationException UnknownInput() =>
		new("Unknown host input.");

	sealed class Listener
	{
		public EventHandler? Click { get; init; }

		public EventHandler<TextChangedEventArgs>? TextChanged { get; init; }

		public EventHandler<CompoundButton.CheckedChangeEventArgs>? Checked { get; init; }
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

sealed class AndroidField : EditText
{
	public string? Key { get; set; }

	public AndroidField(Context context) : base(context)
	{
	}

	protected override void OnSelectionChanged(int selStart, int selEnd)
	{
		base.OnSelectionChanged(selStart, selEnd);
		if (FieldFocus.Frozen || Key is null)
			return;
		FieldFocus.Note(Key, selStart, selEnd);
	}
}

static class FieldFocus
{
	public static bool Frozen { get; set; }

	static string? _key;
	static int _start;
	static int _end;

	public static void Note(string key, int start, int end)
	{
		_key = key;
		_start = start;
		_end = end;
		Log.Info(MainActivity.Tag, "field-note key=" + key + " start=" + start + " end=" + end);
	}

	public static void Restore(View root)
	{
		if (_key is null)
			return;
		var field = Find(root, _key);
		if (field is null)
			return;
		var length = field.Text?.Length ?? 0;
		var start = Math.Clamp(_start, 0, length);
		var end = Math.Clamp(_end, 0, length);
		Log.Info(MainActivity.Tag, "field-focus key=" + _key + " start=" + start + " end=" + end);
		Frozen = true;
		try
		{
			field.RequestFocus();
			field.SetSelection(Math.Min(start, end), Math.Max(start, end));
		}
		finally
		{
			Frozen = false;
		}
	}

	static AndroidField? Find(View view, string key)
	{
		if (view is AndroidField field && field.Key == key)
			return field;
		if (view is not ViewGroup group)
			return null;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is null)
				continue;
			var found = Find(child, key);
			if (found is not null)
				return found;
		}

		return null;
	}
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

	public void Push(string screen) => ScreenNav.Shared.Push(screen);

	public void Pop() => ScreenNav.Shared.Pop();

	public void Effect(string property, string resource) =>
		EffectRuntime.Start(_activity, property, resource, () => _activity.RequestRender());

	public void Every(string property) => ClockRuntime.Arm(property);
}
