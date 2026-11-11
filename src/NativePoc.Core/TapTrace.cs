namespace NativePoc;

public static class TapTrace
{
	public static bool Enabled { get; set; }

	static long _input;
	static long _script;
	static long _renderIn;
	static long _prepOut;
	static long _renderOut;
	static int _renders;
	static int _bind;

	public static void Input()
	{
		if (!Enabled)
			return;
		_input = Stamp();
		_script = 0;
		_renderIn = 0;
		_prepOut = 0;
		_renderOut = 0;
		_renders = 0;
		_bind = 0;
	}

	public static void Script()
	{
		if (!Enabled)
			return;
		if (_input == 0)
			_input = Stamp();
		_script = Stamp();
	}

	public static void RenderIn()
	{
		if (!Enabled)
			return;
		if (_renderIn == 0)
			_renderIn = Stamp();
		_renders++;
	}

	public static void PrepDone()
	{
		if (!Enabled)
			return;
		_prepOut = Stamp();
	}

	public static void RenderOut()
	{
		if (!Enabled)
			return;
		_renderOut = Stamp();
	}

	public static void Bind(bool hit)
	{
		if (!Enabled)
			return;
		_bind = hit ? 2 : 1;
	}

	public static string Line(long actionUs)
	{
		if (!Enabled || _script == 0)
			return "";
		var input = Us(_input, _script);
		var renderStart = _renderIn == 0 ? _renderOut : _renderIn;
		var vm = Us(_script, renderStart);
		var prep = Us(_renderIn, _prepOut);
		var render = Us(_renderIn, _renderOut);
		var tail = _renderOut == 0 ? 0 : Us(_renderOut, Stamp());
		var bind = _bind switch
		{
			2 => "hit",
			1 => "miss",
			_ => "none",
		};
		return "tap-phase input=" + input.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " vm=" + vm.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " prep=" + prep.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " render=" + render.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " tail=" + tail.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " bind=" + bind
			+ " renders=" + _renders.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " action=" + actionUs.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	static long Stamp() => System.Diagnostics.Stopwatch.GetTimestamp();

	static long Us(long from, long to)
	{
		if (from == 0 || to < from)
			return 0;
		return (to - from) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
	}
}
