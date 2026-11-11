using Android.Content;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace NativePoc;

static class NativeViews
{
	public static View Create(Context context, string kind)
	{
		switch (kind)
		{
			case "webview":
				return new WebView(context);
			default:
				throw new InvalidOperationException("Unknown native view '" + kind + "'. Add a direct constructor call in NativeViews. There is no reflection lookup.");
		}
	}

	public static void Apply(View view, string prop, string value)
	{
		switch (view)
		{
			case WebView web when prop == "url":
				if (value.StartsWith("http://", StringComparison.Ordinal)
					|| value.StartsWith("https://", StringComparison.Ordinal)
					|| value.StartsWith("about:", StringComparison.Ordinal))
				{
					web.LoadUrl(value);
					return;
				}

				web.LoadData(value, "text/html", "utf-8");
				return;
			default:
				throw new InvalidOperationException("Native view " + view.GetType().Name + " has no prop " + prop + ".");
		}
	}

	public static void Listen(View view, Action<string, string> emit)
	{
		if (view is WebView web)
			web.SetWebViewClient(new NavigateClient(emit));
	}

	sealed class NavigateClient : WebViewClient
	{
		readonly Action<string, string> _emit;

		public NavigateClient(Action<string, string> emit) => _emit = emit;

		public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
		{
			_emit("navigate", request?.Url?.ToString() ?? "");
			return true;
		}
	}
}

sealed class HostSlot : FrameLayout
{
	string _kind = "";
	string _prop = "";
	string _value = "";
	string _event = "";
	Action<string, string>? _onEvent;
	View? _child;

	public HostSlot(Context context)
		: base(context)
	{
	}

	public void Apply(string kind, string prop, string value, string eventName, Action<string, string>? onEvent)
	{
		var created = _child is null || !string.Equals(_kind, kind, StringComparison.Ordinal);
		if (created)
		{
			if (_child is not null)
				RemoveView(_child);
			var context = Context ?? throw new InvalidOperationException("A native view has no context.");
			_child = NativeViews.Create(context, kind);
			_kind = kind;
			_prop = "";
			_value = "";
			var density = Resources?.DisplayMetrics?.Density ?? 1f;
			var height = (int)(240 * (density <= 0 ? 1f : density));
			AddView(_child, new LayoutParams(ViewGroup.LayoutParams.MatchParent, height));
			NativeViews.Listen(_child, (name, payload) =>
			{
				if (_event.Length > 0 && !string.Equals(name, _event, StringComparison.Ordinal))
					return;
				_onEvent?.Invoke(name, payload);
			});
		}

		_event = eventName;
		_onEvent = onEvent;
		if (!created && _prop == prop && _value == value)
			return;
		_prop = prop;
		_value = value;
		NativeViews.Apply(_child!, prop, value);
	}
}
