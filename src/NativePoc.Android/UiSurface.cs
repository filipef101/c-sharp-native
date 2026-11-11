using Android.App;

namespace NativePoc;

static class UiSurface
{
	static Activity? _activity;
	static Func<Element?>? _tree;

	public static void Bind(Activity activity, Func<Element?> tree)
	{
		_activity = activity;
		_tree = tree;
	}

	public static void Clear(Activity activity)
	{
		if (!ReferenceEquals(_activity, activity))
			return;
		_activity = null;
		_tree = null;
	}

	public static Element? Current() => _tree?.Invoke();
}
