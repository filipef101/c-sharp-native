using System.Globalization;
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
	bool _loggedChrome;

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
				return new StartupButton(_activity);
			case ElementKind.TextField:
				return new AndroidField(_activity);
			case ElementKind.Switch:
				return new AndroidSwitch(_activity);
			case ElementKind.Scroll:
				var scroll = new RefreshScroll(_activity)
				{
					FillViewport = true,
					ScrollBarStyle = ScrollbarStyles.OutsideOverlay,
				};
				scroll.SetClipChildren(false);
				scroll.SetClipToPadding(false);
				return scroll;
			case ElementKind.List:
#if NATIVEPOC_RECYCLER
				return KeyedList.Create(_activity);
#else
				return new RowWindow(_activity);
#endif
			case ElementKind.Image:
				return new ImageView(_activity)
				{
					LayoutParameters = new ViewGroup.LayoutParams(Dp(HostPolicy.TouchFloorDp), Dp(HostPolicy.TouchFloorDp)),
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
		var previous = text.Text ?? "";
		text.Text = value;
		text.ContentDescription = HostPolicy.TextAccessibility(value);
		text.Visibility = HostPolicy.TextIsHidden(value) ? ViewStates.Gone : ViewStates.Visible;
		if (text.Parent is LinearLayout across && across.Orientation == Orientation.Horizontal)
			LayoutAcross(across);
		ApplySpeech(view);
		if (value.Length == 0 || string.Equals(previous, value, StringComparison.Ordinal))
			return;
		if (!_paths.TryGetValue(view, out var path) || CurrentElement(path) is not Text spoken)
			return;
		var key = spoken.Key ?? "";
		if (!string.Equals(key, "error", StringComparison.Ordinal) && !key.EndsWith("/error", StringComparison.Ordinal))
			return;
		text.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
#pragma warning disable CA1422
		text.AnnounceForAccessibility(value);
#pragma warning restore CA1422
		Log.Info(MainActivity.Tag, "a11y-announce text=" + value);
	}

	public void SetButton(View view, string title, string accessibilityLabel)
	{
		if (view is not AndroidButton button)
			throw new InvalidOperationException("Button patch targeted a non-button view.");
		var previous = button.Text?.ToString() ?? "";
		button.Text = title;
		button.TransformationMethod = null;
		button.ContentDescription = accessibilityLabel;
		button.SetSingleLine(false);
		button.Ellipsize = null;
		button.SetHorizontallyScrolling(false);
		TouchTarget(button);
		if (_paths.TryGetValue(view, out var path) && CurrentElement(path)?.Style is { Background: 0, Wash: 0, Stroke: 0, Radius: 0 })
		{
			void ClearChrome()
			{
				button.SetBackgroundResource(0);
				button.BackgroundTintList = null;
				button.Foreground = null;
				button.StateListAnimator = null;
				button.Elevation = 0;
			}

			ClearChrome();
			button.Post(ClearChrome);
		}

		if (_paths.TryGetValue(view, out var keepPath) && string.Equals(CurrentElement(keepPath)?.Key, "keep", StringComparison.Ordinal))
		{
			var spoken = Speech.KeepChange(previous, title);
			if (spoken is not null)
			{
				button.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
#pragma warning disable CA1422
				button.AnnounceForAccessibility(spoken);
#pragma warning restore CA1422
				Log.Info(MainActivity.Tag, "a11y-announce text=" + spoken);
			}
		}

		ApplySpeech(view);
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
		var multiline = HostPolicy.FieldIsMultiline(lines, keyboard);
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
		if (edit is AndroidField typed)
			typed.SubmitOnEnter = !multiline;
		var key = edit is AndroidField marked ? marked.Key ?? "" : "";
		Log.Info(MainActivity.Tag, "field-mode key=" + key + " keyboard=" + keyboard + " lines=" + lines + " submit=" + submit);
	}

	bool AllowsAutofill(View view)
	{
		if (!_paths.TryGetValue(view, out var path))
			return true;
		return CurrentElement(path) switch
		{
			TextField field => field.Autofill,
			TextInput input => input.Autofill,
			_ => true,
		};
	}

	public void SetTextField(View view, string value, string placeholder, string accessibilityLabel)
	{
		if (view is not EditText edit)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		Log.Info(MainActivity.Tag, "field-handle " + edit.Handle.ToInt64());
		if (!string.Equals(edit.Hint, placeholder, StringComparison.Ordinal))
			edit.Hint = placeholder;
		edit.ContentDescription = accessibilityLabel;
		if (edit is AndroidField markedField)
		{
			markedField.AllowAutofill = AllowsAutofill(view);
			if (!markedField.AllowAutofill && OperatingSystem.IsAndroidVersionAtLeast(26))
			{
				markedField.ImportantForAutofill = ImportantForAutofill.No;
				Log.Info(MainActivity.Tag, "field-autofill key=" + (markedField.Key ?? "") + " off=1");
			}
		}

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
#if NATIVEPOC_STARTUP_MARKS
		var styleStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
		var dark = ThemeRuntime.IsDark(_activity);
		style = style with
		{
			Color = ThemeTokens.Adapt(style.Color, dark),
			Background = ThemeTokens.Adapt(style.Background, dark),
			Wash = ThemeTokens.Adapt(style.Wash, dark),
			Stroke = ThemeTokens.Adapt(style.Stroke, dark),
		};
		if (view is ViewGroup styledGroup)
			styledGroup.SetClipChildren(false);
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

			if (margin is LinearLayout.LayoutParams linear && style.Grow > 0 && view.Parent is not LinearLayout { Orientation: Orientation.Horizontal })
			{
				linear.Weight = style.Grow;
				linear.Height = 0;
			}

			view.LayoutParameters = margin;
		}

		if (view.Parent is LinearLayout parentRow && parentRow.Orientation == Orientation.Horizontal)
			LayoutAcross(parentRow);

		if (view is LinearLayout row)
		{
			var horizontal = style.Align switch
			{
				Align.Center => GravityFlags.CenterHorizontal,
				Align.End => GravityFlags.End,
				Align.Stretch => GravityFlags.FillHorizontal,
				Align.Start => GravityFlags.Start,
				_ => throw new InvalidOperationException("Unknown alignment."),
			};
			row.Orientation = style.Across > 0 ? Orientation.Horizontal : Orientation.Vertical;
			if (style.Across > 0)
			{
				row.ShowDividers = ShowDividers.None;
				row.BaselineAligned = false;
				row.SetGravity(horizontal | GravityFlags.CenterVertical);
				LayoutAcross(row);
			}
			else
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

				row.SetGravity(horizontal);
			}
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
			if (text is not EditText)
			{
				text.Ellipsize = null;
				text.SetHorizontallyScrolling(false);
				text.SetMaxLines(int.MaxValue);
			}
			if (HostPolicy.IsHeading(style.FontSize) && (int)Android.OS.Build.VERSION.SdkInt >= 28)
			{
				text.ImportantForAccessibility = ImportantForAccessibility.Yes;
#pragma warning disable CA1416
				text.AccessibilityHeading = true;
#pragma warning restore CA1416
				text.SetAccessibilityDelegate(HeadingMark.Instance);
			}
			if (style.Weight == FontWeight.Bold)
			{
#if NATIVEPOC_STARTUP_MARKS
				var typefaceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
				text.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
#if NATIVEPOC_STARTUP_MARKS
				StartupMarks.AddSpan("typeface", SpanUs(typefaceStarted));
#endif
			}
			ThemeRuntime.NoteColor(view, style.Color);
			if (text is AndroidButton inked)
			{
				var ink = style.Color == 0 ? ThemeTokens.Text(dark) : style.Color;
				inked.SetTextColor(FlatChrome.TextList(unchecked((int)ink), unchecked((int)ThemeTokens.Muted(dark))));
			}
			else
			{
				text.SetTextColor(style.Color == 0
					? new Android.Graphics.Color(unchecked((int)ThemeTokens.Text(dark)))
					: new Android.Graphics.Color(unchecked((int)style.Color)));
			}
			if (text is AndroidButton fitted)
				FitButton(fitted, style);
		}

		if (style.Background != 0 || style.Wash != 0 || style.Radius > 0 || style.Stroke != 0)
			ApplyChrome(view, style);
