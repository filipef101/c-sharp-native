using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class DoctorTests
{
	[Fact]
	public void A_ready_machine_prints_no_failures()
	{
		var text = Doctor.Report(new DoctorFacts(
			"10.0.14",
			"android                    36.0.0                SDK 10.0.100\n",
			"adb",
			"List of devices attached\nemulator-5554\tdevice\n",
			"/home/example/.nuget/packages/runtime.android-x64.microsoft.dotnet.ilcompiler"));
		Assert.DoesNotContain("FAIL ", text);
		Assert.Contains("OK dotnet: 10.0.14", text);
		Assert.Contains("OK android workload: installed.", text);
		Assert.Contains("OK adb: adb", text);
		Assert.Contains("OK device: emulator-5554", text);
		Assert.Contains("OK NativeAOT: /home/example/.nuget/packages/runtime.android-x64.microsoft.dotnet.ilcompiler", text);
		Assert.Contains("OK listener apk: no bin/listener publish yet.", text);
		Assert.Contains("OK stale publish: no pre-split bin/Debug publish apk.", text);
	}

	[Fact]
	public void Missing_tools_name_the_next_command()
	{
		var text = Doctor.Report(new DoctorFacts("8.0.100", "maui\n", null, null, null));
		Assert.Contains("FAIL dotnet: found 8.0.100", text);
		Assert.Contains("Install the .NET 10 SDK.", text);
		Assert.Contains("FAIL android workload:", text);
		Assert.Contains("`dotnet workload install android`", text);
		Assert.Contains("FAIL adb:", text);
		Assert.Contains("ANDROID_HOME", text);
		Assert.Contains("FAIL device:", text);
		Assert.Contains("FAIL NativeAOT:", text);
		Assert.Contains("`dotnet workload install android`", text);
	}

	[Fact]
	public void An_offline_emulator_is_not_a_device()
	{
		var text = Doctor.Report(new DoctorFacts(
			"10.0.14",
			"android 36.0.0\n",
			"/sdk/platform-tools/adb",
			"List of devices attached\nemulator-5554\toffline\n",
			null));
		Assert.Contains("FAIL device:", text);
		Assert.Contains("`emulator -list-avds`", text);
		Assert.Contains("OK NativeAOT: the android workload is installed.", text);
	}

	[Fact]
	public void A_listener_apk_without_the_provider_fails()
	{
		var text = Doctor.Report(new DoctorFacts(
			"10.0.14",
			"android 36.0.0\n",
			"adb",
			"List of devices attached\nemulator-5554\tdevice\n",
			"/ilc",
			false,
			true));
		Assert.Contains("FAIL listener apk: classes.dex has no NativeAotRuntimeProvider.", text);
		Assert.Contains("Delete bin/listener and publish the listener again.", text);
		Assert.Contains("FAIL stale publish:", text);
		Assert.Contains("cnative run android --listener removes it", text);
	}

	[Fact]
	public void A_listener_apk_with_the_provider_is_ok()
	{
		var text = Doctor.Report(new DoctorFacts(
			"10.0.14",
			"android 36.0.0\n",
			"adb",
			"List of devices attached\nemulator-5554\tdevice\n",
			"/ilc",
			true,
			false));
		Assert.Contains("OK listener apk: NativeAotRuntimeProvider is in classes.dex.", text);
		Assert.Contains("OK stale publish: no pre-split bin/Debug publish apk.", text);
		Assert.DoesNotContain("FAIL ", text);
	}
}
