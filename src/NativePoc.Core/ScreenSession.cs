namespace NativePoc;

internal sealed class ScreenSession : IPlatform
{
	readonly RecordProgram.Bound _bound;
	readonly List<string> _every = new();
	readonly Dictionary<string, int> _elapsed = new(StringComparer.Ordinal);
	readonly List<string> _effects = new();
	readonly List<string> _copies = new();
	readonly List<string> _stack = new();
	readonly List<string> _modules = new();
	bool _requested;
	bool _rendering;
	Element? _root;
	int _steps;

	ScreenSession(RecordProgram.Bound bound)
	{
		_bound = bound;
	}

	public static ScreenSession Open(byte[] program)
	{
		ArgumentNullException.ThrowIfNull(program);
		var session = new ScreenSession(RecordProgram.Bind(program, new RecordProgram.Bag()));
		session.Render();
		return session;
	}

	public Element Root => _root ?? throw new InvalidOperationException("The session has no tree.");

	public RecordProgram.Bag Bag => _bound.State;

	public IReadOnlyList<string> Effects => _effects;

	public IReadOnlyList<string> Copies => _copies;

	public IReadOnlyList<string> Stack => _stack;

	public IReadOnlyList<string> Modules => _modules;

	public int Steps => _steps;

	public string Tree() => ScreenSnapshot.FormatTree(_root);

	public string State() => _bound.FormatState();

	public string Enter(string id, string text) => Act(() => ElementIds.Invoke(Root, id, text));

	public string Tap(string id) => Act(() => ElementIds.Invoke(Root, id, null));

	public string Submit(string id) => Act(() => ElementIds.Submit(Root, id));

	public string Swipe(string id) => Act(() => ElementIds.Swipe(Root, id));

	public string LongPress(string id) => Act(() => ElementIds.LongPress(Root, id));

	public string Refresh(string id) => Act(() => ElementIds.Refresh(Root, id));

	public int Opacity(string id)
	{
		var element = ElementIds.Require(Root, id);
		if (element.Motion is not { } motion)
			return 100;
		_elapsed.TryGetValue(id, out var elapsed);
		return MotionMath.Lerp(motion.FromOpacity, motion.Opacity, elapsed, motion.Duration);
	}

	public int Step(int milliseconds = 100)
	{
		if (milliseconds < 0)
			throw new ArgumentOutOfRangeException(nameof(milliseconds));
		foreach (var name in _every)
			_bound.State.AddInt(name, 1);
		if (_root is not null)
		{
			foreach (var node in ElementIds.Flatten(_root))
			{
				if (node.Element.Motion is null)
					continue;
				_elapsed.TryGetValue(node.Id, out var elapsed);
				_elapsed[node.Id] = elapsed + milliseconds;
			}
		}

		_steps++;
		Render();
		return _steps;
	}

	public void CopyNonce(string nonce) => _copies.Add(nonce);

	public void RequestRender() => _requested = true;

	public void Push(string screen) => _stack.Add(screen);

	public void Pop()
	{
		if (_stack.Count > 0)
			_stack.RemoveAt(_stack.Count - 1);
	}

	public void Effect(string property, string resource) => _effects.Add(property + "=" + resource);

	public void Module(string module, string method, string[] args) => _modules.Add(module + "." + method);

	public void Every(string property)
	{
		if (!_every.Contains(property))
			_every.Add(property);
	}

	string Act(Func<string> action)
	{
		var text = action();
		if (_requested)
			Render();
		return text;
	}

	void Render()
	{
		if (_rendering)
			return;
		_rendering = true;
		_requested = false;
		try
		{
			_root = _bound.Render(this);
		}
		finally
		{
			_rendering = false;
		}
	}
}
