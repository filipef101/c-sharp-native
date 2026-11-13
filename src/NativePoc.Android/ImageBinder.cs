using System.Runtime.CompilerServices;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace NativePoc;

static class PictureBinder
{
	public static void Show(ImageView image, string source, Context context, string description, bool decorative, int radius = 0)
	{
		if (radius < 0)
			radius = 0;
		if (source.Length == 0)
		{
			image.SetImageDrawable(null);
			image.ContentDescription = null;
			image.Visibility = ViewStates.Gone;
			return;
		}

#if NATIVEPOC_IMAGE
		ImageModule.Show(image, source, context, description, decorative, radius);
#else
#if NATIVEPOC_HTTP
		if (ImageSource.IsRemote(source) || IsBitmap(context, source))
		{
			RemoteImages.Apply(image, source, context);
			return;
		}
#endif
		var id = context.Resources?.GetIdentifier(source, "drawable", context.PackageName) ?? 0;
		if (id == 0)
			throw new InvalidOperationException("Drawable " + source + " was not found.");
		image.SetImageResource(id);
		Describe(image, source, description, decorative);
		image.Visibility = ViewStates.Visible;
#endif
	}

	internal static void Describe(ImageView image, string source, string description, bool decorative)
	{
		if (decorative)
		{
			image.ContentDescription = null;
			image.ImportantForAccessibility = ImportantForAccessibility.No;
			return;
		}

		image.ImportantForAccessibility = ImportantForAccessibility.Yes;
		image.ContentDescription = description.Length > 0 ? description : source;
	}

#if NATIVEPOC_HTTP
	static readonly Dictionary<string, bool> BitmapKind = new(StringComparer.Ordinal);

	static bool IsBitmap(Context context, string source)
	{
		if (BitmapKind.TryGetValue(source, out var known))
			return known;
		var bitmap = BitmapResource(context, source);
		BitmapKind[source] = bitmap;
		return bitmap;
	}

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
			Android.Util.Log.Info(MainActivity.Tag, "image-cache hit source=" + source);
			image.SetImageBitmap(cached);
			image.ContentDescription = source;
			NoteApply(source);
			return;
		}

		if (image.Background is null)
			image.SetImageDrawable(new ColorDrawable(new Color(unchecked((int)0xFFE6E6E6))));
		image.ContentDescription = "Loading image";
		var app = context.ApplicationContext ?? context;
		ThreadPool.QueueUserWorkItem(_ =>
		{
			var offUi = Android.OS.Looper.MyLooper() != Android.OS.Looper.MainLooper;
			Android.Util.Log.Info(MainActivity.Tag, "image-decode off-ui=" + (offUi ? "1" : "0") + " source=" + source);
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

	static byte[]? Download(string url) => HttpTransport.GetBytes(url);
}
#endif
