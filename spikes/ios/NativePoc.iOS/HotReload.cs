#if DEBUG
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(NativePoc.HotReload))]

namespace NativePoc;

static class HotReload
{
	public static void ClearCache(Type[]? updatedTypes)
	{
	}

	public static void UpdateApplication(Type[]? updatedTypes)
	{
		if (!MetadataUpdater.IsSupported)
			return;
		ScreenController.Current?.RequestRender();
	}
}
#endif
