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

	static int _generation = -1;
	static View? _appliedView;
	static View? _appliedDecor;

	public static void ApplyDirection(Activity activity)
	{
		if (activity.Window?.DecorView is not View decor)
			return;
		var generation = StringTable.RightToLeft ? 1 : 0;
		if (_generation == generation && ReferenceEquals(_appliedDecor, decor))
			return;
		ApplyView(decor, generation);
		_appliedDecor = decor;
	}

	public static void ApplyDirection(View view)
	{
		var generation = StringTable.RightToLeft ? 1 : 0;
		if (_generation == generation && ReferenceEquals(_appliedView, view))
			return;
		ApplyView(view, generation);
		_appliedView = view;
	}

	static void ApplyView(View view, int generation)
	{
		var next = generation == 1 ? LayoutDirection.Rtl : LayoutDirection.Ltr;
		if (view.LayoutDirection != next)
			view.LayoutDirection = next;
		_generation = generation;
	}

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
