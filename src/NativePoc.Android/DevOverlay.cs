#if DEBUG
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;

namespace NativePoc;

static class DevOverlay
{
	static FrameLayout? _layer;

	public static bool Visible => _layer is not null;

	public static void Show(Activity activity, string id, string message, string span)
	{
		Dismiss(activity, log: false);
		var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
		var layer = new FrameLayout(activity);
		layer.SetBackgroundColor(new Color(unchecked((int)0xE61C1917)));
		layer.ContentDescription = "dev-overlay";
		var column = new LinearLayout(activity) { Orientation = Orientation.Vertical };
		var pad = (int)(16 * density);
		column.SetPadding(pad, pad, pad, pad);
		var text = new TextView(activity)
		{
			Text = span + "\n" + message,
		};
		text.SetTextColor(Color.White);
		text.ContentDescription = "dev-overlay-body";
		var button = new AndroidButton(activity) { Text = "Dismiss" };
		button.ContentDescription = "dev-overlay-dismiss";
		button.Click += (_, _) => Dismiss(activity, log: true);
		column.AddView(text);
		column.AddView(button);
		layer.AddView(column, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
		activity.AddContentView(layer, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
		_layer = layer;
		Android.Util.Log.Info(MainActivity.Tag, "dev-overlay id=" + id + " file=" + span + " message=" + message);
	}

	public static void Clear(Activity activity)
	{
		if (_layer is null)
			return;
		Dismiss(activity, log: false);
		Android.Util.Log.Info(MainActivity.Tag, "dev-overlay clear");
	}

	public static void Dismiss(Activity activity, bool log)
	{
		if (_layer is null)
			return;
		(_layer.Parent as ViewGroup)?.RemoveView(_layer);
		_layer = null;
		if (log)
			Android.Util.Log.Info(MainActivity.Tag, "dev-overlay dismissed");
	}
}
#endif
