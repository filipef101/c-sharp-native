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
}
