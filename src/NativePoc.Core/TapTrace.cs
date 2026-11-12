namespace NativePoc;

public static class TapTrace
{
	public static bool Enabled { get; set; }

	static string _deferred = "";
	static bool _hasPhase;
	static int _count;
	static int _nameLength;
	static string _title = "";
	static long _eval;
	static long _diff;
	static long _reconcile;
	static long _layout;
	static long _invalidate;
	static long _theme;
	static long _paint;
	static int _paintRan;
	static long _book;
	static long _prepPhase;
	static long _dir;
	static long _content;
	static long _titleUs;
	static long _focus;
	static long _log;
	static long _after;
	static long _save;
	static int _gc0;
	static long _alloc;

	static long _input;
	static long _script;
	static long _renderIn;
	static long _prepOut;
	static long _renderOut;
	static int _renders;
	static int _bind;

	public static void Defer(string line)
	{
		if (!Enabled || line.Length == 0)
			return;
		_deferred = _deferred.Length == 0 ? line : _deferred + " | " + line;
	}

	public static string TakeDeferred()
	{
		var line = _deferred;
		_deferred = "";
		return line;
	}

	public static void NotePhase(
		int count,
		int nameLength,
		string title,
		long eval,
		long diff,
		long reconcile,
		long layout,
		long invalidate,
		long theme,
		long paint,
		int paintRan,
		long book,
		long prep,
		long dir,
		long content,
		long titleUs,
		long focus,
		long log,
		long after,
		long save,
		int gc0,
		long alloc)
	{
		if (!Enabled)
			return;
		_hasPhase = true;
		_count = count;
		_nameLength = nameLength;
		_title = title;
		_eval = eval;
		_diff = diff;
		_reconcile = reconcile;
		_layout = layout;
		_invalidate = invalidate;
		_theme = theme;
		_paint = paint;
		_paintRan = paintRan;
		_book = book;
		_prepPhase = prep;
		_dir = dir;
		_content = content;
		_titleUs = titleUs;
		_focus = focus;
		_log = log;
		_after = after;
		_save = save;
		_gc0 = gc0;
		_alloc = alloc;
	}

	public static string TakePhase()
	{
		if (!_hasPhase)
			return "";
		_hasPhase = false;
		var culture = System.Globalization.CultureInfo.InvariantCulture;
		return "rendered count=" + _count.ToString(culture)
			+ " name-length=" + _nameLength.ToString(culture)
			+ " title=" + _title
			+ " | render-phase eval=" + _eval.ToString(culture)
			+ " diff=" + _diff.ToString(culture)
			+ " reconcile=" + _reconcile.ToString(culture)
			+ " layout=" + _layout.ToString(culture)
			+ " invalidate=" + _invalidate.ToString(culture)
			+ " theme=" + _theme.ToString(culture)
			+ " paint=" + _paint.ToString(culture)
			+ " paint-ran=" + _paintRan.ToString(culture)
			+ " book=" + _book.ToString(culture)
			+ " prep=" + _prepPhase.ToString(culture)
			+ " dir=" + _dir.ToString(culture)
			+ " content=" + _content.ToString(culture)
			+ " title=" + _titleUs.ToString(culture)
			+ " focus=" + _focus.ToString(culture)
			+ " log=" + _log.ToString(culture)
			+ " after=" + _after.ToString(culture)
			+ " save=" + _save.ToString(culture)
			+ " gc0=" + _gc0.ToString(culture)
			+ " alloc=" + _alloc.ToString(culture);
	}

	public static void Input()
	{
		if (!Enabled)
			return;
		_deferred = "";
		_hasPhase = false;
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
