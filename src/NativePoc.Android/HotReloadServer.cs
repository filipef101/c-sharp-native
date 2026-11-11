#if DEBUG
using System.Diagnostics.CodeAnalysis;
#if !NATIVEPOC_PROGRAM_ONLY
using System.Reflection;
using System.Runtime.Loader;
#endif
using System.Security.Cryptography;
using System.Text;
using Android.App;
using Android.Content;
using Android.Net;
using Android.Views;

namespace NativePoc;

static class HotReloadServer
{
	const int TokenBytes = 16;
	const string TokenFile = "nativepoc-hotreload.token";
	const string SocketName = "npoc-hotreload";

	static readonly object Gate = new();

	static int _started;

	static byte[]? _token;

#if !NATIVEPOC_PROGRAM_ONLY
	static AssemblyLoadContext? _loaded;
#endif

	public static void Start(Context context)
	{
		if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
		{
			Android.Util.Log.Info(MainActivity.Tag, "hotreload-listen already");
			return;
		}

		string hex;
		try
		{
			context.DeleteFile("hotreload.token");
			var token = RandomNumberGenerator.GetBytes(TokenBytes);
			hex = Convert.ToHexString(token).ToLowerInvariant();
			lock (Gate)
				_token = token;
		}
		catch (Exception ex)
		{
			Interlocked.Exchange(ref _started, 0);
			Android.Util.Log.Error(MainActivity.Tag, "hotreload-error " + ex.GetType().Name + " " + ex.Message);
			return;
		}

		var thread = new Thread(() => Listen(context, hex))
		{
			IsBackground = true,
			Name = "hot-reload",
		};
		thread.Start();
	}

	static void WriteToken(Context context, string hex)
	{
		using var output = context.OpenFileOutput(TokenFile, FileCreationMode.Private)
			?? throw new InvalidOperationException("The hot reload token file was not created.");
		var encoded = Encoding.ASCII.GetBytes(hex);
		output.Write(encoded, 0, encoded.Length);
		output.Flush();
	}

	static void Listen(Context context, string hex)
	{
		LocalServerSocket listener;
		try
		{
			listener = new LocalServerSocket(SocketName);
			WriteToken(context, hex);
		}
		catch (Exception ex)
		{
			Interlocked.Exchange(ref _started, 0);
			LogFailure(hex, ex);
			return;
		}

		Android.Util.Log.Info(MainActivity.Tag, "hotreload-listen");
		while (true)
		{
			LocalSocket? client = null;
			try
			{
				client = listener.Accept();
				if (client is null)
					throw new InvalidOperationException("The abstract socket accept returned no peer.");
				Handle(client);
			}
			catch (Exception ex)
			{
				LogFailure(hex, ex);
				if (client is not null)
				{
					try
					{
						var detail = ex.Message.Replace(hex, "[redacted]", StringComparison.OrdinalIgnoreCase);
						WriteResponse(Output(client), false, ex.GetType().Name + ": " + detail);
					}
					catch (Exception writeError)
					{
						Android.Util.Log.Error(MainActivity.Tag, "hotreload-reply-failed " + writeError.Message);
					}
				}
			}
			finally
			{
				client?.Dispose();
			}
		}
	}

