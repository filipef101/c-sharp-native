# Platform modules

A subset screen calls a platform API by name. The call is a static method, the first argument is the `IPlatform` parameter, and the lowerer emits `ActModule`. The Android host dispatches that name in `PlatformModules.Dispatch` with a switch. There is no reflection and no `Assembly.Load` on this path, so the same call is valid in the NativeAOT listener.

```csharp
Haptics.Confirm(host);
Share.Text(host, state.Email);
Clipboard.Copy(host, "form", state.Email);
```

| module | method | arguments after the platform | Android |
| --- | --- | --- | --- |
| `Haptics` | `Confirm` | none | `VibrationEffect` one-shot, or `haptic-unavailable` when the device has no vibrator |
| `Share` | `Text` | one string | `ACTION_SEND` text/plain. An empty string logs `share-skipped` and does not open the sheet |
| `Clipboard` | `Copy` | a label literal, then one string | `ClipboardManager`, then a read-back. The log is `clipboard-ok` |

Adding a module is a row in that switch and a matching arm in `SubsetLowerer.ModuleCall`. The screen does not look up a type at runtime.

`cnative agent submit <id>` runs a field's `OnSubmit`, which is where a form calls these modules. `cnative agent logs` shows `haptic-ok`, `haptic-unavailable`, `share-text`, `share-skipped`, and `clipboard-ok`.
