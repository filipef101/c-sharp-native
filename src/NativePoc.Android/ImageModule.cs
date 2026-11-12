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
	static readonly LruCache<Bitmap> Baked = new(Capacity);
	static readonly ConditionalWeakTable<ImageView, ImageSlot> Slots = new();
	static readonly Queue<PendingPresent> Presents = new();
	static readonly object Gate = new();
	static int _presenting;

	readonly record struct PendingPresent(ImageView Image, ImageSlot Slot, int Token, Bitmap Bitmap, string Source);

	public static void Show(ImageView image, string source, Context context, string description, bool decorative, int radiusDp)
	{
		var slot = Slots.GetValue(image, static _ => new ImageSlot());
		var token = slot.Begin();
		var target = TargetPx(image, context);
		image.Visibility = ViewStates.Visible;
		image.SetScaleType(radiusDp > 0 ? ImageView.ScaleType.FitXy : ImageView.ScaleType.CenterCrop);
		PictureBinder.Describe(image, source, description, decorative);
		var generated = source.StartsWith("gen:", StringComparison.Ordinal);
		var key = generated ? source : source + "@" + target.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Bitmap? cached = null;
		lock (Gate)
		{
			if (Cache.TryGet(key, out var hit))
				cached = hit;
		}

		if (cached is not null)
		{
			Android.Util.Log.Info(MainActivity.Tag, "image-cache hit source=" + source);
			Place(image, slot, token, cached, source, description, decorative, radiusDp, target);
			return;
		}

		if (generated)
		{
			Bitmap made;
			lock (Gate)
			{
				if (Cache.TryGet(key, out var hit) && hit is not null)
					made = hit;
				else
				{
					made = Swatch(source);
					Cache.Add(key, made);
				}
			}

			Place(image, slot, token, made, source, description, decorative, radiusDp, target);
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

			Place(image, slot, token, bitmap, source, description, decorative, radiusDp, target);
		});
	}

	static void Place(ImageView image, ImageSlot slot, int token, Bitmap? bitmap, string source, string description, bool decorative, int radiusDp, int target)
	{
		var waits = 0;
		void Draw()
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

			if ((image.Width <= 0 || image.Height <= 0) && waits < 4)
			{
				waits++;
				image.Post(Draw);
				return;
			}

			if (source.StartsWith("gen:", StringComparison.Ordinal))
			{
				image.SetScaleType(ImageView.ScaleType.Center);
				ShowBitmap(image, bitmap);
				image.Visibility = ViewStates.Visible;
				Android.Util.Log.Info(MainActivity.Tag, "image-bitmap reuse source=" + source + " px=" + bitmap.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			}

			var width = image.Width > 0 ? image.Width : target;
			var height = image.Height > 0 ? image.Height : target;
			if (radiusDp <= 0)
			{
				ShowBitmap(image, Fit(bitmap, width, height));
				image.Visibility = ViewStates.Visible;
				Android.Util.Log.Info(
					MainActivity.Tag,
					"image-placed source=" + source
					+ " view=" + image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "x" + image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ " clip=" + (image.ClipToOutline ? "1" : "0")
					+ " elevation=" + image.Elevation.ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			}

			var density = image.Resources?.DisplayMetrics?.Density ?? 1f;
			if (density <= 0)
				density = 1f;
			var bakeKey = source + "@" + width.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ "x" + height.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ "r" + radiusDp.ToString(System.Globalization.CultureInfo.InvariantCulture);
			Bitmap? ready = null;
			lock (Gate)
			{
				if (Baked.TryGet(bakeKey, out var hit))
					ready = hit;
			}

			if (ready is not null)
			{
				ShowBitmap(image, ready);
				image.SetScaleType(ImageView.ScaleType.FitXy);
				image.Visibility = ViewStates.Visible;
				Android.Util.Log.Info(
					MainActivity.Tag,
					"image-bitmap reuse source=" + source
					+ " px=" + ready.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "x" + ready.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			}

			var sourceBitmap = bitmap;
			var radius = radiusDp * density;
			ThreadPool.QueueUserWorkItem(_ =>
			{
				if (!slot.Current(token))
					return;
				var drawn = Fit(sourceBitmap, width, height);
				var baked = Bake(drawn, width, height, radius);
				Bitmap shared;
				var created = false;
				lock (Gate)
				{
					if (Baked.TryGet(bakeKey, out var hit) && hit is not null)
						shared = hit;
					else
					{
						Baked.Add(bakeKey, baked);
						shared = baked;
						created = true;
					}
				}

				if (created)
				{
					Android.Util.Log.Info(
						MainActivity.Tag,
						"image-bitmap drawn=" + drawn.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
						+ "x" + drawn.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
						+ " radius=" + radiusDp.ToString(System.Globalization.CultureInfo.InvariantCulture));
					Android.Util.Log.Info(
						MainActivity.Tag,
						"image-bitmap baked=" + shared.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
						+ "x" + shared.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
				}

				Present(image, slot, token, shared, source);
			});
		}

		image.Post(Draw);
	}

	static void ShowBitmap(ImageView image, Bitmap bitmap)
	{
		if (image.Drawable is BitmapDrawable current && ReferenceEquals(current.Bitmap, bitmap))
			return;
		image.SetImageBitmap(bitmap);
	}

	static void Present(ImageView image, ImageSlot slot, int token, Bitmap bitmap, string source)
	{
		var start = false;
		lock (Gate)
		{
			Presents.Enqueue(new PendingPresent(image, slot, token, bitmap, source));
			if (_presenting == 0)
			{
				_presenting = 1;
				start = true;
			}
		}

		if (start)
			image.PostOnAnimation(new Java.Lang.Runnable(DrainPresent));
	}

	static void DrainPresent()
	{
		PendingPresent item;
		lock (Gate)
		{
			if (Presents.Count == 0)
			{
				_presenting = 0;
				return;
			}

			item = Presents.Dequeue();
		}

		if (item.Image.Handle != IntPtr.Zero && item.Slot.Current(item.Token))
		{
			ShowBitmap(item.Image, item.Bitmap);
			item.Image.SetScaleType(ImageView.ScaleType.FitXy);
			item.Image.Visibility = ViewStates.Visible;
			Android.Util.Log.Info(
				MainActivity.Tag,
				"image-placed source=" + item.Source
				+ " view=" + item.Image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ "x" + item.Image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ " clip=" + (item.Image.ClipToOutline ? "1" : "0")
				+ " elevation=" + item.Image.Elevation.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		ImageView? next = null;
		lock (Gate)
		{
			if (Presents.Count == 0)
				_presenting = 0;
			else
				next = Presents.Peek().Image;
		}

		next?.PostOnAnimation(new Java.Lang.Runnable(DrainPresent));
	}

	static Bitmap Bake(Bitmap source, int width, int height, float radius)
	{
		var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!) ?? source;
		var canvas = new Canvas(bitmap);
		var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
		var shader = new BitmapShader(source, Shader.TileMode.Clamp!, Shader.TileMode.Clamp!);
		if (source.Width != width || source.Height != height)
		{
			var matrix = new Matrix();
			matrix.SetScale(width / (float)Math.Max(1, source.Width), height / (float)Math.Max(1, source.Height));
			shader.SetLocalMatrix(matrix);
		}

		paint.SetShader(shader);
		canvas.DrawRoundRect(0, 0, width, height, radius, radius, paint);
		return bitmap;
	}

	static Bitmap Fit(Bitmap bitmap, int width, int height)
	{
		if (width < 1 || height < 1 || (bitmap.Width == width && bitmap.Height == height))
			return bitmap;
		return Bitmap.CreateScaledBitmap(bitmap, width, height, true) ?? bitmap;
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
		if (source.StartsWith("gen:", StringComparison.Ordinal))
			return Swatch(source);
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

	static readonly uint[] SwatchInk =
	[
		0xFF6E97A0, 0xFF4A4038, 0xFFA89886, 0xFF163A5C,
		0xFFC4923A, 0xFF3E4850, 0xFFC9C0B0, 0xFF8C4A34,
	];

	static Bitmap Swatch(string source)
	{
		const int side = 48;
		var index = 0;
		if (source.Length > 4 && int.TryParse(source.AsSpan(4), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
			index = parsed;
		if (index < 0)
			index = 0;
		var ink = SwatchInk[index % SwatchInk.Length];
		var bitmap = Bitmap.CreateBitmap(side, side, Bitmap.Config.Argb8888!) ?? throw new InvalidOperationException("Thumbnail was not created.");
		var canvas = new Canvas(bitmap);
		var paint = new Paint(PaintFlags.AntiAlias);
		paint.Color = new Color(unchecked((int)ink));
		canvas.DrawRect(0, 0, side, side, paint);
		paint.Color = new Color(unchecked((int)0x66FFFFFF));
		canvas.DrawCircle(side * 0.32f, side * 0.32f, side * 0.18f, paint);
		Android.Util.Log.Info(MainActivity.Tag, "image-gen source=" + source + " px=" + side.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return bitmap;
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
