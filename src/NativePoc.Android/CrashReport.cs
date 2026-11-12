using Android.Util;
using Android.Views;
using Android.Widget;

namespace NativePoc;

// Release keeps StackTraceSupport so the stack names C# methods.
// A frame is "at Type.Method() + 0xoffset" or "BaseAddress+0xoffset".
// The log has no file:line. tools/devx/symbolicate-stack.sh reads
// NativePoc.Android.so.dbg, which is kept beside the build and is not in the APK.
static class CrashReport
{
	static bool _installed;

	public static void Install()
	{
		if (_installed)
			return;
		_installed = true;
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
				Log.Error(MainActivity.Tag, "crash\n" + ex);
		};
		var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
		Java.Lang.Thread.DefaultUncaughtExceptionHandler = new Handler(previous);
	}

	public static void Arm(Activity activity)
	{
		if (!string.Equals(activity.Intent?.GetStringExtra("nativepoc_crash"), "throw", StringComparison.Ordinal))
			return;
		// A throw on the main looper unwinds it, so the crash text never draws
		// and the process sits as an ANR. A background thread keeps the throw
		// unhandled and leaves the looper up long enough to show the screen.
		new Java.Lang.Thread(new ThrowSoon()).Start();
	}

	sealed class ThrowSoon : Java.Lang.Object, Java.Lang.IRunnable
	{
		public void Run() => ThrowNow();
	}

	static void ThrowNow() =>
		throw new InvalidOperationException("nativepoc deliberate crash");

	sealed class Handler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
	{
		readonly Java.Lang.Thread.IUncaughtExceptionHandler? _next;

		public Handler(Java.Lang.Thread.IUncaughtExceptionHandler? next) => _next = next;

		public void UncaughtException(Java.Lang.Thread thread, Java.Lang.Throwable exception)
		{
			var activity = MainActivity.Current;
			var text = exception is null ? "error" : exception.ToString();
			Log.Error(MainActivity.Tag, "crash-java " + text);
			if (activity is null || exception is null)
			{
				FinishProcess();
				return;
			}

			try
			{
				activity.RunOnUiThread(() =>
				{
					var view = new TextView(activity)
					{
						Text = "The app hit an error\n" + text,
					};
					view.ContentDescription = "crash-screen";
					activity.SetContentView(view);
					Log.Error(MainActivity.Tag, "crash-screen");
				});
			}
			catch (Exception)
			{
				Log.Error(MainActivity.Tag, "crash-screen failed");
			}

			// Leave the screen up briefly, then exit so a dead thread does not sit as an ANR.
			new Java.Lang.Thread(new ExitSoon()).Start();
		}
	}

	sealed class ExitSoon : Java.Lang.Object, Java.Lang.IRunnable
	{
		public void Run()
		{
			System.Threading.Thread.Sleep(1200);
			FinishProcess();
		}
	}

	static void FinishProcess()
	{
		try
		{
			MainActivity.Current?.Finish();
		}
		catch (Exception)
		{
			// The activity is already going away.
		}

		Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
	}
}
