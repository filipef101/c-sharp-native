namespace NativePoc;

sealed class UIKitHost : IElementHost<UIView>
{
	readonly Func<Element?> _current;
	readonly Dictionary<UIView, NodePath> _paths = new();
	bool _applyingText;

	public UIKitHost(Func<Element?> current) => _current = current;

	public UIView Create(ElementKind kind)
	{
		switch (kind)
		{
			case ElementKind.Column:
				return new UIStackView
				{
					Axis = UILayoutConstraintAxis.Vertical,
					Alignment = UIStackViewAlignment.Fill,
					Distribution = UIStackViewDistribution.Fill,
					Spacing = 12,
				};
			case ElementKind.Text:
				return new UILabel { Lines = 0, TextColor = UIColor.Label };
			case ElementKind.Button:
				return new UIButton(UIButtonType.System);
			case ElementKind.TextField:
				return new UITextField
				{
					BorderStyle = UITextBorderStyle.RoundedRect,
					TextColor = UIColor.Label,
				};
			case ElementKind.Switch:
				return new UISwitch();
			case ElementKind.Scroll:
			case ElementKind.List:
				return new UIScrollView();
			case ElementKind.Image:
				return new UIImageView { ContentMode = UIViewContentMode.ScaleAspectFit };
			default:
				throw new InvalidOperationException("Unknown element kind " + IosSurface.ViewClass(kind) + ".");
		}
	}

	public void Insert(UIView parent, int index, UIView child)
	{
		if (parent is UIStackView stack)
		{
			stack.InsertArrangedSubview(child, (nuint)index);
			return;
		}

		if (parent is UIScrollView scroll)
		{
			scroll.AddSubview(child);
			return;
		}

		throw new InvalidOperationException("Parent is not a stack.");
	}

	public void Remove(UIView parent, int index)
	{
		if (parent is not UIStackView stack)
			throw new InvalidOperationException("Parent is not a stack.");
		var child = stack.ArrangedSubviews[index];
		stack.RemoveArrangedSubview(child);
		child.RemoveFromSuperview();
	}

	public void SetText(UIView view, string value)
	{
		if (view is not UILabel label)
			throw new InvalidOperationException("Text patch targeted a non-label view.");
		label.Text = value;
	}

	public void SetButton(UIView view, string title, string accessibilityLabel)
	{
		if (view is not UIButton button)
			throw new InvalidOperationException("Button patch targeted a non-button view.");
		button.SetTitle(title, UIControlState.Normal);
		button.AccessibilityLabel = accessibilityLabel;
	}

	public void SetTextField(UIView view, string value, string placeholder, string accessibilityLabel)
	{
		if (view is not UITextField field)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		field.Placeholder = placeholder;
		field.AccessibilityLabel = accessibilityLabel;
		if (string.Equals(field.Text ?? "", value, StringComparison.Ordinal))
			return;
		_applyingText = true;
		try
		{
			field.Text = value;
		}
		finally
		{
			_applyingText = false;
		}
	}

	public void SetFieldMode(UIView view, Keyboard keyboard, int lines, Submit submit)
	{
		if (view is not UITextField field)
			throw new InvalidOperationException("Text field patch targeted a non-field view.");
		field.KeyboardType = keyboard switch
		{
			Keyboard.Text => UIKeyboardType.Default,
			Keyboard.Email => UIKeyboardType.EmailAddress,
			Keyboard.Number => UIKeyboardType.NumberPad,
			Keyboard.Phone => UIKeyboardType.PhonePad,
			Keyboard.Password => UIKeyboardType.Default,
			_ => throw new InvalidOperationException("Unknown keyboard."),
		};
		field.SecureTextEntry = keyboard == Keyboard.Password;
		field.ReturnKeyType = submit switch
		{
			Submit.None => UIReturnKeyType.Default,
			Submit.Next => UIReturnKeyType.Next,
			Submit.Send => UIReturnKeyType.Send,
			Submit.Go => UIReturnKeyType.Go,
			Submit.Search => UIReturnKeyType.Search,
			_ => throw new InvalidOperationException("Unknown submit."),
		};
		_ = lines;
	}

	public void SetSwitch(UIView view, bool value, string accessibilityLabel)
	{
		if (view is not UISwitch toggle)
			throw new InvalidOperationException("Switch patch targeted a non-switch view.");
		toggle.On = value;
		toggle.AccessibilityLabel = accessibilityLabel;
	}

	public void SetList(UIView view, IReadOnlyList<Element> rows)
	{
		if (view is not UIScrollView scroll)
			throw new InvalidOperationException("List patch targeted a non-list view.");
		foreach (var sub in scroll.Subviews)
			sub.RemoveFromSuperview();
		var stack = new UIStackView { Axis = UILayoutConstraintAxis.Vertical };
		scroll.AddSubview(stack);
		foreach (var row in rows)
		{
			var label = new UILabel { Text = row is Text text ? text.Value : "" };
			stack.AddArrangedSubview(label);
		}
	}