	static void Handle(LocalSocket client)
	{
		var input = Input(client);
		var output = Output(client);
		var presented = new byte[TokenBytes];
		ReadExact(input, presented);
		byte[] expected;
		lock (Gate)
			expected = _token ?? throw new InvalidOperationException("The hot reload listener has no token.");
		if (!CryptographicOperations.FixedTimeEquals(presented, expected))
			throw new UnauthorizedAccessException("Rejected hot reload token.");

		var length = ReadInt(input);
		if (length <= 0 || length > 8 * 1024 * 1024)
			throw new InvalidOperationException("Rejected screen assembly length " + length + ".");
		var dll = new byte[length];
		ReadExact(input, dll);
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
				var waitForPresent = !ScreenQuery.IsQuery(dll) && ScreenProgram.IsProgram(dll);
				title = Apply(dll, activity);
				if (!waitForPresent)
				{
					done.Set();
					return;
				}

				Activity presented = activity;
				if (PackingActivity.Current is Activity packingActivity && packingActivity.HasWindowFocus)
					presented = packingActivity;
				if (!presented.HasWindowFocus)
				{
					title += " present=0";
					done.Set();
					return;
				}

				if (!ArmPresent(presented, done))
				{
					title += " present=0";
					done.Set();
					return;
				}

				title += " present=1";
			}
			catch (Exception ex)
			{
				error = ex;
				done.Set();
			}
		});
		if (!done.Wait(TimeSpan.FromSeconds(10)))
			throw new TimeoutException("The UI thread did not apply the screen assembly.");
		if (error is not null)
			throw error;
		WriteResponse(output, true, title);
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Debug-only loader for a screen assembly compiled on the host.")]
	[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Debug-only lookup of NativePoc.Screen.Render.")]
	[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Debug-only bind of NativePoc.Screen.Render.")]
	[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Debug-only bind of NativePoc.Screen.Render.")]
	static string Apply(byte[] dll, MainActivity activity)
	{
		if (ScreenQuery.IsQuery(dll))
			return activity.Answer(ScreenQuery.Op(dll), dll);
		if (ScreenProgram.IsProgram(dll))
		{
			if (!ScreenProgram.IsRecord(dll))
				throw new InvalidOperationException("The screen program is not a record.");
			EffectGate.Shared.NoteReload();
			var screenId = RecordProgram.ScreenId(dll);
			if (screenId.Length == 0)
				screenId = "screen";
			var bag = ScreenNav.Shared.Bag(screenId);
			var kept = RecordProgram.KeptNote(dll, bag);
			RecordSession.Prepare(dll, bag);
			ScreenNav.Shared.Show(dll);
			var record = ScreenNav.Shared.Top() ?? RecordProgram.Bind(dll, bag);
			string programmed;
			if (PackingActivity.Current is { HasWindowFocus: true })
				programmed = PackingActivity.UseProgram(record);
			else
				programmed = activity.UseProgram(record);
			var programmedMessage = programmed + " program=1";
			if (kept.Length > 0)
				programmedMessage += "\n" + kept;
			Android.Util.Log.Info(MainActivity.Tag, "hotreload-applied " + programmedMessage);
			return programmedMessage;
		}

#if NATIVEPOC_PROGRAM_ONLY
		throw new InvalidOperationException("This build has no assembly loader.");
#else
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
#endif
	}

#if !NATIVEPOC_PROGRAM_ONLY
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
		var simple = name.Name;
		if (simple == "NativePoc.Core")
			return typeof(AppSession).Assembly;
		if (IsFrameworkName(simple))
		{
			foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
			{
				if (loaded.GetName().Name == simple)
					return loaded;
			}
		}
		return null;
	}

	static bool IsFrameworkName(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return false;
		return name == "System.Private.CoreLib"
			|| name == "netstandard"
			|| name == "mscorlib"
			|| name.StartsWith("System.", StringComparison.Ordinal);
	}
#endif

	static bool ArmPresent(Activity activity, ManualResetEventSlim done)
	{
		var root = activity.Window?.DecorView;
		var observer = root?.ViewTreeObserver;
		if (root is null || observer is null || !observer.IsAlive)
			return false;
		observer.AddOnDrawListener(new PresentDraw(root, done));
		root.Invalidate();
		return true;
	}

	sealed class PresentDraw : Java.Lang.Object, ViewTreeObserver.IOnDrawListener
	{
		readonly View _view;
		readonly ManualResetEventSlim _done;
		int _fired;

		public PresentDraw(View view, ManualResetEventSlim done)
		{
			_view = view;
			_done = done;
		}

		public void OnDraw()
		{
			if (Interlocked.Exchange(ref _fired, 1) != 0)
				return;
			Android.Util.Log.Info(MainActivity.Tag, "present-frame");
			_view.Post(() =>
			{
				var observer = _view.ViewTreeObserver;
				if (observer is not null && observer.IsAlive)
					observer.RemoveOnDrawListener(this);
				_done.Set();
			});
		}
	}

	static void LogFailure(string hex, Exception ex)
	{
		var message = ex.Message.Replace(hex, "[redacted]", StringComparison.OrdinalIgnoreCase);
		Android.Util.Log.Error(MainActivity.Tag, "hotreload-error " + ex.GetType().Name + " " + message);
	}

	static Stream Input(LocalSocket socket)
	{
		var stream = socket.InputStream ?? throw new InvalidOperationException("The hot reload socket has no input stream.");
		return stream;
	}

	static Stream Output(LocalSocket socket)
	{
		var stream = socket.OutputStream ?? throw new InvalidOperationException("The hot reload socket has no output stream.");
		return stream;
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
