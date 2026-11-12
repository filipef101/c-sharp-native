namespace NativePoc;

[AttributeUsage(AttributeTargets.Class)]
public sealed class JniTypeAttribute : Attribute
{
	public JniTypeAttribute(string className)
	{
		ArgumentException.ThrowIfNullOrEmpty(className);
		ClassName = className;
	}

	public string ClassName { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class JniMethodAttribute : Attribute
{
	public JniMethodAttribute(string name, string signature)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(signature);
		Name = name;
		Signature = signature;
	}

	public string Name { get; }

	public string Signature { get; }

	public bool Instance { get; set; }

	public string Service { get; set; } = "";

	public bool Blocking { get; set; }
}
