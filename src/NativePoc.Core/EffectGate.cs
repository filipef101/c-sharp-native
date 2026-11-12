namespace NativePoc;

public static class EffectContext
{
	[ThreadStatic]
	static RecordProgram.Bag? _bag;

	internal static RecordProgram.Bag? Bag
	{
		get => _bag;
		set => _bag = value;
	}
}

public sealed class EffectGate
{
	readonly Dictionary<string, string> _resource = new(StringComparer.Ordinal);
	readonly HashSet<string> _applied = new(StringComparer.Ordinal);

	public static EffectGate Shared { get; } = new();

	public void NoteReload()
	{
	}

	public bool TryStart(string property, string resource)
	{
		if (_resource.TryGetValue(property, out var current))
		{
			if (string.Equals(current, resource, StringComparison.Ordinal))
				return false;
			_resource[property] = resource;
			_applied.Remove(property + "\0" + current);
			return true;
		}

		_resource[property] = resource;
		return true;
	}

	public bool TryApply(string property, string resource)
	{
		if (!_resource.TryGetValue(property, out var current) || !string.Equals(current, resource, StringComparison.Ordinal))
			return false;
		return _applied.Add(property + "\0" + resource);
	}

	public int ApplyCount(string property, string resource) =>
		_applied.Contains(property + "\0" + resource) ? 1 : 0;
}
