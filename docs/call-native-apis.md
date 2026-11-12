# Call native APIs

A screen calls a static method. The compiled button runs that method. A hot reload of the same call goes through `JniRegistry`, which runs the same method. The registry name is `Type.Method`. Arguments travel as strings. A string result can be stored in a string field.

The call is trim-safe. There is no reflection and no `Assembly.Load`. A `[JniMethod]` whose declaration is `partial` is generated as `JNIEnv` calls, and a module initializer registers it. A `[JniMethod]` with a body is the same registry entry, written by hand, and its module initializer registers it. The screen has to call the method so the trimmer keeps the body. Reload can change a literal argument, or which registered method the button calls. It cannot add a binding, or change the Java signature, until the next publish.

## Vibrator

`samples/bind/DeviceVibrator.cs` is the generated form. One Java method, primitive arguments, a system service:

```csharp
[JniType("android/os/Vibrator")]
static partial class DeviceVibrator
{
	[JniMethod("vibrate", "(J)V", Instance = true, Service = "vibrator")]
	public static partial void Vibrate(long milliseconds);
}
```

The button is `DeviceVibrator.Vibrate(20)`. The log line is `jni-bind class=android/os/Vibrator method=vibrate milliseconds=20`. Changing `20` to `40` and saving the screen changes the next tap. The process stays up.

## Clipboard

`samples/bind/DeviceClipboard.cs` is the hand-written form. Set and get are several Java calls, so the method has a body and still carries `[JniMethod]`. The registry entries are `DeviceClipboard.Set` and `DeviceClipboard.Get`.

```csharp
DeviceClipboard.Set("bind", "pier");
state.Note = DeviceClipboard.Get();
```

Set logs `jni-bind class=android/content/ClipboardManager method=setPrimaryClip`. Get logs `method=getPrimaryClip` and returns the text. The assignment lowers to the `jni` module, and the field receives that text.

## Share

`samples/bind/DeviceShare.cs` starts `Intent.ACTION_SEND` through the same registry. The entry is `DeviceShare.Send`.

```csharp
DeviceShare.Send("pier");
```

The log line is `jni-bind class=android/content/Intent method=ACTION_SEND text=pier`, and the activity opens the share chooser. Changing the string and saving the screen changes the next share. The chooser title is `Share`.
