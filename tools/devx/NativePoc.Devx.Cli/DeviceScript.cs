namespace NativePoc.Devx;

enum DeviceVerb
{
	Screen,
	Enter,
	Press,
	State,
	Tree,
}

readonly record struct DeviceStep(int Line, DeviceVerb Verb, string Target, string Text);

static class DeviceScript
{
	public static IReadOnlyList<DeviceStep> Parse(string source)
	{
		var steps = new List<DeviceStep>();
		var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (var index = 0; index < lines.Length; index++)
		{
			var raw = lines[index].Trim();
			if (raw.Length == 0 || raw.StartsWith('#'))
				continue;
			var line = index + 1;
			var split = raw.IndexOf(' ');
			var verb = split < 0 ? raw : raw[..split];
			var rest = split < 0 ? "" : raw[(split + 1)..].Trim();
			switch (verb)
			{
				case "screen":
					if (rest is not ("counter" or "packing"))
						throw new UsageException("Line " + line + ": screen is counter or packing.");
					steps.Add(new DeviceStep(line, DeviceVerb.Screen, rest, ""));
					break;
				case "enter":
				case "type":
					steps.Add(Pair(line, DeviceVerb.Enter, rest, "enter <id> <text>"));
					break;
				case "press":
				case "tap":
					if (rest.Length == 0 || rest.Contains(' '))
						throw new UsageException("Line " + line + ": press <id>");
					steps.Add(new DeviceStep(line, DeviceVerb.Press, rest, ""));
					break;
				case "state":
					if (rest.Length == 0)
						throw new UsageException("Line " + line + ": state <text>");
					steps.Add(new DeviceStep(line, DeviceVerb.State, rest, ""));
					break;
				case "tree":
					if (rest.Length == 0)
						throw new UsageException("Line " + line + ": tree <text>");
					steps.Add(new DeviceStep(line, DeviceVerb.Tree, rest, ""));
					break;
				default:
					throw new UsageException("Line " + line + ": unknown step " + verb + ".");
			}
		}

		if (steps.Count == 0)
			throw new UsageException("The script has no steps.");
		return steps;
	}

	static DeviceStep Pair(int line, DeviceVerb verb, string rest, string usage)
	{
		var split = rest.IndexOf(' ');
		if (split <= 0 || split == rest.Length - 1)
			throw new UsageException("Line " + line + ": " + usage);
		return new DeviceStep(line, verb, rest[..split], rest[(split + 1)..]);
	}
}
