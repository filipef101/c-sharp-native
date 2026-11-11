using System.Globalization;

namespace NativePoc;

static class EffectRuntime
{
	public static void Start(Activity activity, string property, string resource, Action refresh)
	{
		var bag = EffectContext.Bag ?? throw new InvalidOperationException("An effect ran outside a screen.");
		if (!EffectGate.Shared.TryStart(property, resource))
			return;
		Task.Run(() =>
		{
			try
			{
				if (resource == "throw")
					throw new InvalidOperationException("effect failed");
				Thread.Sleep(800);
				var text = Read(activity, resource);
				activity.RunOnUiThread(() =>
				{
					if (!EffectGate.Shared.TryApply(property, resource))
						return;
					bag.WriteString(property, text);
					Android.Util.Log.Info(MainActivity.Tag, "effect-apply " + property + " count=" + EffectGate.Shared.ApplyCount(property, resource).ToString(CultureInfo.InvariantCulture));
					refresh();
				});
			}
			catch (Exception ex)
			{
#if DEBUG
				activity.RunOnUiThread(() => DevOverlay.Show(activity, "effect", ex.Message, resource));
#else
				_ = ex;
#endif
			}
		});
	}

	static string Read(Activity activity, string resource)
	{
		try
		{
			using var input = activity.Assets?.Open(resource + ".txt")
				?? throw new IOException("The asset was not opened.");
			using var reader = new StreamReader(input);
			return reader.ReadToEnd().Trim();
		}
		catch (Exception ex) when (ex is IOException or Java.IO.IOException)
		{
			return "";
		}
	}
}

static class ClockRuntime
{
	static int _tick;
	static readonly List<(string Property, RecordProgram.Bag Bag)> _armed = new();

	public static void Arm(string property)
	{
		var bag = EffectContext.Bag ?? throw new InvalidOperationException("A timer ran outside a screen.");
		for (var index = 0; index < _armed.Count; index++)
		{
			if (_armed[index].Property == property && ReferenceEquals(_armed[index].Bag, bag))
				return;
		}

		_armed.Add((property, bag));
	}

	public static string Step(Action refresh)
	{
		var stepped = MotionPlayer.Advance(100);
		if (_armed.Count == 0)
			return "animations=" + stepped.ToString(CultureInfo.InvariantCulture) + "\nclock=0\n";
		_tick++;
		foreach (var (property, bag) in _armed)
			bag.AddInt(property, 1);
		refresh();
		return "animations=" + stepped.ToString(CultureInfo.InvariantCulture) + "\nclock=" + _tick.ToString(CultureInfo.InvariantCulture) + "\n";
	}
}
