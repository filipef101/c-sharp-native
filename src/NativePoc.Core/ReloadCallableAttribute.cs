namespace NativePoc;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ReloadCallableAttribute : Attribute
{
	public ReloadCallableAttribute()
	{
	}

	public ReloadCallableAttribute(string space)
	{
		Space = space;
	}

	public string? Space { get; }
}
