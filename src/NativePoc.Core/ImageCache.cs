namespace NativePoc;

public static class ImageSource
{
	public static bool IsRemote(string source)
	{
		if (string.IsNullOrEmpty(source))
			return false;
		return source.StartsWith("http://", StringComparison.Ordinal)
			|| source.StartsWith("https://", StringComparison.Ordinal);
	}
}

public sealed class ImageSlot
{
	int _generation;

	public int Begin()
	{
		if (_generation == int.MaxValue)
			_generation = 0;
		return ++_generation;
	}

	public bool Current(int token) => token == _generation && token != 0;
}

public sealed class LruCache<T> where T : class
{
	readonly int _capacity;
	readonly Dictionary<string, LinkedListNode<Entry>> _map = new();
	readonly LinkedList<Entry> _order = new();

	public LruCache(int capacity)
	{
		if (capacity < 1)
			throw new ArgumentOutOfRangeException(nameof(capacity));
		_capacity = capacity;
	}

	public int Count => _map.Count;

	public bool TryGet(string key, out T value)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (!_map.TryGetValue(key, out var node))
		{
			value = null!;
			return false;
		}

		_order.Remove(node);
		_order.AddFirst(node);
		value = node.Value.Value;
		return true;
	}

	public void Add(string key, T value)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(value);
		if (_map.TryGetValue(key, out var existing))
		{
			_order.Remove(existing);
			_map.Remove(key);
		}

		var node = _order.AddFirst(new Entry(key, value));
		_map[key] = node;
		while (_map.Count > _capacity)
		{
			var last = _order.Last;
			if (last is null)
				break;
			_map.Remove(last.Value.Key);
			_order.RemoveLast();
		}
	}

	readonly record struct Entry(string Key, T Value);
}
