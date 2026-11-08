namespace NativePoc;

public enum HostInput
{
	None = 0,
	Button = 1,
	TextField = 2,
	Switch = 3,
}

public static class HostDispatch
{
	public static HostInput Choose(Type view, Type button, Type textField, Type toggle)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(button);
		ArgumentNullException.ThrowIfNull(textField);
		ArgumentNullException.ThrowIfNull(toggle);
		if (button.IsAssignableFrom(view))
			return HostInput.Button;
		if (textField.IsAssignableFrom(view))
			return HostInput.TextField;
		if (toggle.IsAssignableFrom(view))
			return HostInput.Switch;
		return HostInput.None;
	}
}
