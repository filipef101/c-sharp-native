#if NATIVEPOC_FAST_NATIVES
using System.Runtime.InteropServices;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Java.Interop;

namespace NativePoc;

/* The five activity overrides, bound by function pointer. The generated
   path looks each one up by name. These delegates are the same JNI shape
   as Mono.Android's private thunks, and the table stays rooted for the
   process so RegisterNatives' pointers stay valid. */
static class ActivityNatives
{
	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	delegate void JniPp(nint env, nint self);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	delegate void JniPpl(nint env, nint self, nint arg);

	static readonly JniNativeMethodRegistration[] Table =
	[
		new("n_onCreate", "(Landroid/os/Bundle;)V", new JniPpl(OnCreate)),
		new("n_onNewIntent", "(Landroid/content/Intent;)V", new JniPpl(OnNewIntent)),
		new("n_onResume", "()V", new JniPp(OnResume)),
		new("n_onBackPressed", "()V", new JniPp(OnBackPressed)),
		new("n_onDestroy", "()V", new JniPp(OnDestroy)),
	];

	[UnmanagedCallersOnly(EntryPoint = "Java_nativepoc_ActivityNatives_register")]
	static void Register(nint env, nint klass, nint activityClass)
	{
		_ = klass;
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var peer = new JniObjectReference(activityClass);
			JniEnvironment.Types.RegisterNatives(peer, Table);
			Log.Info(MainActivity.Tag, "jcw-fast count=" + Table.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}

	static void OnCreate(nint env, nint self, nint saved)
	{
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var activity = Java.Lang.Object.GetObject<MainActivity>(env, self, JniHandleOwnership.DoNotTransfer);
			var bundle = Java.Lang.Object.GetObject<Bundle>(saved, JniHandleOwnership.DoNotTransfer);
			activity?.FastOnCreate(bundle);
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}

	static void OnNewIntent(nint env, nint self, nint intent)
	{
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var activity = Java.Lang.Object.GetObject<MainActivity>(env, self, JniHandleOwnership.DoNotTransfer);
			var next = Java.Lang.Object.GetObject<Android.Content.Intent>(intent, JniHandleOwnership.DoNotTransfer);
			activity?.FastOnNewIntent(next);
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}

	static void OnResume(nint env, nint self)
	{
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var activity = Java.Lang.Object.GetObject<MainActivity>(env, self, JniHandleOwnership.DoNotTransfer);
			activity?.FastOnResume();
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}

	static void OnBackPressed(nint env, nint self)
	{
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var activity = Java.Lang.Object.GetObject<MainActivity>(env, self, JniHandleOwnership.DoNotTransfer);
			activity?.FastOnBackPressed();
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}

	static void OnDestroy(nint env, nint self)
	{
		if (!JniEnvironment.BeginMarshalMethod(env, out JniTransition transition, out JniRuntime? runtime))
			return;
		try
		{
			var activity = Java.Lang.Object.GetObject<MainActivity>(env, self, JniHandleOwnership.DoNotTransfer);
			activity?.FastOnDestroy();
		}
		catch (Exception ex)
		{
			runtime?.OnUserUnhandledException(ref transition, ex);
		}
		finally
		{
			JniEnvironment.EndMarshalMethod(ref transition);
		}
	}
}
#endif
