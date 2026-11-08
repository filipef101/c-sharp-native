namespace NativePoc.Devx;

sealed class ChangeDebouncer
{
	readonly object _gate = new();
	readonly int _windowMs;
	readonly List<string> _paths = [];
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
			if (!_paths.Contains(path))
				_paths.Add(path);
			_lastMs = nowMs;
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
