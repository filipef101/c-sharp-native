using NativePoc.ReloadGen;

namespace NativePoc.Devx.Tests;

public sealed class BindingThreadTests
{
	const string Header = """
		class JniMethodAttribute : System.Attribute
		{
			public JniMethodAttribute(string name, string signature) {}
			public bool Blocking { get; set; }
		}
		static class DeviceBattery
		{
			[JniMethod("getIntProperty", "(I)I", Blocking = true)]
			public static string Level() => "1";
			public static void Read(string screen, string field)
			{
				System.Threading.Tasks.Task.Run(() => Level());
			}
		}
		""";

	[Fact]
	public void A_blocking_call_in_a_button_is_NP265()
	{
		var source = Header + """
			static class BindScreen
			{
				public static void Render()
				{
					var button = new Button(() => DeviceBattery.Level());
				}
			}
			""";
		var hit = BindingThread.Analyze(source);
		Assert.Contains(hit, line => line.StartsWith("NP265 ", StringComparison.Ordinal) && line.Contains("DeviceBattery.Level", StringComparison.Ordinal));
	}

	[Fact]
	public void The_same_call_inside_Task_Run_is_clean()
	{
		var source = Header + """
			static class BindScreen
			{
				public static void Go()
				{
					System.Threading.Tasks.Task.Run(() => DeviceBattery.Level());
				}
			}
			""";
		Assert.Empty(BindingThread.Analyze(source));
	}

	[Fact]
	public void An_off_thread_entry_in_a_button_is_clean()
	{
		var source = Header + """
			static class BindScreen
			{
				public static void Render()
				{
					var button = new Button(() => DeviceBattery.Read("bind", "Note"));
				}
			}
			""";
		Assert.Empty(BindingThread.Analyze(source));
	}
}
