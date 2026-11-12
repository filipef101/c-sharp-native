using System.Globalization;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class NavFocus
{
	static bool _armed;

	public static void Arm() => _armed = true;

	public static void Apply(View root)
	{
		if (!_armed)
			return;
		_armed = false;
		root.Post(() => FocusNow(root));
	}

	static void FocusNow(View root)
	{
		var heading = Find(root);
		var depth = ScreenNav.Shared.Depth.ToString(CultureInfo.InvariantCulture);
		if (heading is null)
		{
			Log.Info(MainActivity.Tag, "nav-focus missing depth=" + depth);
			return;
		}

		var acted = heading.PerformAccessibilityAction(global::Android.Views.Accessibility.Action.AccessibilityFocus, null);
		var held = heading.IsAccessibilityFocused;
		var label = heading is TextView text ? text.Text ?? "" : "";
		Log.Info(
			MainActivity.Tag,
			"nav-focus heading=" + label
			+ " acted=" + (acted ? "1" : "0")
			+ " held=" + (held ? "1" : "0")
			+ " depth=" + depth);
	}

	static View? Find(View view)
	{
		if (view is TextView text && (int)Android.OS.Build.VERSION.SdkInt >= 28)
		{
#pragma warning disable CA1416
			if (text.AccessibilityHeading)
				return text;
#pragma warning restore CA1416
		}
		if (view is not ViewGroup group)
			return null;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is null)
				continue;
			var found = Find(child);
			if (found is not null)
				return found;
		}

		return null;
	}
}
