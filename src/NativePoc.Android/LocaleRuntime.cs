using Android.Views;

namespace NativePoc;

static class LocaleRuntime
{
	public static void Apply(Activity activity)
	{
		var extra = activity.Intent?.GetStringExtra("nativepoc_locale");
		var tag = string.IsNullOrEmpty(extra) ? DeviceTag(activity) : extra;
		StringTable.Use(tag);
		Android.Util.Log.Info(MainActivity.Tag, "startup locale=" + tag + " rtl=" + (StringTable.RightToLeft ? "1" : "0"));
		ApplyDirection(activity);
	}

	public static void ApplyDirection(Activity activity)
	{
		if (activity.Window?.DecorView is View decor)
			ApplyDirection(decor);
	}

	public static void ApplyDirection(View view)
	{
		var next = Direction();
		if (view.LayoutDirection != next)
			view.LayoutDirection = next;
	}

	static LayoutDirection Direction() =>
		StringTable.RightToLeft ? LayoutDirection.Rtl : LayoutDirection.Ltr;

	static string DeviceTag(Activity activity)
	{
		var locales = activity.Resources?.Configuration?.Locales;
		if (locales is not null && !locales.IsEmpty)
		{
			var tag = locales.Get(0)?.ToLanguageTag();
			if (!string.IsNullOrEmpty(tag))
				return tag;
		}

		return "en";
	}
}