	public void SetStyle(UIView view, Style style)
	{
		if (!IosSurface.Applies(style) && style.Padding == 0)
			return;
		if (view is UIStackView stack && style.Gap > 0)
			stack.Spacing = style.Gap;
		if (view is UILabel label)
		{
			if (style.FontSize > 0)
				label.Font = style.Weight == FontWeight.Bold ? UIFont.BoldSystemFontOfSize(style.FontSize) : UIFont.SystemFontOfSize(style.FontSize);
			if (style.Color != 0)
				label.TextColor = Color(style.Color);
		}

		if (style.Background != 0)
			view.BackgroundColor = Color(style.Background);
		if (style.Radius > 0)
		{
			view.Layer.CornerRadius = style.Radius;
			view.ClipsToBounds = true;
		}
	}

	public void SetMotion(UIView view, Motion motion)
	{
		view.Alpha = motion.FromOpacity / 100f;
		view.Transform = CGAffineTransform.MakeScale(motion.FromScale / 100f, motion.FromScale / 100f);
		UIView.Animate(motion.Duration / 1000d, () =>
		{
			view.Alpha = motion.Opacity / 100f;
			var scale = CGAffineTransform.MakeScale(motion.Scale / 100f, motion.Scale / 100f);
			var shift = CGAffineTransform.MakeTranslation(motion.X, motion.Y);
			view.Transform = scale.Multiply(shift);
		});
	}

	public void SetGesture(UIView view, byte mask)
	{
		if (view.GestureRecognizers is not null)
		{
			foreach (var recognizer in view.GestureRecognizers)
				view.RemoveGestureRecognizer(recognizer);
		}

		if ((mask & Gestures.Tap) != 0)
		{
			var tap = new UITapGestureRecognizer(() => Fire(view, Gestures.Tap));
			view.AddGestureRecognizer(tap);
		}

		if ((mask & Gestures.LongPress) != 0)
		{
			var press = new UILongPressGestureRecognizer(() => Fire(view, Gestures.LongPress));
			view.AddGestureRecognizer(press);
		}

		if ((mask & Gestures.Swipe) != 0)
		{
			var swipe = new UISwipeGestureRecognizer(() => Fire(view, Gestures.Swipe))
			{
				Direction = UISwipeGestureRecognizerDirection.Left | UISwipeGestureRecognizerDirection.Right,
			};
			view.AddGestureRecognizer(swipe);
		}

		if ((mask & Gestures.Refresh) != 0)
		{
			var pan = new UIPanGestureRecognizer(() => Fire(view, Gestures.Refresh));
			view.AddGestureRecognizer(pan);
		}
	}

	public void SetImage(UIView view, string source)
	{
		if (view is not UIImageView image)
			throw new InvalidOperationException("Image patch targeted a non-image view.");
		image.Image = UIImage.FromBundle(source);
		image.AccessibilityLabel = source;
	}

	public void OnMounted(NodePath path, UIView view)
	{
		_paths[view] = path;
		Attach(path, view);
	}

	public void Retarget(NodePath path, UIView view)
	{
		_paths[view] = path;
		Attach(path, view);
	}

	public void OnUnmounted(NodePath path, UIView view)
	{
		_paths.Remove(view);
		if (view.Superview is not null)
			view.RemoveFromSuperview();
		view.Dispose();
	}

	void Attach(NodePath path, UIView view)
	{
		if (view is UIButton button)
		{
			button.TouchUpInside += (_, _) =>
			{
				if (Current(path) is Button current)
					current.OnPress();
			};
		}

		if (view is UITextField field)
		{
			field.EditingChanged += (_, _) =>
			{
				if (_applyingText)
					return;
				if (Current(path) is TextField current)
					current.OnChange(field.Text ?? "");
			};
		}

		if (view is UISwitch toggle)
		{
			toggle.ValueChanged += (_, _) =>
			{
				if (Current(path) is Switch current)
					current.OnChange(toggle.On);
			};
		}
	}

	void Fire(UIView view, byte kind)
	{
		if (!_paths.TryGetValue(view, out var path) || Current(path) is not { } element)
			return;
		var action = kind switch
		{
			Gestures.Tap => element.OnTap,
			Gestures.LongPress => element.OnLongPress,
			Gestures.Swipe => element.OnSwipe,
			Gestures.Refresh => element.OnRefresh,
			_ => null,
		};
		action?.Invoke();
	}

	Element? Current(NodePath path)
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

	static UIColor Color(uint argb)
	{
		var alpha = ((argb >> 24) & 0xFF) / 255f;
		var red = ((argb >> 16) & 0xFF) / 255f;
		var green = ((argb >> 8) & 0xFF) / 255f;
		var blue = (argb & 0xFF) / 255f;
		return UIColor.FromRGBA(red, green, blue, alpha);
	}
}
