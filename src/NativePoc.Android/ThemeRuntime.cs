using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class ThemeRuntime
{
	static readonly Dictionary<View, uint> _explicit = new();
	static readonly Color InkLight = new(unchecked((int)ThemeTokens.Text(false)));
	static readonly Color InkDark = new(unchecked((int)ThemeTokens.Text(true)));
	static readonly Color PageLight = new(unchecked((int)ThemeTokens.Page(false)));
	static readonly Color PageDark = new(unchecked((int)ThemeTokens.Page(true)));

	public static string Mode { get; private set; } = "system";

	static bool _nightKnown;
	static bool _night;

	public static void Load(Activity activity)
	{
		var stored = AppStorage.Get(activity, "theme");
		Mode = stored is "dark" or "light" or "system" ? stored : "system";
		ObserveNight(activity);
	}

	public static void ObserveNight(Activity activity)
	{
		var mask = activity.Resources?.Configuration?.UiMode ?? 0;
		_night = (mask & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
		_nightKnown = true;
	}

	public static void Set(Activity activity, string mode)
	{
		Mode = ThemeTokens.Persist(mode);
		AppStorage.Set(activity, "theme", Mode);
		if (Mode == "system")
		{
			ObserveNight(activity);
			ScreenNav.Shared.Bag("settings").WriteBool("Dark", IsDark(activity));
		}

		Paint(activity, activity.Window?.DecorView);
		Android.Util.Log.Info(MainActivity.Tag, "theme mode=" + Mode + " dark=" + (IsDark(activity) ? "1" : "0"));
	}

	public static void ApplyWindow(Activity activity)
	{
		var page = new Color(unchecked((int)ThemeTokens.Page(IsDark(activity))));
		if (activity.Window is not { } window)
			return;
		window.SetBackgroundDrawable(new ColorDrawable(page));
		window.DecorView?.SetBackgroundColor(page);
	}

	public static bool IsDark(Activity activity)
	{
		if (Mode == "dark")
			return true;
		if (Mode == "light")
			return false;
		if (!_nightKnown)
			ObserveNight(activity);
		return _night;
	}

	public static void NoteColor(View view, uint color)
	{
		if (color == 0)
			_explicit.Remove(view);
		else
			_explicit[view] = color;
	}

	public static void Paint(Activity activity, View? root)
	{
		var dark = IsDark(activity);
		var ink = dark ? InkDark : InkLight;
		var page = dark ? PageDark : PageLight;
		var decor = activity.Window?.DecorView;
		if (decor is not null && (decor.Background is not ColorDrawable painted || painted.Color.ToArgb() != page.ToArgb()))
			decor.SetBackgroundColor(page);
		if (root is not null)
			Walk(root, ink);
	}

	static void Walk(View view, Color ink)
	{
		if (view is TextView label && !_explicit.ContainsKey(label) && label.CurrentTextColor != ink.ToArgb())
			label.SetTextColor(ink);
		if (view is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				Walk(child, ink);
		}
	}
}

static class StorageSeed
{
	public static void Apply(Activity activity)
	{
		ThemeRuntime.Load(activity);
		var bag = ScreenNav.Shared.Bag("settings");
		var name = AppStorage.Get(activity, "name");
		if (name is not null)
			bag.WriteString("Name", name);
		var note = AppStorage.Get(activity, "note");
		if (note is not null)
			bag.WriteString("Note", note);
		bag.WriteBool("Dark", ThemeRuntime.IsDark(activity));
		ScreenNav.Shared.Bag("trips").WriteBool("Dark", ThemeRuntime.IsDark(activity));
		Refresh(activity, "trip");
		ThemeRuntime.Paint(activity, activity.Window?.DecorView);
		Android.Util.Log.Info(MainActivity.Tag, "theme mode=" + ThemeRuntime.Mode + " dark=" + (ThemeRuntime.IsDark(activity) ? "1" : "0"));
	}

	public static void Refresh(Activity activity, string screen)
	{
		if (screen == "settings")
		{
			var settings = ScreenNav.Shared.Bag("settings");
			Copy(activity, settings, "name", "Name");
			Copy(activity, settings, "note", "Note");
			settings.WriteBool("Dark", ThemeRuntime.IsDark(activity));
			return;
		}

		if (screen == "trips")
		{
			ScreenNav.Shared.Bag("trips").WriteBool("Dark", ThemeRuntime.IsDark(activity));
			return;
		}

		if (screen != "trip")
			return;
		var trip = ScreenNav.Shared.Bag("trip");
		Copy(activity, trip, "trip-id", "OpenId");
		Copy(activity, trip, "trip-name", "Name");
		Copy(activity, trip, "trip-note", "Note");
	}

	static void Copy(Activity activity, RecordProgram.Bag bag, string key, string property)
	{
		var value = AppStorage.Get(activity, key);
		if (value is not null)
			bag.WriteString(property, value);
	}
}
