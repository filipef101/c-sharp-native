# Packing checklist

An offline list of fictional kit. There is no account, no personal data, and no network call.

The list and the editor are C# functions in `NativePoc.Packing`. They return `Element` trees. `Switch` is a real `android.widget.Switch`. `INativeServices` is the clipboard and haptic module. `AndroidNativeServices` implements it and is disposed when `PackingActivity` is destroyed. `ViewReconciler.Release` detaches listeners before the views are disposed.

`MainActivity` is still the launcher and still renders the counter screen.

## State

`PackingSession.Shared` is one static object for the process.

Activity recreation, including rotation, keeps that object. The new activity reads the same items, route, and draft.

A Release process restart does not. A force-stop or a cold start builds a new process, and `PackingState.Seed` supplies Canvas tent, Camp stove, Ridge map, and Water bladder again. Release writes nothing to a bundle or a file. A Debug process writes `files/nativepoc-state.bin` on each render and reads it before the first frame, so a Debug force-stop restores the list. That file is not the hot-reload token.

## Install

Both APKs are `android-x64`. This tree was not run on arm64.

Debug, assemblies embedded so `adb install` works:

```bash
adb install -r nativepoc-packing-debug-x64.apk
adb shell am start -n com.scratch.nativepoc/nativepoc.PackingActivity
```

Release NativeAOT:

```bash
adb install -r nativepoc-packing-nativeaot-x64.apk
adb shell am start -n com.scratch.nativepoc/nativepoc.PackingActivity
```

Hashes are in `evidence/packing-apk-hashes.txt` after the publish recorded in that file.

## Native API

```csharp
public sealed record Switch(bool Value, Action<bool> OnChange, string AccessibilityLabel) : Element;

public interface INativeServices : IDisposable
{
    void CopyText(string label, string text);
    void Pulse();
}
```

`AndroidHost` creates `Android.Widget.Switch`, subscribes `CheckedChange`, and unsubscribes in `Detach` before `Dispose`. `CopyText` round-trips `ClipboardManager`. `Pulse` calls `Vibrator.Vibrate` on API 26 and later, and logs `haptic-unavailable` when the device has no vibrator.