#if NATIVEPOC_RECYCLER
		if (KeyedList.Is(view) && style.Across >= 2)
			KeyedList.Columns(view, style.Across);
#endif
#if NATIVEPOC_STARTUP_MARKS
		var bucket = ViewBucket(view);
		if (bucket is not null)
			StartupMarks.AddSpan(bucket, SpanUs(styleStarted));
#endif
		ApplySpeech(view);
	}

	void ApplySpeech(View view)
	{
		if (view.Parent is not View parent)
			return;
		if (!_paths.TryGetValue(parent, out var path))
			return;
		var element = CurrentElement(path);
		if (element is not Column column)
			return;
		var spoken = Speech.Of(column);
		if (spoken is null || parent is not ViewGroup group)
			return;
		var statCard = column.Key is "pieces" or "kept" or "kiln"
			&& column.Children.Count == 2
			&& column.Children[0] is Text
			&& column.Children[1] is Text;
		if (statCard)
		{
			parent.ContentDescription = spoken;
			parent.ImportantForAccessibility = ImportantForAccessibility.Yes;
			for (var index = 0; index < group.ChildCount; index++)
			{
				var child = group.GetChildAt(index);
				if (child is not null)
					child.ImportantForAccessibility = ImportantForAccessibility.No;
			}

			return;
		}

		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not TextView text || child is AndroidButton)
				continue;
			if (!_paths.TryGetValue(child, out var childPath) || CurrentElement(childPath) is not Text)
				continue;
			text.ImportantForAccessibility = ImportantForAccessibility.No;
		}
	}

	string? ViewBucket(View view)
	{
		for (View? current = view; current is not null; current = current.Parent as View)
		{
			if (!_paths.TryGetValue(current, out var path))
				continue;
			var key = CurrentElement(path)?.Key;
			if (key is "hero" or "page" or "stats" or "pieces" or "kept" or "kiln" or "shelf" or "room")
				return "views-" + key;
		}

		return null;
	}

