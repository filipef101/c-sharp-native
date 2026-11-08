namespace NativePoc.Devx;

sealed record ChangeBatch(IReadOnlyList<string> Paths, long FirstMs);

sealed class ChangeDebouncer
{
	readonly object _gate = new();
	readonly int _windowMs;
	readonly List<string> _paths = [];
	long _firstMs;
	long _lastMs;

	public ChangeDebouncer(int windowMs)
	{
		if (windowMs < 1)
			throw new ArgumentOutOfRangeException(nameof(windowMs));
		_windowMs = windowMs;
	}

	public void Push(string path, long nowMs)
	{
		lock (_gate)
		{
			if (_paths.Count == 0)
				_firstMs = nowMs;
			if (!_paths.Contains(path))
				_paths.Add(path);
			_lastMs = nowMs;
		}
	}

	public long? Remaining(long nowMs)
	{
		lock (_gate)
		{
			if (_paths.Count == 0)
				return null;
			var elapsed = nowMs - _lastMs;
			if (elapsed >= _windowMs)
				return 0;
			return _windowMs - elapsed;
		}
	}

	public ChangeBatch? Drain(long nowMs)
	{
		lock (_gate)
		{
			if (_paths.Count == 0 || nowMs - _lastMs < _windowMs)
				return null;
			var batch = new ChangeBatch(_paths.ToArray(), _firstMs);
			_paths.Clear();
			return batch;
		}
	}

	public IReadOnlyList<string>? Poll(long nowMs)
	{
		lock (_gate)
		{
			if (_paths.Count == 0)
				return null;
			if (nowMs - _lastMs < _windowMs)
				return null;
			var copy = _paths.ToArray();
			_paths.Clear();
			return copy;
		}
	}
}
