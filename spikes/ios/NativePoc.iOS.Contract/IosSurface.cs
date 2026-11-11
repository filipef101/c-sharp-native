namespace NativePoc;

public static class IosSurface
{
	public static string ViewClass(ElementKind kind)
	{
		switch (kind)
		{
			case ElementKind.Column:
				return "UIStackView";
			case ElementKind.Text:
				return "UILabel";
			case ElementKind.Button:
				return "UIButton";
			case ElementKind.TextField:
				return "UITextField";
			case ElementKind.Switch:
				return "UISwitch";
			case ElementKind.Scroll:
			case ElementKind.List:
				return "UIScrollView";
			case ElementKind.Image:
				return "UIImageView";
			case ElementKind.Host:
				return "UIView";
			default:
				throw new InvalidOperationException("Unknown element kind.");
		}
	}

	public static readonly string[] Modules =
	[
		"clipboard.Copy",
		"haptics.Confirm",
		"share.Text",
		"storage.Set",
		"theme.Mode",
	];

	public static bool Applies(Style style) =>
		style.Padding != 0
		|| style.Margin != 0
		|| style.Gap != 0
		|| style.Grow != 0
		|| style.Align != Align.Start
		|| style.FontSize != 0
		|| style.Weight != FontWeight.Regular
		|| style.Radius != 0
		|| style.Color != 0
		|| style.Background != 0;
}
