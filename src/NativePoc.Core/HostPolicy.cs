using System.Globalization;

namespace NativePoc;

/* Decisions shared by AndroidHost and the UIKit host. The numbers are the
   ones PlatformConformance and cnative inspect already require. */
public static class HostPolicy
{
	public const int TouchFloorDp = 48;

	public static bool TextIsHidden(string value) => value.Length == 0;

	public static string TextAccessibility(string value) => value;

	public static int TouchFloor(byte mask) => mask == 0 ? 0 : TouchFloorDp;

	public static bool IsHeading(int fontSize) => fontSize >= 20;

	public static bool FieldIsMultiline(int lines, Keyboard keyboard) =>
		lines > 1 && keyboard != Keyboard.Password;

	public static string FieldView(int lines, Keyboard keyboard) =>
		FieldIsMultiline(lines, keyboard) ? "UITextView" : "UITextField";

	public static string RowText(Element row)
	{
		ArgumentNullException.ThrowIfNull(row);
		if (row is Text text)
			return text.Value;
		if (row is Column column)
		{
			foreach (var child in column.Children)
			{
				if (child is Text nested && nested.Value.Length > 0)
					return nested.Value;
			}
		}

		return "";
	}

	public static string ListSummary(IReadOnlyList<Element> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		return "count=" + rows.Count.ToString(CultureInfo.InvariantCulture);
	}

	public static bool RemoveAllowed(ElementKind kind) =>
		kind is ElementKind.Column or ElementKind.Scroll or ElementKind.List;

	public static bool InsertHonorsIndex(ElementKind kind) => RemoveAllowed(kind);
}
