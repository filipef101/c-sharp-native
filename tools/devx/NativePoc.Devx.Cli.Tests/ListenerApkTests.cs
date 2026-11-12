using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ListenerApkTests
{
	[Fact]
	public void A_newer_apk_skips_publish_when_only_a_screen_changed()
	{
		var root = Path.Combine(Path.GetTempPath(), "cnative-apk-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, "screens"));
		var project = Path.Combine(root, "cnative-app.csproj");
		var nuget = Path.Combine(root, "nuget.config");
		var apk = Path.Combine(root, "app-Signed.apk");
		var screen = Path.Combine(root, "screens", "HomeScreen.cs");
		File.WriteAllText(project, "<Project />");
		File.WriteAllText(nuget, "<configuration />");
		File.WriteAllText(apk, "apk");
		var written = DateTime.UtcNow.AddMinutes(-5);
		File.SetLastWriteTimeUtc(project, written);
		File.SetLastWriteTimeUtc(nuget, written);
		File.SetLastWriteTimeUtc(apk, written.AddMinutes(1));
		File.WriteAllText(screen, "Title");
		File.SetLastWriteTimeUtc(screen, written.AddMinutes(2));

		Assert.True(AppLoop.ListenerApkCurrent(project, apk));

		File.SetLastWriteTimeUtc(project, written.AddMinutes(3));
		Assert.False(AppLoop.ListenerApkCurrent(project, apk));
		Assert.False(AppLoop.ListenerApkCurrent(project, null));
		Directory.Delete(root, recursive: true);
	}

	[Fact]
	public void An_installed_apk_matches_on_the_device_hash()
	{
		var hash = new string('a', 64);
		Assert.True(DeviceInstall.SameHash(hash, hash.ToUpperInvariant() + "  /data/app/base.apk\n"));
		Assert.False(DeviceInstall.SameHash(hash, "missing"));
		Assert.False(DeviceInstall.SameHash("short", hash));
		Assert.Equal("/data/app/~~x==/base.apk", DeviceInstall.PackagePath("package:/data/app/~~x==/base.apk\n"));
		Assert.Equal("/data/app/base.apk", DeviceInstall.PackagePath("package:/data/app/split.apk\npackage:/data/app/base.apk\n"));
		Assert.Null(DeviceInstall.PackagePath(""));
	}

	[Fact]
	public void An_unchanged_screen_is_not_pushed_again()
	{
		var root = Path.Combine(Path.GetTempPath(), "cnative-stamp-" + Guid.NewGuid().ToString("N"));
		var screen = Path.Combine(root, "screens", "HomeScreen.cs");
		Directory.CreateDirectory(Path.GetDirectoryName(screen)!);
		File.WriteAllText(screen, "Title");
		var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(screen, written);
		var stamp = AppLoop.StampPath(root);
		Directory.CreateDirectory(Path.GetDirectoryName(stamp)!);
		File.WriteAllText(stamp, "run\n");
		File.SetLastWriteTimeUtc(stamp, written.AddMinutes(1));

		Assert.Empty(AppLoop.NewerScreens(root, ["screens/HomeScreen.cs"], stamp));

		File.SetLastWriteTimeUtc(screen, written.AddMinutes(2));
		Assert.Equal("screens/HomeScreen.cs", Assert.Single(AppLoop.NewerScreens(root, ["screens/HomeScreen.cs"], stamp)));
		Directory.Delete(root, recursive: true);
	}
}
