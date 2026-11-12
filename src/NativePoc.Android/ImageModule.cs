#if NATIVEPOC_IMAGE
using System.Runtime.CompilerServices;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class ImageModule
{
	const int Capacity = 32;
	static readonly LruCache<Bitmap> Cache = new(Capacity);
	static readonly ConditionalWeakTable<ImageView, ImageSlot> Slots = new();
	static readonly object Gate = new();

	public static void Show(ImageView image, string source, Context context, string description, bool decorative)
	{
		var slot = Slots.GetValue(image, static _ => new ImageSlot());
		var token = slot.Begin();
		var target = TargetPx(image, context);
		image.Visibility = ViewStates.Visible;
		image.SetScaleType(ImageView.ScaleType.CenterCrop);
		PictureBinder.Describe(image, source, description, decorative);
		var key = source + "@" + target.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Bitmap? cached = null;
		lock (Gate)
		{
			if (Cache.TryGet(key, out var hit))
				cached = hit;
		}

		if (cached is not null)
		{
			image.SetImageBitmap(cached);
			Android.Util.Log.Info(MainActivity.Tag, "image-cache hit source=" + source);
			return;
		}

		image.SetImageDrawable(new ColorDrawable(new Color(unchecked((int)0xFFE6E6E6))));
		Android.Util.Log.Info(MainActivity.Tag, "image-placeholder source=" + source);
		var app = context.ApplicationContext ?? context;
		ThreadPool.QueueUserWorkItem(_ =>
		{
			Bitmap? bitmap = null;
			try
			{
				bitmap = Load(app, source, target);
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			}

			if (bitmap is not null)
			{
				lock (Gate)
					Cache.Add(key, bitmap);
			}

			image.Post(() =>
			{
				if (!slot.Current(token))
					return;
				PictureBinder.Describe(image, source, description, decorative);
				if (bitmap is null)
				{
					image.SetImageDrawable(new ColorDrawable(new Color(unchecked((int)0xFFB00020))));
					image.Visibility = ViewStates.Visible;
					Android.Util.Log.Info(MainActivity.Tag, "image-fail source=" + source);
					return;
				}

				image.SetImageBitmap(bitmap);
				image.Visibility = ViewStates.Visible;
				Android.Util.Log.Info(
					MainActivity.Tag,
					"image-placed source=" + source
					+ " view=" + image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "x" + image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ " clip=" + (image.ClipToOutline ? "1" : "0")
					+ " elevation=" + image.Elevation.ToString(System.Globalization.CultureInfo.InvariantCulture));
			});
		});
	}

	static int TargetPx(ImageView image, Context context)
	{
		var laid = Math.Max(image.Width, image.Height);
		if (laid <= 0)
			laid = Math.Max(image.MinimumWidth, image.MinimumHeight);
		if (laid <= 0 && image.LayoutParameters is { Width: > 0 } layout)
			laid = Math.Max(layout.Width, layout.Height);
		if (laid > 0)
			return laid;
		var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
		if (density <= 0)
			density = 1f;
		return Math.Max(1, (int)(HostPolicy.TouchFloorDp * density));
	}

	static Bitmap? Load(Context context, string source, int target)
	{
		if (ImageSource.IsRemote(source))
		{
			var bytes = Download(source);
			if (bytes is null)
				return null;
			return Decode(bytes, target);
		}

		var id = context.Resources?.GetIdentifier(source, "drawable", context.PackageName) ?? 0;
		if (id == 0)
			return null;
		using var stream = context.Resources?.OpenRawResource(id);
		if (stream is null)
			return null;
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		var raw = buffer.ToArray();
		return raw.Length == 0 ? null : Decode(raw, target);
	}

	static Bitmap? Decode(byte[] bytes, int target)
	{
		var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
		BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
		var sample = ImageSample.InSampleSize(bounds.OutWidth, bounds.OutHeight, target);
		Android.Util.Log.Info(
			MainActivity.Tag,
			"image-sample in=" + bounds.OutWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "x" + bounds.OutHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " sample=" + sample.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " target=" + target.ToString(System.Globalization.CultureInfo.InvariantCulture));
		var options = new BitmapFactory.Options { InSampleSize = sample };
		var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
		if (bitmap is null)
			return null;
		var config = bitmap.GetConfig();
		Android.Util.Log.Info(
			MainActivity.Tag,
			"image-bitmap decoded=" + bitmap.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "x" + bitmap.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ " config=" + (config is null ? "none" : config.ToString())
			+ " target=" + target.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return bitmap;
	}

	static byte[]? Download(string url)
	{
		Java.Net.HttpURLConnection? connection = null;
		try
		{
			connection = (Java.Net.HttpURLConnection)new Java.Net.URL(url).OpenConnection()!;
			connection.RequestMethod = "GET";
			connection.ConnectTimeout = 8000;
			connection.ReadTimeout = 8000;
			var code = (int)connection.ResponseCode;
			if (code is < 200 or > 299)
				return null;
			using var input = connection.InputStream;
			if (input is null)
				return null;
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			int read;
			while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
			{
				buffer.Write(chunk, 0, read);
				if (buffer.Length > 4_000_000)
					return null;
			}

			return buffer.Length == 0 ? null : buffer.ToArray();
		}
		catch (Exception ex)
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			return null;
		}
		finally
		{
			try
			{
				connection?.Disconnect();
			}
			catch (Exception)
			{
			}
		}
	}
}
#endif