#if NATIVEPOC_STARTUP_MARKS
	static long SpanUs(long started) =>
		(System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
#endif

	void ApplyChrome(View view, Style style)
	{
		if (!_loggedChrome)
		{
			_loggedChrome = true;
			Android.Util.Log.Info(MainActivity.Tag, "startup chrome=direct shadow=0 clip=0");
		}

		if (view is ImageView && style.Radius > 0)
		{
			view.Background = null;
			view.OutlineProvider = null;
			view.ClipToOutline = false;
			view.Elevation = 0;
			return;
		}

		var gradient = style.Wash != 0 && style.Background != 0;
		if (gradient)
			StartupMarks.Phase("gradient");
#if NATIVEPOC_STARTUP_MARKS
		var drawableStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
		// Rounded fill, gradient, and stroke are this drawable. An outline clip
		// and an elevation shadow are extra shaders on the first frame.
		var drawable = new GradientDrawable();
		drawable.SetCornerRadius(Dp(style.Radius));
		if (style.Background != 0 && style.Wash != 0)
		{
			drawable.SetOrientation(GradientDrawable.Orientation.TopBottom);
			drawable.SetColors(new[] { unchecked((int)style.Background), unchecked((int)style.Wash) });
		}
		else
		{
			drawable.SetColor(style.Background == 0
				? Android.Graphics.Color.Transparent
				: new Android.Graphics.Color(unchecked((int)style.Background)));
		}

		if (style.Stroke != 0)
			drawable.SetStroke(Math.Max(1, Dp(2)), new Android.Graphics.Color(unchecked((int)style.Stroke)));
		view.Background = drawable;
		view.OutlineProvider = null;
		view.ClipToOutline = false;
		view.Elevation = 0;
		if (view is AndroidButton tinted)
		{
			tinted.BackgroundTintList = style.Background == 0 && style.Wash == 0
				? Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent)
				: null;
		}
#if NATIVEPOC_STARTUP_MARKS
		var drawableUs = (System.Diagnostics.Stopwatch.GetTimestamp() - drawableStarted) * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
		StartupMarks.AddDrawable(gradient, false, drawableUs);
#endif
	}

	void LayoutAcross(LinearLayout row)
	{
		var gapDp = 0;
		if (_paths.TryGetValue(row, out var rowPath))
			gapDp = CurrentElement(rowPath)?.Style?.Gap ?? 0;
		var gap = gapDp > 0 ? Dp(gapDp) : 0;
		var seen = false;
		for (var index = 0; index < row.ChildCount; index++)
		{
			var child = row.GetChildAt(index);
			if (child?.LayoutParameters is not LinearLayout.LayoutParams linear)
				continue;
			var gone = child.Visibility == ViewStates.Gone;
			Style? style = null;
			if (_paths.TryGetValue(child, out var path))
				style = CurrentElement(path)?.Style;
			var edge = 0;
			var grow = linear.Weight;
			if (style is { } known)
			{
				if (known.Margin > 0)
					edge = Dp(known.Margin);
				grow = known.Grow;
			}
			linear.TopMargin = edge;
			linear.RightMargin = edge;
			linear.BottomMargin = edge;
			linear.LeftMargin = edge + (!gone && seen ? gap : 0);
			if (grow > 0)
			{
				linear.Weight = grow;
				linear.Width = 0;
				linear.Height = ViewGroup.LayoutParams.WrapContent;
			}
			else
			{
				linear.Weight = 0;
				linear.Width = ViewGroup.LayoutParams.WrapContent;
				linear.Height = ViewGroup.LayoutParams.WrapContent;
			}

			child.LayoutParameters = linear;
			if (!gone)
				seen = true;
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
		if (HostPolicy.TouchFloor(mask) > 0)
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

	public void SetImage(View view, string source, string description, bool decorative)
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

		var side = HostPolicy.TouchFloorDp;
		var radius = 0;
		if (_paths.TryGetValue(view, out var imagePath) && CurrentElement(imagePath) is Image picture)
		{
			if (picture.Side > 0)
				side = picture.Side;
			if (picture.Style is { Radius: > 0 } tile)
				radius = tile.Radius;
		}
		var px = Dp(side);
		image.SetMinimumWidth(px);
		image.SetMinimumHeight(px);
		image.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, px);
		PictureBinder.Show(image, source, _activity, description, decorative, radius);
		TouchTarget(image);
	}

	void FitButton(AndroidButton button, Style? style)
	{
		style ??= _paths.TryGetValue(button, out var path) ? CurrentElement(path)?.Style : null;
		var scale = _activity.Resources?.Configuration?.FontScale ?? 1f;
		var height = Dp(HostPolicy.ButtonDp(style?.FontSize ?? 0, style?.Padding ?? 0, scale));
		var floor = Dp(HostPolicy.TouchFloorDp);
		button.SetMinimumHeight(height);
		button.SetMinHeight(height);
		button.SetMinWidth(floor);
		if (button.MinimumWidth < floor)
			button.SetMinimumWidth(floor);
		var horizontal = (style?.Align ?? Align.Start) switch
		{
			Align.Center => GravityFlags.CenterHorizontal,
			Align.End => GravityFlags.End,
			Align.Stretch => GravityFlags.FillHorizontal,
			Align.Start => GravityFlags.Start,
			_ => throw new InvalidOperationException("Unknown alignment."),
		};
		button.Gravity = horizontal | GravityFlags.CenterVertical;
		if (button.LayoutParameters is LinearLayout.LayoutParams linear && !(linear.Height == 0 && linear.Weight > 0))
		{
			linear.Height = height;
			button.LayoutParameters = linear;
		}
	}

	void TouchTarget(View view)
	{
		if (view is AndroidButton button)
		{
			FitButton(button, null);
			return;
		}

		var min = Dp(HostPolicy.TouchFloorDp);
		if (view.MinimumWidth > min)
			min = view.MinimumWidth;
		view.SetMinimumWidth(min);
		view.SetMinimumHeight(Math.Max(view.MinimumHeight, min));
		if (view is TextView text)
		{
			text.SetMinWidth(min);
			text.SetMinHeight(min);
		}
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
		toggle.Text = accessibilityLabel;
		toggle.ContentDescription = accessibilityLabel;
		toggle.SetSingleLine(false);
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
		if (view is GestureColumn column)
		{
			var key = CurrentElement(path)?.Key;
			if (key is "stats" or "row-0" or "hero" or "pieces" or "kept" or "kiln" or "page")
				column.MarkStartup(key);
			else if (string.IsNullOrEmpty(key) && path.Length == 1)
				column.MarkStartup("page");
		}

		if (view is StartupButton timed && CurrentElement(path)?.Key is string buttonKey && (buttonKey == "shelf" || buttonKey == "room"))
			timed.MarkStartup(buttonKey);

		var mountedKey = CurrentElement(path)?.Key;
		if (!string.IsNullOrEmpty(mountedKey))
			AnimatedPlayer.Bind(mountedKey, view);
		if (view is RefreshScroll shelfScroll && string.Equals(mountedKey, "scroll", StringComparison.Ordinal))
		{
			shelfScroll.Post(() =>
			{
				if (shelfScroll.Handle == IntPtr.Zero || shelfScroll.ChildCount < 1)
					return;
				var child = shelfScroll.GetChildAt(0);
				if (child is null || !_paths.TryGetValue(child, out var childPath))
					return;
				if (!string.Equals(CurrentElement(childPath)?.Key, "list", StringComparison.Ordinal))
					return;
				Log.Info(MainActivity.Tag, "list-first=0");
			});
		}

		if (path.Length == 0)
		{
			var pad = (int)(16 * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));
			view.SetPadding(pad, pad, pad, pad);
			if (view is ViewGroup rootGroup)
			{
				rootGroup.SetClipToPadding(false);
				rootGroup.SetClipChildren(false);
			}
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
		AnimatedPlayer.Unbind(view);
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

		if (view is ImageView picture)
		{
			picture.Clickable = (mask & Gestures.Tap) != 0;
			picture.SetOnClickListener((mask & Gestures.Tap) == 0 ? null : new TapClick(() => Fire(path, Gestures.Tap)));
			return;
		}

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
			if (RunSubmit(path, edit, args.ActionId, args.Event, "ime"))
				args.Handled = true;
		};
		edit.EditorAction += editor;
		_listeners[view] = new Listener { TextChanged = handler, Editor = editor };
		if (edit is AndroidField field)
		{
			field.Key = CurrentElement(path)?.Key;
			field.OnEnter = ev => RunSubmit(path, edit, ImeAction.ImeNull, ev, "key");
			field.FocusChange += (_, args) =>
			{
				if (!args.HasFocus || field.Key is null || FieldFocus.Frozen)
					return;
				FieldFocus.Note(field.Key, field.SelectionStart, field.SelectionEnd);
				if (!field.UserTouch)
					return;
				field.UserTouch = false;
				FieldIme.Show(field);
			};
		}
	}

	bool RunSubmit(NodePath path, EditText edit, ImeAction actionId, KeyEvent? ev, string via)
	{
		var current = CurrentElement(path);
		if (current is not (TextField or TextInput))
			return false;
		if (ev is { Action: not KeyEventActions.Down })
			return false;
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
		var expected = kind switch
		{
			Submit.Next => ImeAction.Next,
			Submit.Send => ImeAction.Send,
			Submit.Go => ImeAction.Go,
			Submit.Search => ImeAction.Search,
			Submit.None => ImeAction.Done,
			_ => throw new InvalidOperationException("Unknown submit action."),
		};
		if (actionId != expected && actionId != ImeAction.ImeNull)
			return false;
		var key = edit is AndroidField marked ? marked.Key ?? "" : "";
		Log.Info(MainActivity.Tag, "field-action key=" + key + " action=" + actionId + " via=" + via);
		if (kind == Submit.Next && FieldIme.FocusNext(edit))
			return true;
		if (submit is null)
			return false;
		if (edit is AndroidField gate)
		{
			var now = Environment.TickCount64;
			if (now - gate.LastSubmit < 250)
				return true;
			gate.LastSubmit = now;
		}
#if DEBUG
		Guard(current, submit);
#else
		submit();
#endif
		Log.Info(MainActivity.Tag, "field-submit key=" + key + " via=" + via);
		return true;
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

static class FieldIme
{
	static bool _watching;
	static int _generation;

	public static void Show(AndroidField field)
	{
		var generation = _generation;
		field.Post(() =>
		{
			if (generation != _generation || !field.IsFocused)
				return;
			if (field.Context is Activity activity)
				activity.Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateVisible);
			var imm = (InputMethodManager?)field.Context?.GetSystemService(Context.InputMethodService);
			imm?.ShowSoftInput(field, ShowFlags.Implicit);
			var rect = new Rect(0, 0, Math.Max(field.Width, 1), Math.Max(field.Height, 1));
			field.RequestRectangleOnScreen(rect, true);
			LogScroll(field);
		});
		if (_watching)
			return;
		var decor = (field.Context as Activity)?.Window?.DecorView;
		if (decor?.ViewTreeObserver is null)
			return;
		_watching = true;
		decor.ViewTreeObserver.AddOnGlobalLayoutListener(new KeyboardLayout(decor));
	}

	public static void Hide(Activity activity)
	{
		var generation = ++_generation;
		var decor = activity.Window?.DecorView;
		var token = activity.CurrentFocus?.WindowToken ?? decor?.WindowToken;
		var cleared = decor is null ? 0 : ClearFields(decor);
		var imm = (InputMethodManager?)activity.GetSystemService(Context.InputMethodService);
		var hidden = Dismiss(activity, decor, token, imm);
		decor?.RequestFocus();
		activity.Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateHidden);
		Log.Info(
			MainActivity.Tag,
			"field-hide focused=" + (cleared > 0 ? "1" : "0")
			+ " cleared=" + cleared.ToString(CultureInfo.InvariantCulture)
			+ " hidden=" + (hidden ? "1" : "0"));
		decor?.Post(() =>
		{
			if (generation != _generation)
				return;
			if (decor is not null)
				ClearFields(decor);
			Dismiss(activity, decor, token, imm);
		});
	}

	static bool Dismiss(Activity activity, View? decor, Android.OS.IBinder? token, InputMethodManager? imm)
	{
		var hidden = token is not null && imm?.HideSoftInputFromWindow(token, HideSoftInputFlags.None) == true;
		if (OperatingSystem.IsAndroidVersionAtLeast(30) && decor?.WindowInsetsController is { } insets)
		{
#pragma warning disable CA1416
			insets.Hide(WindowInsets.Type.Ime());
#pragma warning restore CA1416
		}

		return hidden;
	}

	static int ClearFields(View view)
	{
		var count = 0;
		if (view is AndroidField marked)
			marked.UserTouch = false;
		if (view is EditText edit && edit.IsFocused)
		{
			edit.ClearFocus();
			count = 1;
		}

		if (view is not ViewGroup group)
			return count;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				count += ClearFields(child);
		}

		return count;
	}

	public static bool FocusNext(EditText edit)
	{
		var root = edit.RootView;
		if (root is null)
			return false;
		var fields = new List<EditText>();
		Collect(root, fields);
		var index = fields.IndexOf(edit);
		if (index < 0 || index + 1 >= fields.Count)
			return false;
		var next = fields[index + 1];
		var from = edit is AndroidField fromField ? fromField.Key ?? "" : "";
		var to = next is AndroidField toField ? toField.Key ?? "" : "";
		if (!next.RequestFocus())
			return false;
		Log.Info(MainActivity.Tag, "field-next from=" + from + " to=" + to);
		return true;
	}

	static void LogScroll(AndroidField field)
	{
		var visible = new Rect();
		field.GetWindowVisibleDisplayFrame(visible);
		var location = new int[2];
		field.GetLocationOnScreen(location);
		var bottom = location[1] + field.Height;
		var above = bottom <= visible.Bottom ? "1" : "0";
		Log.Info(
			MainActivity.Tag,
			"field-scroll key=" + (field.Key ?? "")
			+ " field-bottom=" + bottom.ToString(CultureInfo.InvariantCulture)
			+ " visible-bottom=" + visible.Bottom.ToString(CultureInfo.InvariantCulture)
			+ " above=" + above);
	}

	static void Collect(View view, List<EditText> fields)
	{
		if (view is EditText edit)
			fields.Add(edit);
		if (view is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				Collect(child, fields);
		}
	}

	sealed class KeyboardLayout : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
	{
		readonly View _decor;

		public KeyboardLayout(View decor) => _decor = decor;

		public void OnGlobalLayout()
		{
			if (_decor.FindFocus() is AndroidField field)
				LogScroll(field);
		}
	}
}

