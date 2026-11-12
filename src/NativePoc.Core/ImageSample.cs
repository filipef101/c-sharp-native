namespace NativePoc;

internal static class ImageSample
{
	public static int InSampleSize(int width, int height, int target)
	{
		if (width < 1 || height < 1 || target < 1)
			return 1;
		var sample = 1;
		var halfWidth = width / 2;
		var halfHeight = height / 2;
		while (sample < 32 && halfHeight / sample >= target && halfWidth / sample >= target)
			sample *= 2;
		return sample;
	}
}
