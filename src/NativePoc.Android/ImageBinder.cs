using System.Runtime.CompilerServices;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class PictureBinder
{
	public static void Show(ImageView image, string source, Context context)
	{
		if (source.Length == 0)
		{
			image.SetImageDrawable(null);
			image.ContentDescription = null;
			image.Visibility = ViewStates.Gone;
			return;
		}

#if NATIVEPOC_HTTP
		if (ImageSource.IsRemote(source) || BitmapResource(context, source))
		{
			RemoteImages.Apply(image, source, context);
			return;
		}
#endif
		var id = context.Resources?.GetIdentifier(source, "drawable", context.PackageName) ?? 0;
		if (id == 0)
			throw new InvalidOperationException("Drawable " + source + " was not found.");
		image.SetImageResource(id);
		image.ContentDescription = source;
		image.Visibility = ViewStates.Visible;
	}

#if NATIVEPOC_HTTP
	static bool BitmapResource(Context context, string source)
	{
		var id = context.Resources?.GetIdentifier(source, "drawable", context.PackageName) ?? 0;
		if (id == 0)
			return false;
		try
		{
			using var stream = context.Resources?.OpenRawResource(id);
			if (stream is null)
				return false;
			var header = new byte[8];
			var read = stream.Read(header, 0, header.Length);
			if (read < 1)
				return false;
			return header[0] != (byte)'<';
		}
		catch (Exception)
		{
			return false;
		}
	}
#endif
}

#if NATIVEPOC_HTTP
static class RemoteImages
{
	const int Capacity = 32;
	static readonly LruCache<Bitmap> Cache = new(Capacity);
	static readonly ConditionalWeakTable<ImageView, ImageSlot> Slots = new();
	static readonly object Gate = new();
	static int _applies;

	public static void Apply(ImageView image, string source, Context context)
	{
		var slot = Slots.GetValue(image, static _ => new ImageSlot());
		var token = slot.Begin();
		image.Visibility = ViewStates.Visible;
		image.SetScaleType(ImageView.ScaleType.CenterCrop);
		Bitmap? cached = null;
		lock (Gate)
		{
			if (Cache.TryGet(source, out var hit))
				cached = hit;
		}

		if (cached is not null)
		{
			image.SetImageBitmap(cached);
			image.ContentDescription = source;
			NoteApply(source);
			return;
		}

		image.SetImageDrawable(new ColorDrawable(new Color(unchecked((int)0xFFE6E6E6))));
		image.ContentDescription = "Loading image";
		var app = context.ApplicationContext ?? context;
		ThreadPool.QueueUserWorkItem(_ =>
		{
			Bitmap? bitmap = null;
			try
			{
				bitmap = Load(app, source);
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(MainActivity.Tag, "image-error " + ex.GetType().Name);
			}

			if (bitmap is not null)
			{
				lock (Gate)
					Cache.Add(source, bitmap);
			}

			image.Post(() =>
			{
				if (!slot.Current(token))
				{
					Android.Util.Log.Info(MainActivity.Tag, "image-drop source=" + source);
					return;
				}

				if (bitmap is null)
				{
					image.SetImageDrawable(new ColorDrawable(new Color(unchecked((int)0xFFB00020))));
					image.ContentDescription = "Image failed";
					image.Visibility = ViewStates.Visible;
					Android.Util.Log.Info(MainActivity.Tag, "image-fail source=" + source);
					return;
				}

				image.SetImageBitmap(bitmap);
				image.ContentDescription = source;
				image.Visibility = ViewStates.Visible;
				NoteApply(source);
			});
		});
	}

	static void NoteApply(string source)
	{
		var count = Interlocked.Increment(ref _applies);
		if (count <= 40 || source == "feed_mark")
			Android.Util.Log.Info(MainActivity.Tag, "image-apply source=" + source);
	}

	static Bitmap? Load(Context context, string source)
	{
		if (ImageSource.IsRemote(source))
		{
			var bytes = Download(source);
			if (bytes is null)
				return null;
			return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
		}

		var id = context.Resources?.GetIdentifier(source, "drawable", context.PackageName) ?? 0;
		if (id == 0)
			return null;
		return BitmapFactory.DecodeResource(context.Resources, id);
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
			var chunk = new byte[4096];
			int read;
			while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
			{
				buffer.Write(chunk, 0, read);
				if (buffer.Length > 1_000_000)
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
