using Android.App;

namespace NativePoc;

static class UiSurface
{
	static Activity? _activity;
	static Func<Element?>? _tree;
	static Func<string>? _layout;

	public static void Bind(Activity activity, Func<Element?> tree, Func<string>? layout)
	{
		_activity = activity;
		_tree = tree;
		_layout = layout;
	}

	public static void Clear(Activity activity)
	{
		if (!ReferenceEquals(_activity, activity))
			return;
		_activity = null;
		_tree = null;
		_layout = null;
	}

	public static Element? Current() => _tree?.Invoke();

	public static string Layout()
	{
		var activity = _activity;
		var layout = _layout;
		if (activity is null || layout is null)
			return "";
		return ViewLayout.OnMain(activity, layout);
	}
}
