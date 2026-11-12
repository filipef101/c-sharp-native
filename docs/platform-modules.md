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
| `Animate` | `Play` | a key literal | replays the `Motion` last mounted on the view with that key. A missing key logs `motion miss` |
| `Http` | `GetJson` | one URL string | `HttpURLConnection` GET on a background thread. The body comes back as a string. A failure is `http-error:` plus a reason (`timeout`, `cancelled`, a status code, or an exception name). Connect and read time out at 8 seconds |
| `Http` | `PostJson` | a URL string, then a JSON string | the same connection, POST, `application/json` |
| `Timer` | `After` | a millisecond int from 0 to 10000 | completes after that delay. The result is `ok` |
| `Location` | `Read` | none | runtime permission, then one GPS fix. The result is a string `Location.Parse` turns into `LocationFix`. `granted <lat> <lon> <accuracy>`, `denied`, or `permanently-denied`. Compiles only when `NativePocLocation` is true |
| `Persist` | `Get` | a key literal | async read of one string from the app's private `nativepoc-persist` directory. Missing is empty. The log is `persist-get`. The file copy is compiled only when `NativePocPersist` is true |
| `Persist` | `Put` | a key literal, then one string | async write of that string. The call returns `ok`. The log is `persist-put` |
| `Persist` | `GetRecord` | a key literal | the same file, decoded as a `PersistRecord` (`Id`, `Title`, `Note`) by `PersistCodec`. There is no reflection serializer. The log is `persist-record-load` |
| `Persist` | `PutRecord` | a key literal, then id, title, and note strings | writes that record and returns the codec text. The log is `persist-record` |
| `FeedModule` | `Apply` | the screen state, then a JSON string | a direct reader for `{ "items": [{ "id", "title" }] }` fills the `Items` list. `http-error:` and an empty string clear the list. The reader is not `System.Text.Json` |
| `FeedModule` | `Generate` | the screen state | when `Generated` is empty, writes 1,000 rows. The id is `0000` through `0999` and the title is `Row ` plus that id. A list that already has rows is left as it is. The lowered program does the same write with `OpSeedList` and does not call the C# method |

`Http.GetJson`, `Http.PostJson`, `Storage.Get`, `Timer.After`, `Location.Read`, `Persist.Get`, `Persist.Put`, `Persist.GetRecord`, and `Persist.PutRecord` return `Task<string>`. A handler assigns them with `await`. The lowerer emits `ActAwait`. A task that is already finished writes the string and continues. A task that is still running suspends the action: the continuation is stored and resumed on the UI thread, and the step counter starts again for that segment. `Pop` cancels the in-flight call for the screen that left and requests a render of the screen underneath. The log is `http-cancel screen=` and, when a call was in flight, `http-cancel module=`.

The feed reader does not reference `System.Text.Json`. Setting `JsonSerializerIsReflectionEnabledByDefault` roots that assembly for the trimmer, so the project leaves the switch unset and does not reference the assembly at all. The listener used to publish with Debug's `AndroidLinkMode=None`, and that trim kept `System.Text.Json`. The listener property group now sets `AndroidLinkMode` to `SdkOnly`, `TrimMode` to `full`, and turns serialization discovery off. The bytes are in [devx/evidence/list-input.txt](devx/evidence/list-input.txt). `Http` and `samples/feed` compile only when `NativePocHttp` is true. A Release of the counter app does not pass that flag, so it does not reference `System.Net.Http`, `SocketsHttpHandler`, or the feed screen. The listener publish passes `-p:NativePocHttp=true` and `-p:NativePocRecycler=true`. `NativePocRecycler` compiles `Java/nativepoc/KeyedList.java` into the APK. The dex uses RecyclerView 1.2.1, core 1.3.2, and customview 1.0.0, fetched by `tools/bench/fetch-androidx.sh` into a gitignored cache and checked against `tools/bench/list-floor/deps.sha256`. Those versions do not reference Kotlin. The managed binding is not a compile reference. A Release with that flag sets `AndroidLinkTool` to `r8` and keeps `nativepoc.KeyedList`. The shrunk dex size is in [devx/evidence/scroll-floor.txt](devx/evidence/scroll-floor.txt). A Release of the counter passes neither flag. Image decode for a URL or a bitmap resource is in the same `NativePocHttp` compile, on a thread-pool worker, with an in-memory cache of 32 bitmaps.

Adding a module is a row in that switch and a matching arm in `SubsetLowerer.ModuleCall`. An awaitable method is an arm in `AwaitArgs` instead. The screen does not look up a type at runtime.

`cnative agent submit <id>` runs a field's `OnSubmit`, which is where a form calls these modules. `cnative agent logs` shows `haptic-ok`, `haptic-unavailable`, `share-text`, `share-skipped`, `clipboard-ok`, `storage-set`, `storage-load`, `theme mode=`, `http-get`, `http-post`, `http-error`, and `http-cancel`.
