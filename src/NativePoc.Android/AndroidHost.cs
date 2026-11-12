using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Views.InputMethods;
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
	readonly Dictionary<View, NodePath> _paths = new();
	readonly HashSet<View> _gone = new();
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
				return new GestureColumn(_activity)
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
				return new RefreshScroll(_activity)
				{
					FillViewport = true,
				};
			case ElementKind.List:
#if NATIVEPOC_RECYCLER
				return KeyedList.Create(_activity);
#else
				return new RowWindow(_activity);
#endif
			case ElementKind.Image:
				return new ImageView(_activity)
				{
					LayoutParameters = new ViewGroup.LayoutParams(Dp(48), Dp(48)),
				};
			case ElementKind.Host:
				return new HostSlot(_activity);
			default:
				throw UnknownKind(kind);
		}
	}

	public void Insert(View parent, int index, View child)
	{
		if (parent is not ViewGroup group)
			throw new InvalidOperationException("Parent is not a view group.");
#if NATIVEPOC_RECYCLER
		if (KeyedList.Is(child) && parent is LinearLayout)
		{
			child.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f);
			group.AddView(child, index);
			return;
		}
#endif
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
		text.Visibility = value.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
	}

	public void SetButton(View view, string title, string accessibilityLabel)
	{
		if (view is not AndroidButton button)
			throw new InvalidOperationException("Button patch targeted a non-button view.");
		button.Text = title;
		button.ContentDescription = accessibilityLabel;
		TouchTarget(button);
	}

	public void RestoreFocus(View root) => FieldFocus.Restore(root);

	public void SetFieldMode(View view, Keyboard keyboard, int lines, Submit submit)
	{
		if (view is not EditText edit)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		var input = keyboard switch
		{
			Keyboard.Text => InputTypes.ClassText,
			Keyboard.Email => InputTypes.ClassText | InputTypes.TextVariationEmailAddress,
			Keyboard.Number => InputTypes.ClassNumber,
			Keyboard.Phone => InputTypes.ClassPhone,
			Keyboard.Password => InputTypes.ClassText | InputTypes.TextVariationPassword,
			_ => throw new InvalidOperationException("Unknown keyboard."),
		};
		var multiline = lines > 1 && keyboard != Keyboard.Password;
		if (multiline)
		{
			edit.SetSingleLine(false);
			edit.SetMinLines(lines);
			edit.Gravity = GravityFlags.Top;
			input |= InputTypes.TextFlagMultiLine;
		}
		else
			edit.SetSingleLine(true);
		edit.InputType = input;
		edit.ImeOptions = submit switch
		{
			Submit.Next => ImeAction.Next,
			Submit.Send => ImeAction.Send,
			Submit.Go => ImeAction.Go,
			Submit.Search => ImeAction.Search,
			Submit.None => ImeAction.Done,
			_ => throw new InvalidOperationException("Unknown submit action."),
		};
		var key = edit is AndroidField marked ? marked.Key ?? "" : "";
		Log.Info(MainActivity.Tag, "field-mode key=" + key + " keyboard=" + keyboard + " lines=" + lines + " submit=" + submit);
	}

	public void SetTextField(View view, string value, string placeholder, string accessibilityLabel)
	{
		if (view is not EditText edit)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		Log.Info(MainActivity.Tag, "field-handle " + edit.Handle.ToInt64());
		if (!string.Equals(edit.Hint, placeholder, StringComparison.Ordinal))
			edit.Hint = placeholder;
		edit.ContentDescription = accessibilityLabel;
		TouchTarget(edit);
		var current = edit.Text ?? "";
		if (string.Equals(current, value, StringComparison.Ordinal))
			return;
		FieldFocus.Frozen = true;
		_applyingText = true;
		try
		{
			edit.Text = value;
			if (edit is AndroidField marked && marked.Key is not null)
			{
				var placed = edit.Text?.Length ?? 0;
				int start;
				int end;
				if (current.Length == 0 && placed > 0)
				{
					start = placed;
					end = placed;
				}
				else if (!FieldFocus.TryRead(marked.Key, out start, out end))
				{
					start = edit.SelectionStart;
					end = edit.SelectionEnd;
				}

				start = Math.Clamp(start, 0, placed);
				end = Math.Clamp(end, 0, placed);
				if (edit.SelectionStart != start || edit.SelectionEnd != end)
					edit.SetSelection(Math.Min(start, end), Math.Max(start, end));
			}
		}
		finally
		{
			_applyingText = false;
			FieldFocus.Frozen = false;
		}

		if (edit is AndroidField noted && noted.Key is not null)
			FieldFocus.Note(noted.Key, noted.SelectionStart, noted.SelectionEnd);
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
			if (style.FontSize >= 20 && (int)Android.OS.Build.VERSION.SdkInt >= 28)
			{
#pragma warning disable CA1416
				text.AccessibilityHeading = true;
#pragma warning restore CA1416
				text.SetAccessibilityDelegate(HeadingMark.Instance);
			}
			if (style.Weight == FontWeight.Bold)
				text.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
			ThemeRuntime.NoteColor(view, style.Color);
			text.SetTextColor(style.Color == 0
				? new Android.Graphics.Color(unchecked((int)ThemeTokens.Text(ThemeRuntime.IsDark(_activity))))
				: new Android.Graphics.Color(unchecked((int)style.Color)));
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

	public void SetMotion(View view, Motion motion)
	{
		var density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
		var key = "";
		if (_paths.TryGetValue(view, out var path))
			key = CurrentElement(path)?.Key ?? "";
		MotionPlayer.Remember(key, view, motion, density);
		MotionPlayer.Play(view, motion, density, key);
	}

	public void SetGesture(View view, byte mask)
	{
		if (mask != 0)
			TouchTarget(view);
		if (!_paths.TryGetValue(view, out var path))
			return;
		BindGesture(view, path, mask);
	}

	public void SetHost(View view, string kind, string prop, string value, string eventName, Action<string, string>? onEvent)
	{
		if (view is not HostSlot slot)
			throw new InvalidOperationException("Host patch targeted a non-host view.");
		slot.Apply(kind, prop, value, eventName, onEvent);
	}

	public void SetImage(View view, string source)
	{
		if (view is not ImageView image)
			throw new InvalidOperationException("Image patch targeted a non-image view.");
		if (source.Length == 0)
		{
			image.SetImageDrawable(null);
			image.ContentDescription = null;
			image.Visibility = ViewStates.Gone;
			return;
		}

		PictureBinder.Show(image, source, _activity);
		TouchTarget(image);
		if (image.LayoutParameters is ViewGroup.LayoutParams layout)
		{
			layout.Width = Dp(48);
			layout.Height = Dp(48);
			image.LayoutParameters = layout;
		}
	}

	void TouchTarget(View view)
	{
		var min = Dp(48);
		view.SetMinimumWidth(min);
		view.SetMinimumHeight(min);
	}

	int Dp(int value)
	{
		var density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
		return (int)(value * density);
	}

	public void SetList(View view, IReadOnlyList<Element> rows)
	{
#if NATIVEPOC_RECYCLER
		if (KeyedList.Is(view))
		{
			KeyedList.Bind(view, rows);
			return;
		}
#endif
		if (view is not RowWindow window)
			throw new InvalidOperationException("List patch targeted a non-list view.");
		window.Bind(rows);
	}

	public void SetSwitch(View view, bool value, string accessibilityLabel)
	{
		if (view is not AndroidSwitch toggle)
			throw new InvalidOperationException("Switch patch targeted a non-switch view.");
		toggle.ContentDescription = accessibilityLabel;
		TouchTarget(toggle);
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
		_paths[view] = path;
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
		_paths[view] = path;
		if (_listeners.ContainsKey(view))
			Attach(path, view, logMount: false);
		if (TryElement(path) is { } element && element.Gesture != 0)
			BindGesture(view, path, element.Gesture);
	}

	public void OnUnmounted(NodePath path, View view)
	{
		MotionPlayer.Stop(view);
		Detach(view);
		_paths.Remove(view);
		var tracking = view is GestureColumn column && column.Tracking
			|| view is RefreshScroll scroll && scroll.Tracking
			|| view is RowWindow window && window.Tracking;
		if (tracking)
		{
			void Done() => Finish(view);
			if (view is GestureColumn gesture)
				gesture.OnReleased = Done;
			else if (view is RefreshScroll refresh)
				refresh.OnReleased = Done;
			else if (view is RowWindow rows)
				rows.OnReleased = Done;
			view.Post(() => Finish(view));
			return;
		}

		Finish(view);
	}

	void Finish(View view)
	{
		if (!_gone.Add(view))
			return;
		if (view.Handle == IntPtr.Zero)
			return;
		if (view.Parent is ViewGroup group && group.Handle != IntPtr.Zero)
			group.RemoveView(view);
		// Dispose after this traversal. Doing it here drops a parent that the
		// unmount is still walking, and the next screen hits a disposed column.
		var decor = _activity.Window?.DecorView;
		if (decor is null || decor.Handle == IntPtr.Zero)
		{
			view.Dispose();
			return;
		}

		decor.Post(() =>
		{
			if (view.Handle != IntPtr.Zero)
				view.Dispose();
		});
	}

	void BindGesture(View view, NodePath path, byte mask)
	{
		if (view is GestureColumn column)
		{
			column.Configure(
				mask,
				(mask & Gestures.Tap) == 0 ? null : () => Fire(path, Gestures.Tap),
				(mask & Gestures.LongPress) == 0 ? null : () => Fire(path, Gestures.LongPress),
				(mask & Gestures.Swipe) == 0 ? null : () => Fire(path, Gestures.Swipe));
			return;
		}

		if (view is RefreshScroll scroll)
		{
			scroll.Configure((mask & Gestures.Refresh) != 0, (mask & Gestures.Refresh) == 0 ? null : () => Fire(path, Gestures.Refresh));
			return;
		}

		if (view is RowWindow window)
		{
			window.ConfigureRefresh((mask & Gestures.Refresh) != 0, (mask & Gestures.Refresh) == 0 ? null : () => Fire(path, Gestures.Refresh));
			return;
		}

#if NATIVEPOC_RECYCLER
		if (KeyedList.Is(view))
		{
			KeyedList.SetRefresh(view, (mask & Gestures.Refresh) != 0, (mask & Gestures.Refresh) == 0 ? null : () => Fire(path, Gestures.Refresh));
			KeyedList.SetUndo(view, (mask & Gestures.Undo) != 0, (mask & Gestures.Undo) == 0 ? null : () => Fire(path, Gestures.Undo));
			return;
		}
#endif

		if (view is TextView text && view is not AndroidButton && view is not EditText)
		{
			text.Clickable = (mask & Gestures.Tap) != 0;
			text.LongClickable = (mask & Gestures.LongPress) != 0;
			text.SetOnClickListener((mask & Gestures.Tap) == 0 ? null : new TapClick(() => Fire(path, Gestures.Tap)));
			text.SetOnLongClickListener((mask & Gestures.LongPress) == 0 ? null : new LongClick(() => Fire(path, Gestures.LongPress)));
			if ((mask & (Gestures.Tap | Gestures.LongPress)) != 0)
				text.SetAccessibilityDelegate(ButtonRole.Instance);
		}
	}

	void Fire(NodePath path, byte kind)
	{
		if (TryElement(path) is not { } element)
			return;
		var action = kind switch
		{
			Gestures.Tap => element.OnTap,
			Gestures.LongPress => element.OnLongPress,
			Gestures.Swipe => element.OnSwipe,
			Gestures.Refresh => element.OnRefresh,
			Gestures.Undo => element.OnUndo,
			_ => null,
		};
		if (action is null)
			return;
#if DEBUG
		Guard(element, action);
#else
		action();
#endif
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

#if DEBUG
	static void Guard(Element element, System.Action call)
	{
		try
		{
			call();
		}
		catch (Exception ex)
		{
			var span = Span(element);
			if (MainActivity.Current is { } activity)
				DevOverlay.Show(activity, "handler", ex.Message, span);
			Log.Info(MainActivity.Tag, "handler-fault span=" + span + " " + ex.GetType().Name);
		}
	}

	static string Span(Element element)
	{
		var (file, line) = element switch
		{
			Button button => (button.SourceFile, button.SourceLine),
			TextField field => (field.SourceFile, field.SourceLine),
			TextInput input => (input.SourceFile, input.SourceLine),
			Switch toggle => (toggle.SourceFile, toggle.SourceLine),
			_ => ("", 0),
		};
		if (file.Length == 0 || file.Contains("RecordProgram.cs", StringComparison.Ordinal))
			return "screen";
		var cut = file.LastIndexOf("/src/", StringComparison.Ordinal);
		if (cut < 0)
			cut = file.LastIndexOf("/samples/", StringComparison.Ordinal);
		var shown = cut >= 0 ? file[(cut + 1)..] : System.IO.Path.GetFileName(file);
		return line > 0 ? shown + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture) : shown;
	}
#endif

	void AttachButton(NodePath path, View view, bool logMount)
	{
		if (view is not AndroidButton button)
			return;
		EventHandler handler = (_, _) =>
		{
			if (CurrentElement(path) is Button current)
			{
				TapTrace.Input();
#if DEBUG
				Guard(current, current.OnPress);
#else
				current.OnPress();
#endif
			}
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
			if (edit is AndroidField marked && marked.Key is not null && !FieldFocus.Frozen)
				FieldFocus.Note(marked.Key, marked.SelectionStart, marked.SelectionEnd);
			var text = args.Text?.ToString() ?? "";
			switch (CurrentElement(path))
			{
				case TextField field:
#if DEBUG
					Guard(field, () => field.OnChange(text));
#else
					field.OnChange(text);
#endif
					break;
				case TextInput input:
#if DEBUG
					Guard(input, () => input.OnChange(text));
#else
					input.OnChange(text);
#endif
					break;
				default:
					break;
			}
		};
		edit.TextChanged += handler;
		EventHandler<TextView.EditorActionEventArgs> editor = (_, args) =>
		{
			var current = CurrentElement(path);
			var submit = current switch
			{
				TextField field => field.OnSubmit,
				TextInput input => input.OnSubmit,
				_ => null,
			};
			var kind = current switch
			{
				TextField field => field.Submit,
				TextInput input => input.Submit,
				_ => Submit.None,
			};
			if (submit is null || current is not (TextField or TextInput))
				return;
			var expected = kind switch
			{
				Submit.Next => ImeAction.Next,
				Submit.Send => ImeAction.Send,
				Submit.Go => ImeAction.Go,
				Submit.Search => ImeAction.Search,
				Submit.None => ImeAction.Done,
				_ => throw new InvalidOperationException("Unknown submit action."),
			};
			if (args.ActionId != expected)
				return;
			if (args.Event is { Action: not KeyEventActions.Down })
				return;
			if (edit is AndroidField gate)
			{
				var now = Environment.TickCount64;
				if (now - gate.LastSubmit < 250)
					return;
				gate.LastSubmit = now;
			}
#if DEBUG
			Guard(current, submit);
#else
			submit();
#endif
			args.Handled = true;
		};
		edit.EditorAction += editor;
		_listeners[view] = new Listener { TextChanged = handler, Editor = editor };
		if (edit is AndroidField field)
		{
			field.Key = CurrentElement(path)?.Key;
			field.FocusChange += (_, args) =>
			{
			if (!args.HasFocus || field.Key is null || FieldFocus.Frozen)
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
			{
#if DEBUG
				var flag = args.IsChecked;
				Guard(current, () => current.OnChange(flag));
#else
				current.OnChange(args.IsChecked);
#endif
			}
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
				if (view is EditText edit)
				{
					if (listener.TextChanged is not null)
						edit.TextChanged -= listener.TextChanged;
					if (listener.Editor is not null)
						edit.EditorAction -= listener.Editor;
				}
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

		public EventHandler<TextView.EditorActionEventArgs>? Editor { get; init; }

		public EventHandler<CompoundButton.CheckedChangeEventArgs>? Checked { get; init; }
	}

	Element? CurrentElement(NodePath path) => TryElement(path);

	Element? TryElement(NodePath path)
	{
		var root = _current();
		if (root is null)
			return null;
		try
		{
			return TreeDiff.At(root, path);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	static InvalidOperationException UnknownKind(ElementKind kind) =>
		new("Unknown element kind.");
}

sealed class AndroidField : EditText
{
	static bool _loggedFocus;

	public string? Key { get; set; }

	public long LastSubmit { get; set; }

	public AndroidField(Context context) : base(context)
	{
		FocusableInTouchMode = false;
		if (OperatingSystem.IsAndroidVersionAtLeast(26))
			ImportantForAutofill = ImportantForAutofill.No;
		ViewTreeObserver?.AddOnPreDrawListener(new TouchFocus(this));
	}

	sealed class TouchFocus : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
	{
		readonly AndroidField _field;
		bool _done;

		public TouchFocus(AndroidField field) => _field = field;

		public bool OnPreDraw()
		{
			if (_done)
				return true;
			_done = true;
			var focused = _field.IsFocused;
			_field.FocusableInTouchMode = true;
			if (OperatingSystem.IsAndroidVersionAtLeast(26))
				_field.ImportantForAutofill = ImportantForAutofill.Auto;
			_field.ViewTreeObserver?.RemoveOnPreDrawListener(this);
			if (_loggedFocus)
				return true;
			_loggedFocus = true;
			Log.Info(
				MainActivity.Tag,
				"startup field-focus focused=" + (focused ? "1" : "0"));
			return true;
		}
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

	public static bool TryRead(string key, out int start, out int end)
	{
		if (_key != key)
		{
			start = 0;
			end = 0;
			return false;
		}

		start = _start;
		end = _end;
		return true;
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
			if (!field.IsFocused)
				field.RequestFocus();
			var nextStart = Math.Min(start, end);
			var nextEnd = Math.Max(start, end);
			if (field.SelectionStart != nextStart || field.SelectionEnd != nextEnd)
				field.SetSelection(nextStart, nextEnd);
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
		_live = _calls;
	}

	public void CopyNonce(string nonce)
	{
		AndroidNativeServices.CopyChecked(_activity, "nonce", nonce);
	}

	public void RequestRender() => _activity.RequestRender();

	public void Push(string screen)
	{
		var before = ScreenNav.Shared.TopId();
		var depth = ScreenNav.Shared.Depth;
		if (!ScreenNav.Shared.Push(screen))
			Log.Info(MainActivity.Tag, "nav-miss " + screen);
		else if (ScreenNav.Shared.TopId() != before || ScreenNav.Shared.Depth != depth)
			NavMotion.Arm(NavMotion.Dir.Push);
		StorageSeed.Refresh(_activity, screen);
	}

	public void Push(string screen, object row)
	{
		CompiledScreens.Deliver(screen, row);
		Push(screen);
	}

	public void WriteBack() => CompiledScreens.WriteBack(ScreenNav.Shared.TopId());

	readonly CancellationTokenSource _calls = new();

	CancellationTokenSource _live;

	public void Pop()
	{
		var leaving = ScreenNav.Shared.TopId();
		if (!ScreenNav.Shared.Pop())
			return;
		NavMotion.Arm(NavMotion.Dir.Pop);
		_live.Cancel();
		_live = new CancellationTokenSource();
		Log.Info(MainActivity.Tag, "http-cancel screen=" + leaving);
		RequestRender();
	}

	public void Effect(string property, string resource) =>
		EffectRuntime.Start(_activity, property, resource, () => _activity.RequestRender());

	public void Every(string property) => ClockRuntime.Arm(property);

	public void Module(string module, string method, string[] args) =>
		PlatformModules.Dispatch(_activity, module, method, args);

	public Task<string> ModuleAsync(string module, string method, string[] args) =>
		PlatformModules.DispatchAsync(_activity, module, method, args, _live.Token);

	public void Continue(Task<string> task, Action<string> resume)
	{
		var generation = _live;
		task.ContinueWith(done =>
		{
			var text = done.IsCompletedSuccessfully ? done.Result ?? "" : "http-error: " + (done.Exception?.GetBaseException().Message ?? "failed");
			_activity.RunOnUiThread(() =>
			{
				if (!ReferenceEquals(generation, _live))
					return;
				resume(text);
			});
		});
	}
}

sealed class HeadingMark : View.AccessibilityDelegate
{
	public static readonly HeadingMark Instance = new();

	public override void OnInitializeAccessibilityNodeInfo(View host, AccessibilityNodeInfo info)
	{
		base.OnInitializeAccessibilityNodeInfo(host, info);
		if ((int)Android.OS.Build.VERSION.SdkInt < 28)
			return;
#pragma warning disable CA1416
		info.Heading = true;
#pragma warning restore CA1416
		Log.Info(MainActivity.Tag, "a11y-heading");
	}
}

sealed class ButtonRole : View.AccessibilityDelegate
{
	public static readonly ButtonRole Instance = new();

	public override void OnInitializeAccessibilityNodeInfo(View host, AccessibilityNodeInfo info)
	{
		base.OnInitializeAccessibilityNodeInfo(host, info);
		info.ClassName = "android.widget.Button";
		if (info.ContentDescription is not { Length: > 0 } && host is TextView text && text.Text is { Length: > 0 } value)
			info.ContentDescription = value;
	}
}

static class A11yTree
{
	public static void Write(View root)
	{
		var count = 0;
		Walk(root, ref count);
		Log.Info(MainActivity.Tag, "a11y-nodes=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}

	static void Walk(View view, ref int count)
	{
		if (count >= 80)
			return;
		var info = view.CreateAccessibilityNodeInfo();
		if (info is not null)
		{
			try
			{
#pragma warning disable CA1416
				var interesting = info.Heading || view is Android.Widget.ImageView || view is AndroidSwitch || view is AndroidButton || view.Clickable || view.LongClickable;
				if (interesting)
				{
					var bounds = new Rect();
					info.GetBoundsInScreen(bounds);
					var text = info.Text?.ToString() ?? "";
					var desc = info.ContentDescription?.ToString() ?? "";
					Log.Info(
						MainActivity.Tag,
						"a11y-node class=" + info.ClassName
						+ " heading=" + (info.Heading ? "1" : "0")
						+ " text=" + text
						+ " desc=" + desc
					+ " h=" + bounds.Height().ToString(System.Globalization.CultureInfo.InvariantCulture));
#pragma warning restore CA1416
				count++;
			}
			}
			finally
			{
				info.Dispose();
			}
		}

		if (view is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				Walk(child, ref count);
		}
	}
}
