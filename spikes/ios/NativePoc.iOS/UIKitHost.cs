namespace NativePoc;

sealed class UIKitHost : IElementHost<UIView>
{
	readonly Func<Element?> _current;
	readonly Dictionary<UIView, Slot> _slots = new();
	bool _applyingText;

	public UIKitHost(Func<Element?> current)
	{
		_current = current;
	}

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
				return new UILabel
				{
					Lines = 0,
					TextColor = UIColor.Label,
				};
			case ElementKind.Button:
				return new UIButton(UIButtonType.System);
			case ElementKind.TextField:
				return new UITextField
				{
					BorderStyle = UITextBorderStyle.RoundedRect,
					TextColor = UIColor.Label,
					AutocapitalizationType = UITextAutocapitalizationType.None,
					AutocorrectionType = UITextAutocorrectionType.No,
				};
			default:
				return Never(kind);
		}
	}

	public void Insert(UIView parent, int index, UIView child)
	{
		if (parent is not UIStackView stack)
			throw new InvalidOperationException("Parent is not a stack.");
		stack.InsertArrangedSubview(child, (nuint)index);
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
		Console.WriteLine("NativePoc field-handle " + field.Handle);
		if (!string.Equals(field.Placeholder, placeholder, StringComparison.Ordinal))
			field.Placeholder = placeholder;
		field.AccessibilityLabel = accessibilityLabel;
		var current = field.Text ?? "";
		if (string.Equals(current, value, StringComparison.Ordinal))
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

	public void OnMounted(NodePath path, UIView view)
	{
		Attach(path, view, logMount: true);
	}

	public void Retarget(NodePath path, UIView view)
	{
		if (_slots.TryGetValue(view, out var slot))
			slot.Path = path;
	}

	public void OnUnmounted(NodePath path, UIView view)
	{
		Detach(view);
		if (view.Superview is not null)
			view.RemoveFromSuperview();
		view.Dispose();
	}

	void Attach(NodePath path, UIView view, bool logMount)
	{
		Detach(view);
		if (view is UIButton button)
		{
			EventHandler handler = (_, _) =>
			{
				if (!_slots.TryGetValue(button, out var slot))
					return;
				if (CurrentElement(slot.Path) is Button current)
					current.OnPress();
			};
			button.TouchUpInside += handler;
			_slots[view] = new Slot { Path = path, Click = handler };
			if (logMount)
				Console.WriteLine("NativePoc button-mounted handle=" + button.Handle);
			return;
		}

		if (view is UITextField field)
		{
			EventHandler handler = (_, _) =>
			{
				if (_applyingText)
					return;
				if (!_slots.TryGetValue(field, out var slot))
					return;
				if (CurrentElement(slot.Path) is TextField current)
					current.OnChange(field.Text ?? "");
			};
			field.EditingChanged += handler;
			_slots[view] = new Slot { Path = path, EditingChanged = handler };
		}
	}

	void Detach(UIView view)
	{
		if (!_slots.Remove(view, out var slot))
			return;
		if (view is UIButton button && slot.Click is not null)
			button.TouchUpInside -= slot.Click;
		if (view is UITextField field && slot.EditingChanged is not null)
			field.EditingChanged -= slot.EditingChanged;
	}

	Element? CurrentElement(NodePath path)
	{
		var root = _current();
		if (root is null)
			return null;
		return TreeDiff.At(root, path);
	}

	static UIView Never(ElementKind kind) => kind switch
	{
		ElementKind.Column => throw new InvalidOperationException("Unknown element kind."),
		ElementKind.Text => throw new InvalidOperationException("Unknown element kind."),
		ElementKind.Button => throw new InvalidOperationException("Unknown element kind."),
		ElementKind.TextField => throw new InvalidOperationException("Unknown element kind."),
	};

	sealed class Slot
	{
		public NodePath Path { get; set; }

		public EventHandler? Click { get; init; }

		public EventHandler? EditingChanged { get; init; }
	}
}
