# Call native APIs

A screen calls a static method. The compiled button runs that method. A hot reload of the same call goes through `JniRegistry`, which runs the same method. The registry name is `Type.Method`. Arguments travel as strings. A string result can be stored in a string field.

The call is trim-safe. There is no reflection and no `Assembly.Load`. A `[JniMethod]` whose declaration is `partial` is generated as `JNIEnv` calls, and a module initializer registers it. A `[JniMethod]` with a body is the same registry entry: the generator writes the module initializer, so the binding is that one file. The screen has to call the method so the trimmer keeps the body. `cnative` says that a new binding needs a republish, and that a literal or a different registered method hot-reloads. A warm `am start --es nativepoc_root <id>` pushes that screen. Back pops.

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

## Toast

`samples/bind/DeviceToast.cs` is one file. The method has a body, the attribute names `makeText`, and the generator registers `DeviceToast.Show`.

```csharp
DeviceToast.Show("pier");
```

The log line is `jni-bind class=android/widget/Toast method=makeText text=pier`. Saving a new `Device*.cs` binding does not hot-reload it. `cnative` prints `a new binding needs a republish`.

## Battery

`samples/bind/DeviceBattery.cs` reads the capacity without a permission. `Read` runs off the UI thread and posts the number into the screen field. `Plugged` reads the sticky `ACTION_BATTERY_CHANGED` intent the same way. The log lines are `method=getIntProperty` and `method=EXTRA_PLUGGED`. A hot reload that changes the button from `DeviceBattery.Read` to `DeviceBattery.Plugged` rebinds the registered method. A new `Device*.cs` file still needs a republish.

```csharp
DeviceBattery.Read("bind", "Note");
```

`Level` is the same reading with `Blocking = true`. Calling it from a button or from `Render` is NP265. `Read` calls it inside `Task.Run`, which is the off-thread entry.

## Clipboard listener

`DeviceClipboard.Listen` replaces any previous `PrimaryClipChanged` handler and posts the clip text into the named field. The arming log is `method=addPrimaryClipChangedListener armed=1`. A later clip change logs `text=` and the screen field updates. `dumpsys clipboard` on the API 34 image prints nothing; the log line is the proof.

```csharp
DeviceClipboard.Listen("bind", "Note");
```

## Bench

`samples/bind/DeviceBench.cs` calls `DeviceBattery.Sample` a thousand times. `Direct` is the compiled call. `Registry` is `JniRegistry.Call`, the hop a reloaded screen uses. Both log `jni-bench n=1000` and `note=swiftshader-irrelevant`. The loops do not log each call. This is JNI, not frames, so the emulator's SwiftShader does not apply.

A result-returning `jni` call runs on `Task.Run`. A void call marked `Blocking` does too. Toast, share, and vibrate stay on the UI thread because they start an activity or a system service that expects it.

## Doctor and the restart prompt

`cnative doctor` fails when a `bin/listener` apk's `classes.dex` has no `NativeAotRuntimeProvider`, and when `bin/Debug/.../publish` still holds a pre-split apk. It does not delete that apk. `cnative run android --listener` deletes only that publish directory and refuses to install anything that is not under `bin/listener`.

`Restart the app? [y/N]` follows `--yes` and `--no`. `--yes` is the same choice as `--restart-on-rude`. `--no`, and a shell that is not a terminal, leave the app running and print that the default is no. They do not read a line.
