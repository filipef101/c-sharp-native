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
| `Storage` | `Set` | a key literal, then one string | `SharedPreferences` `nativepoc-settings`, committed before the call returns. The log is `storage-set` |
| `Storage` | `Get` | a key literal | the same store. The result is a string, empty when the key is missing. The log is `storage-load` |
| `Theme` | `Mode` | `dark`, `light`, `system`, or a bool property | writes the same store under `theme` and repaints. A bool property is `dark` when true. `system` follows `uiMode` |
| `Http` | `GetJson` | one URL string | `HttpClient` GET. The body comes back as a string. A failure is `http-error:` plus a reason (`timeout`, `cancelled`, a status code, or an exception name). The client timeout is 8 seconds |
| `Http` | `PostJson` | a URL string, then a JSON string | the same client, POST, `application/json` |
| `Timer` | `After` | a millisecond int from 0 to 10000 | completes after that delay. The result is `ok` |
| `FeedModule` | `Apply` | the screen state, then a JSON string | source-generated `System.Text.Json` reads `{ "items": [{ "id", "title" }] }` into the `Items` list. `http-error:` and an empty string clear the list |

`Http.GetJson`, `Http.PostJson`, `Storage.Get`, and `Timer.After` return `Task<string>`. A handler assigns them with `await`. The lowerer emits `ActAwait`. A task that is already finished writes the string and continues. A task that is still running suspends the action: the continuation is stored and resumed on the UI thread, and the step counter starts again for that segment. `Pop` cancels the in-flight call for the screen that left and requests a render of the screen underneath. The log is `http-cancel screen=` and, when a call was in flight, `http-cancel module=`.

JSON in the Android binary uses a source-generated `JsonSerializerContext`. There is no reflection serialization.

Adding a module is a row in that switch and a matching arm in `SubsetLowerer.ModuleCall`. An awaitable method is an arm in `AwaitArgs` instead. The screen does not look up a type at runtime.

`cnative agent submit <id>` runs a field's `OnSubmit`, which is where a form calls these modules. `cnative agent logs` shows `haptic-ok`, `haptic-unavailable`, `share-text`, `share-skipped`, `clipboard-ok`, `storage-set`, `storage-load`, `theme mode=`, `http-get`, `http-post`, `http-error`, and `http-cancel`.
