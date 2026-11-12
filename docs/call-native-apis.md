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

`samples/bind/DeviceBattery.cs` reads the capacity without a permission. `Read` runs off the UI thread and posts a `BatteryReading` into the screen field. A capacity from 0 through 100 posts that number. `-1`, and any other value outside that range, posts `unknown` rather than the raw int. The log line is `method=getIntProperty known=1 text=100` or `known=0 text=unknown`. `Plugged` reads the sticky `ACTION_BATTERY_CHANGED` intent the same way. A hot reload that changes the button from `DeviceBattery.Read` to `DeviceBattery.Plugged` rebinds the registered method. A new `Device*.cs` file still needs a republish.

```csharp
DeviceBattery.Read("bind", "Note");
```

`Level` is the same reading with `Blocking = true`. Calling it from a button or from `Render` is NP265. `Read` calls it inside `Task.Run`, which is the off-thread entry.

## Clipboard listener

`DeviceClipboard.Listen` replaces any previous `PrimaryClipChanged` handler and posts the clip text into the named field. The arming log is `method=addPrimaryClipChangedListener armed=1`. A later clip change logs `text=` and the screen field updates. `dumpsys clipboard` on the API 34 image prints nothing; the log line is the proof.

One `setPrimaryClip` on that image fires the listener twice, about 30ms apart, with the same text. When both events share a clip timestamp, or the same text arrives again inside 100ms, the second post is dropped and the log says `deduped=1`. A later set still posts. `Stop` removes the handler and logs `method=removePrimaryClipChangedListener`. The bind screen calls `Stop` from `OnLeave`, so back unsubscribes. A clip change after that does not post. Leaving the screen is the unsubscribe. Home pauses the activity and leaves the listener armed.

```csharp
DeviceClipboard.Listen("bind", "Note");
```

## Notification permission

`samples/bind/DeviceNotify.cs` asks for `POST_NOTIFICATIONS` on API 33 and above, then posts `granted`, `denied`, or `blocked` into the named field. The manifest overlay is `Properties/NotifyPermission.xml`, included when `NativePocJni` is set. Below API 33, and when the permission is already granted, `Ask` shows the notification and posts `granted` without a dialog. Otherwise `RequestPermissions` runs, and `OnRequestPermissionsResult` completes the same post. `PermissionRules` maps a grant to `granted`, a denial that can still show a rationale to `denied`, and `FLAG_PERMISSION_USER_FIXED`, `shouldShowRequestPermissionRationale` false, or a second denial to `blocked`. The log line is `method=requestPermission text=granted`, `text=denied`, or `text=blocked`, with `rationale=` and `fixed=`. A grant also logs `method=notify text=pier`. `OpenSettings` starts `ACTION_APPLICATION_DETAILS_SETTINGS` for this package and logs `method=ACTION_APPLICATION_DETAILS_SETTINGS`. There is no reflection: the activity calls `DeviceNotify.OnResult`, and the screen calls `Ask` and `OpenSettings`.

```csharp
DeviceNotify.Ask("bind", "Note");
```

`pm revoke` then the Notify button is the denied path while the system still offers a rationale. A second denial, or a result with `shouldShowRequestPermissionRationale` false or `USER_FIXED`, posts `blocked`. The Settings button then opens the app details screen. `pm grant` then the Notify button is the granted path, and the shade shows the Bind notification.

## Pause and resume

`platform.OnPause`, `platform.OnResume`, and `platform.OnLeave` take a local function in `Render`. The activity calls them from `OnPause`, `OnResume`, and when the screen is popped. The bind screen writes `paused` into `Phase` and `resumed` into `Returned`. A hot reload of those functions replaces the handlers. `OnLeave` also calls `DeviceClipboard.Stop`. The host logs `lifecycle pause`, `lifecycle resume`, and `lifecycle leave`.

The bind screen is a scroll of cards. Text, card, muted, and accent colors are the light `ThemeTokens` constants. The Android host maps those constants to the dark pair when the theme is dark, including a system night mode. The page color already follows `ThemeTokens.Page`.

## Bench

`samples/bind/DeviceBench.cs` calls `DeviceBattery.Sample` a thousand times. `Direct` is the compiled call. `Registry` is `JniRegistry.Call`, the hop a reloaded screen uses. Both log `jni-bench n=1000` and `note=swiftshader-irrelevant`. The loops do not log each call. This is JNI, not frames, so the emulator's SwiftShader does not apply.

A result-returning `jni` call runs on `Task.Run`. A void call marked `Blocking` does too. Toast, share, and vibrate stay on the UI thread because they start an activity or a system service that expects it.

## Doctor and the restart prompt

`cnative doctor` fails when a `bin/listener` apk's `classes.dex` has no `NativeAotRuntimeProvider`, and when `bin/Debug/.../publish` still holds a pre-split apk. It does not delete that apk. `cnative run android --listener` deletes only that publish directory and refuses to install anything that is not under `bin/listener`.

`Restart the app? [y/N]` follows `--yes` and `--no`. `--yes` is the same choice as `--restart-on-rude`. `--no`, and a shell that is not a terminal, leave the app running and print that the default is no. They do not read a line.