sealed class FieldRule : Drawable
{
	readonly Paint _paint;
	readonly int _height;

	public FieldRule(int color, int heightPx)
	{
		_height = heightPx;
		_paint = new Paint();
		_paint.Color = new Color(color);
	}

	public override void Draw(Canvas canvas)
	{
		var bounds = Bounds;
		var top = bounds.Bottom - _height;
		if (top < bounds.Top)
			top = bounds.Top;
		canvas.DrawRect(bounds.Left, top, bounds.Right, bounds.Bottom, _paint);
	}

	public override void SetAlpha(int alpha) => _paint.Alpha = alpha;

	public override void SetColorFilter(ColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);

	public override int Opacity => (int)Format.Translucent;
}

sealed class AndroidField : EditText
{
	static bool _loggedFocus;
	public string? Key { get; set; }

	public long LastSubmit { get; set; }

	public bool AllowAutofill { get; set; }

	public bool SubmitOnEnter { get; set; }

	public bool UserTouch { get; set; }

	public Func<KeyEvent?, bool>? OnEnter { get; set; }

	public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
	{
		if (keyCode == Keycode.Enter && SubmitOnEnter && OnEnter?.Invoke(e) == true)
			return true;
		return base.OnKeyDown(keyCode, e);
	}

	public override bool OnTouchEvent(MotionEvent? e)
	{
		if (e?.Action == MotionEventActions.Down)
			UserTouch = true;
		return base.OnTouchEvent(e);
	}

