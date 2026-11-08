#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace NativePoc;

static class HotReloadServer
{
	public const int Port = 18765;

	static AssemblyLoadContext? _loaded;

	public static void Start()
	{
		var thread = new Thread(Listen)
		{
			IsBackground = true,
			Name = "hot-reload",
		};
		thread.Start();
	}

	static void Listen()
	{
		var listener = new TcpListener(IPAddress.Loopback, Port);
		listener.Start();
		Android.Util.Log.Info(MainActivity.Tag, "hotreload-listen " + Port);
		while (true)
		{
			var client = listener.AcceptTcpClient();
			try
			{
				client.NoDelay = true;
				Handle(client);
			}
			catch (Exception ex)
			{
				Android.Util.Log.Error(MainActivity.Tag, "hotreload-error " + ex.GetType().Name + " " + ex.Message);
				try
				{
					WriteResponse(client.GetStream(), false, ex.GetType().Name + ": " + ex.Message);
				}
				catch (Exception writeError)
				{
					Android.Util.Log.Error(MainActivity.Tag, "hotreload-reply-failed " + writeError.Message);
				}
			}
			finally
			{
				client.Dispose();
			}
		}
	}

	static void Handle(TcpClient client)
	{
		var stream = client.GetStream();
		var length = ReadInt(stream);
		if (length <= 0 || length > 8 * 1024 * 1024)
			throw new InvalidOperationException("Rejected screen assembly length " + length + ".");
		var dll = new byte[length];
		ReadExact(stream, dll);
		var activity = MainActivity.Current;
		if (activity is null)
			throw new InvalidOperationException("No activity is current.");

		string title = "";
		Exception? error = null;
		var done = new ManualResetEventSlim(false);
		activity.RunOnUiThread(() =>
		{
			try
			{
				title = Apply(dll, activity);
			}
			catch (Exception ex)
			{
				error = ex;
			}
			finally
			{
				done.Set();
			}
		});
		if (!done.Wait(TimeSpan.FromSeconds(10)))
			throw new TimeoutException("The UI thread did not apply the screen assembly.");
		if (error is not null)
			throw error;
		WriteResponse(stream, true, title);
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Debug-only loader for a screen assembly compiled on the host.")]
	[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Debug-only lookup of NativePoc.Screen.Render.")]
	[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Debug-only bind of NativePoc.Screen.Render.")]
	[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Debug-only bind of NativePoc.Screen.Render.")]
	static string Apply(byte[] dll, MainActivity activity)
	{
		AssemblyLoadContext context;
		Assembly assembly;
		var collectible = true;
		try
		{
			(context, assembly) = Load(dll, collectible: true);
		}
		catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException)
		{
			collectible = false;
			(context, assembly) = Load(dll, collectible: false);
		}

		var type = assembly.GetType("NativePoc.Screen", throwOnError: true);
		var method = type!.GetMethod(
			"Render",
			BindingFlags.Public | BindingFlags.Static,
			[typeof(AppState), typeof(IPlatform)]);
		if (method is null)
			throw new InvalidOperationException("NativePoc.Screen.Render was not found.");
		var render = method.CreateDelegate<Func<AppState, IPlatform, Element>>();
		var title = activity.UseRender(render);
		var previous = _loaded;
		_loaded = context;
		if (previous is { IsCollectible: true })
		{
			try
			{
				previous.Unload();
			}
			catch (Exception ex)
			{
				Android.Util.Log.Warn(MainActivity.Tag, "hotreload-unload " + ex.Message);
			}
		}

		var message = title + " collectible=" + collectible;
		Android.Util.Log.Info(MainActivity.Tag, "hotreload-applied " + message);
		return message;
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Debug-only loader for a screen assembly compiled on the host.")]
	static (AssemblyLoadContext Context, Assembly Assembly) Load(byte[] dll, bool collectible)
	{
		var context = new AssemblyLoadContext("nativepoc-screen-" + Guid.NewGuid().ToString("N"), collectible);
		context.Resolving += (_, name) => Resolve(name);
		using var stream = new MemoryStream(dll, writable: false);
		var assembly = context.LoadFromStream(stream);
		return (context, assembly);
	}

	static Assembly? Resolve(AssemblyName name)
	{
		if (name.Name == "NativePoc.Core")
			return typeof(AppSession).Assembly;
		foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
		{
			if (loaded.GetName().Name == name.Name)
				return loaded;
		}
		return null;
	}

	static int ReadInt(Stream stream)
	{
		var buffer = new byte[4];
		ReadExact(stream, buffer);
		return buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
	}

	static void ReadExact(Stream stream, byte[] buffer)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = stream.Read(buffer, offset, buffer.Length - offset);
			if (read == 0)
				throw new EndOfStreamException("The hot reload connection closed early.");
			offset += read;
		}
	}

	static void WriteResponse(Stream stream, bool ok, string message)
	{
		var text = Encoding.UTF8.GetBytes(message);
		var header = new byte[5];
		header[0] = ok ? (byte)1 : (byte)0;
		header[1] = (byte)text.Length;
		header[2] = (byte)(text.Length >> 8);
		header[3] = (byte)(text.Length >> 16);
		header[4] = (byte)(text.Length >> 24);
		stream.Write(header);
		stream.Write(text);
		stream.Flush();
	}
}
#endif
