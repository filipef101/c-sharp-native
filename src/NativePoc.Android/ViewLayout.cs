using System.Globalization;
using System.Text;
using Android.App;
using Android.OS;
using Android.Views;

namespace NativePoc;

static class ViewLayout
{
	public static string Format(ViewReconciler<View>? reconciler)
	{
		if (reconciler?.Current is not Element element || reconciler.Root is null)
			return "";
		var builder = new StringBuilder();
		foreach (var node in ElementIds.Flatten(element))
		{
			var view = reconciler.ViewAt(node.Path);
			var point = new int[2];
			view.GetLocationOnScreen(point);
			builder.Append("id=").Append(node.Id);
			builder.Append(" left=").Append(point[0].ToString(CultureInfo.InvariantCulture));
			builder.Append(" top=").Append(point[1].ToString(CultureInfo.InvariantCulture));
			builder.Append(" width=").Append(view.Width.ToString(CultureInfo.InvariantCulture));
			builder.Append(" height=").Append(view.Height.ToString(CultureInfo.InvariantCulture));
			builder.Append('\n');
		}

		return builder.ToString();
	}

	public static string OnMain(Activity activity, Func<string> read)
	{
		if (Looper.MainLooper?.IsCurrentThread == true)
			return read();
		var answer = "";
		using var done = new ManualResetEventSlim(false);
		activity.RunOnUiThread(() =>
		{
			try
			{
				answer = read();
			}
			finally
			{
				done.Set();
			}
		});
		if (!done.Wait(TimeSpan.FromSeconds(2)))
			throw new InvalidOperationException("Layout did not answer.");
		return answer;
	}
}
