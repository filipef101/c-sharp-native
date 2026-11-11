using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class ThemeRuntime
{
	static readonly Dictionary<View, uint> _explicit = new();

	public static string Mode { get; private set; } = "system";

	public static void Load(Activity activity)
	{
		var stored = AppStorage.Get(activity, "theme");
		Mode = stored is "dark" or "light" or "system" ? stored : "system";
	}

	public static void Set(Activity activity, string mode)
	{
		Mode = ThemeTokens.Persist(mode);
		AppStorage.Set(activity, "theme", Mode);
		if (Mode == "system")
		{
			ScreenNav.Shared.Bag("settings").WriteBool("Dark", IsDark(activity));
		}

		Paint(activity, activity.Window?.DecorView);
		Android.Util.Log.Info(MainActivity.Tag, "theme mode=" + Mode + " dark=" + (IsDark(activity) ? "1" : "0"));
	}

	public static bool IsDark(Activity activity)
	{
		if (Mode == "dark")
			return true;
		if (Mode == "light")
			return false;
		var mask = activity.Resources?.Configuration?.UiMode ?? 0;
		return (mask & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
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
		var text = unchecked((int)ThemeTokens.Text(dark));
		var page = unchecked((int)ThemeTokens.Page(dark));
		activity.Window?.DecorView?.SetBackgroundColor(new Color(page));
		if (root is not null)
			Walk(root, text);
	}

	static void Walk(View view, int text)
	{
		if (view is TextView label && !_explicit.ContainsKey(label))
			label.SetTextColor(new Color(text));
		if (view is not ViewGroup group)
			return;
		for (var index = 0; index < group.ChildCount; index++)
		{
			var child = group.GetChildAt(index);
			if (child is not null)
				Walk(child, text);
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
		ThemeRuntime.Paint(activity, activity.Window?.DecorView);
		Android.Util.Log.Info(MainActivity.Tag, "theme mode=" + ThemeRuntime.Mode + " dark=" + (ThemeRuntime.IsDark(activity) ? "1" : "0"));
	}
}
