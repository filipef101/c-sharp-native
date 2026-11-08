using Android.App;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Sparkline.Adapter;

namespace Sparkline.Probe;

[Activity(Label = "Sparkline probe", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
	static int _nextId;

	SparklineViewAdapter? _adapter;
	TextView? _status;
	int _id;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		_id = Interlocked.Increment(ref _nextId);
		Log.Info(
			SparklineViewAdapter.LogTag,
			"create id=" + _id
			+ " pid=" + Process.MyPid()
			+ " dynamic=" + System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
			+ " compiled=" + System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled);
		var series = SparklineSeries.FromValues([1f, 4f, 2f, 7f, 3f, 8f, 5f, 6f]);
		_adapter = new SparklineViewAdapter(this, series, _id);
		_adapter.Scrubbed += OnScrubbed;
		var density = Resources?.DisplayMetrics?.Density ?? 1f;
		int Dp(int value) => (int)(value * density);
		var root = new LinearLayout(this)
		{
			Orientation = Orientation.Vertical,
		};
		root.SetPadding(Dp(16), Dp(24), Dp(16), Dp(16));
		root.SetBackgroundColor(Android.Graphics.Color.White);
		var title = new TextView(this)
		{
			Text = "Spark 1.2.0 via .NET Android binding",
			TextSize = 18f,
		};
		_status = new TextView(this)
		{
			Text = "waiting for scrub",
			TextSize = 22f,
			ContentDescription = "status",
		};
		_status.SetTextColor(Android.Graphics.Color.Black);
		var spark = _adapter.View;
		spark.LayoutParameters = new LinearLayout.LayoutParams(
			ViewGroup.LayoutParams.MatchParent,
			Dp(220));
		var recreate = new Button(this)
		{
			Text = "Recreate activity",
			ContentDescription = "recreate",
		};
		recreate.Click += (_, _) => Recreate();
		var button = new Button(this)
		{
			Text = "Unsubscribe",
			ContentDescription = "unsubscribe",
		};
		button.Click += (_, _) =>
		{
			if (_adapter is null)
				return;
			_adapter.Scrubbed -= OnScrubbed;
			_adapter.Unsubscribe();
			if (_status is not null)
				_status.Text = "unsubscribed";
		};
		root.AddView(title);
		root.AddView(_status);
		root.AddView(spark);
		root.AddView(recreate);
		root.AddView(button);
		SetContentView(root);
	}

	void OnScrubbed(SparklineScrub scrub)
	{
		if (scrub.Ended)
		{
			Log.Info(SparklineViewAdapter.LogTag, "emit ended id=" + _id);
			if (_status is not null)
				_status.Text = "scrub ended";
			return;
		}
		Log.Info(
			SparklineViewAdapter.LogTag,
			"emit index=" + scrub.Index + " value=" + scrub.Value + " id=" + _id);
		if (_status is not null)
			_status.Text = "index " + scrub.Index + " value " + scrub.Value;
	}

	protected override void OnDestroy()
	{
		Log.Info(SparklineViewAdapter.LogTag, "destroy id=" + _id);
		if (_adapter is not null)
		{
			_adapter.Scrubbed -= OnScrubbed;
			_adapter.Dispose();
			_adapter = null;
		}
		base.OnDestroy();
	}
}