	public AndroidField(Context context) : base(context)
	{
		// edit_text_material is a nine-patch. A transparent stand-in that
		// came back after the reported frame made fully drawn an unfinished
		// field. The steady state is a 2dp rule in the app accent.
		var color = unchecked((int)ThemeTokens.Accent(FlatChrome.Dark(context)));
		var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		var height = Math.Max(1, (int)(2f * density));
		SetBackgroundResource(0);
		BackgroundTintList = null;
		Background = new FieldRule(color, height);
		FlatChrome.LogField(color);
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
			_field.ViewTreeObserver?.RemoveOnPreDrawListener(this);
			Enable(_field);
			if (_loggedFocus)
				return true;
			_loggedFocus = true;
			Log.Info(
				MainActivity.Tag,
				"startup field-focus focused=" + (focused ? "1" : "0"));
			return true;
		}

		static void Enable(AndroidField field)
		{
			field.FocusableInTouchMode = true;
			if (!OperatingSystem.IsAndroidVersionAtLeast(26))
				return;
			if (field.AllowAutofill)
				field.ImportantForAutofill = ImportantForAutofill.Auto;
			else
				field.ImportantForAutofill = ImportantForAutofill.No;
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

	public static void Clear()
	{
		if (_key is null)
			return;
		Log.Info(MainActivity.Tag, "field-blur key=" + _key);
		_key = null;
		_start = 0;
		_end = 0;
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
		{
			FieldIme.Hide(_activity);
			FieldFocus.Clear();
			NavMotion.Arm(NavMotion.Dir.Push);
#if NATIVEPOC_NAV
			NavFocus.Arm();
#endif
		}
		else
		{
			/* DeliverRow pushes before this call, so the stack is already
			   there. The screen the user is about to see still needs the
			   heading focus. */
			FieldIme.Hide(_activity);
			FieldFocus.Clear();
#if NATIVEPOC_NAV
			NavFocus.Arm();
#endif
			NavMotion.Arm(NavMotion.Dir.Push);
		}
		StorageSeed.Refresh(_activity, screen);
	}

	public void Push(string screen, object row)
	{
		CompiledScreens.Deliver(screen, row);
		Push(screen);
	}

	public void WriteBack() => CompiledScreens.WriteBack(ScreenNav.Shared.TopId());

	readonly Dictionary<string, CancellationTokenSource> _screenCalls = new(StringComparer.Ordinal);

	string? _lastOpen;

	string? _reopen;

	CancellationTokenSource CallsFor(string screen)
	{
		if (screen.Length == 0)
			screen = "http";
		if (!_screenCalls.TryGetValue(screen, out var calls))
		{
			calls = new CancellationTokenSource();
			_screenCalls[screen] = calls;
		}

		return calls;
	}

	public void RefreshTop(string screen)
	{
		_reopen = screen;
		StorageSeed.Refresh(_activity, screen);
		Log.Info(MainActivity.Tag, "nav-refresh id=" + screen + " slide=0");
	}

	public void Pop()
	{
		FieldIme.Hide(_activity);
		FieldFocus.Clear();
		var leaving = ScreenNav.Shared.TopId();
		var nested = ScreenNav.Shared.Depth > 1;
		if (nested)
			ScreenSignals.FireLeave();
		if (!ScreenNav.Shared.Pop())
			return;
		if (nested)
			Log.Info(MainActivity.Tag, "lifecycle leave " + leaving);
		NavMotion.Arm(NavMotion.Dir.Pop);
#if NATIVEPOC_NAV
		NavFocus.Arm();
#endif
#if NATIVEPOC_HTTP
		if (leaving.Length > 0)
		{
			HttpFlight.Cancel(leaving);
#if !NATIVEPOC_HTTP_MANAGED
			try
			{
				JniRegistry.Call("PlatformHttp.Disconnect", [leaving]);
			}
			catch (Exception)
			{
			}
#endif
		}
#endif
		if (leaving.Length > 0 && _screenCalls.Remove(leaving, out var calls))
			calls.Cancel();
		RequestRender();
	}

	public void OnOpen(string screen, System.Action open)
	{
		if (screen.Length == 0)
			return;
		var again = string.Equals(screen, _reopen, StringComparison.Ordinal);
		if (!again && string.Equals(screen, _lastOpen, StringComparison.Ordinal))
			return;
		if (again)
			_reopen = null;
		_lastOpen = screen;
		Log.Info(MainActivity.Tag, "nav-open screen=" + screen + " again=" + (again ? "1" : "0"));
		open();
	}

	public void OnPause(System.Action handler) => ScreenSignals.Pause = handler;

	public void OnResume(System.Action handler) => ScreenSignals.Resume = handler;

	public void OnLeave(System.Action handler) => ScreenSignals.Leave = handler;

	public void Effect(string property, string resource) =>
		EffectRuntime.Start(_activity, property, resource, () => _activity.RequestRender());

	public void Every(string property) => ClockRuntime.Arm(property);

	public void Module(string module, string method, string[] args) =>
		PlatformModules.Dispatch(_activity, module, method, args);

	public Task<string> ModuleAsync(string module, string method, string[] args)
	{
		var screen = ScreenNav.Shared.TopId();
		return PlatformModules.DispatchAsync(_activity, module, method, args, CallsFor(screen).Token, screen);
	}

	public void Continue(Task<string> task, Action<string> resume)
	{
		var generation = CallsFor(ScreenNav.Shared.TopId());
		task.ContinueWith(done =>
		{
			var text = done.IsCompletedSuccessfully ? done.Result ?? "" : "http-error: " + (done.Exception?.GetBaseException().Message ?? "failed");
			_activity.RunOnUiThread(() =>
			{
				if (generation.IsCancellationRequested)
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
