# Independent review: Android native-widget proof of concept

## Re-review of 0a45484

Re-reviewed `0a45484` ("Record the Mac arm64 startup percentiles and validate NativeAOT"), including the commits after `c9f454f`. Sources were read from that commit. `src/` was not modified. The signed APKs are not in git and are not under `/opt/cursor/artifacts/` on this machine. The Android workload is not installed here, so this pass did not publish or strings-scan an APK.

### Verdict

The app now mints the hot-reload token and the launch extra is ignored. The log line no longer carries the token. The keyed greeting move leaves the field and the button attached in the memory host, and a NativeAOT process reused the same four handles. `SetContentView` runs again when the root instance changes. Those three are fixed in the code. The token is still the abstract socket name. The report's KVM section still describes the previous fixed socket and the previous warning list. `IL3053` is suppressed globally with `NoWarn`, and the claim that this cannot hide a warning from this repo is not what that property does. Release keeps the listener behind `#if DEBUG`. This pass did not open a Release APK.

### What was executed

- `dotnet test tests/NativePoc.Core.Tests` on the `0a45484` tree, SDK 10.0.403. **Passed 26, failed 0, skipped 0, 111 ms.** `evidence/unit-tests-il3053.log` is 26/0 at 66 ms.
- Recomputed the KVM `TotalTime` medians from the series logs. They match the table.
- Read `Microsoft.NET.ILLink.tasks` 10.0.14 on this machine for how `NoWarn` and `TrimmerSingleWarn` are passed. The ILC task sources are not installed here, and no probe publish was run.

### Round-2 findings

| Finding | Verdict | What is in the tree | What the logs show |
| --- | --- | --- | --- |
| Token leak: launch extra, log line, and socket name | **Fixed for the extra and the log. The socket name is still the token.** | `Start(Context)` draws 16 bytes, deletes a stale `hotreload.token`, binds `localabstract:npoc-<hex>`, then writes `files/nativepoc-hotreload.token` with `FileCreationMode.Private` (`HotReloadServer.cs:25-73`). The success log is `hotreload-listen` (`HotReloadServer.cs:81`). `LogFailure` redacts the hex (`HotReloadServer.cs:244-247`). `OnCreate` does not read `nativepoc_hotreload` (`MainActivity.cs:43-45`). The host reads the file with `run-as` and throws if that hex is in logcat (`Program.cs:387-392`, `:440-447`). | `evidence/token-contract-check.log`: forged extra `0011…eeff` differed from the file token; `token-in-logcat=False`, `socket-name-in-logcat=False`, `stale-file-absent=True`. The forged 16 bytes got `UnauthorizedAccessException`. The file token got through to `Rejected screen assembly length 0.` `run-as cat /proc/net/unix` was `Permission denied`, so the socket name was not shown to be absent. `evidence/hot-reload-once-token.log`: `once-ms=25.1`, `Counter 0 collectible=True`, pid 15786. |
| Keyed moves detach every sibling, so the field loses focus and the IME | **Fixed for siblings that stay in order.** | `StationaryNewIndices` keeps a longest increasing run of old indexes attached and removes the others (`ViewReconciler.cs:168-202`, `:232-268`). `Screen.Render` keys `title`, `name`, `save`, `greeting` (`Screen.cs:9-33`). `GreetingFirst` swaps the greeting in front of the field. | `Screen_render_keeps_the_field_and_button_when_the_greeting_moves_first` requires `field.Removed == 0` and `button.Removed == 0`. The title and the greeting are still removed and reinserted; the test does not assert otherwise. A full reversal only requires that one of the four views stay attached. Pid 16467, inside the first `OnCreate`, logged the same handles after the move: greeting `12458` at index 0, field `11974`, button `12294`, title `11814` (`evidence/kvm-nativeaot-validate.log`). `button-mounted` was logged once. That move happens before any typing. The log does not record focus or an open IME. |
| `SetContentView` once | **Fixed in the activity. The new call was not run on device.** | `RenderNow` calls `SetContentView` when `Root` is a different instance (`MainActivity.cs:109-116`). | `Replacing_the_root_mounts_a_new_view_and_disposes_the_old_tree` checks the reconciler, not the activity. The acceptance screen always returns a `Column`, so the second render of the keyed move does not take the new branch. No device root swap. |
| Stale report sections | **Partial.** | The TCP listener is under "Earlier passes" and line 381 says that subsection is not the current loader. | The KVM table still says the listen line is `hotreload-listen localabstract:npoc-hotreload` and that the private file was `hotreload.token` (line 93). Line 105 says `WarningsNotAsErrors` is `XA1040;IL3053`. The csproj has `IL3053` in `NoWarn` and `WarningsNotAsErrors` is only `XA1040` (`NativePoc.Android.csproj:15-16`). Line 381 then points "the current loader" at that KVM section. The cnative section at the top matches the code. |

`OnUnmounted` now detaches, removes the view if it still has a parent, and then disposes (`AndroidHost.cs:118-124`). The memory host's new tests require `DisposedWhileAttached` to be false. That was not run against `android.view.View`.

A second `Start` still only logs `hotreload-listen already` in code (`HotReloadServer.cs:27-30`). The cnative section says that call was not made on device.

### IL3053

`Release` sets `PublishAot` and `EnablePreviewFeatures` (`NativePoc.Android.csproj:18-20`). `NoWarn` includes `IL3053` for every configuration (`NativePoc.Android.csproj:15`). `TreatWarningsAsErrors` is true. `WarningsNotAsErrors` is `XA1040` only.

`evidence/il3053-detail.log`, with single-warn turned off, is three `IL3050` lines and no `NativePoc` assembly:

- `ManagedTypeManager.MakeGenericType` calls `Type.MakeGenericType`
- `JNIEnv.MakeArrayType` calls `Type.MakeArrayType`
- `JNINativeWrapper.CreateDelegate` calls the `DynamicMethod` constructor

That matches the report's table. The arm64 NativeAOT log still prints one `warning IL3053` on `linked/Mono.Android.dll` (`evidence/arm64-publish-nativeaot.log`). `evidence/arm64-publish-exits.txt` records `NATIVE_EXIT:0`. The default KVM publish does the same and ends `EXIT:0` (`evidence/startup-publish-default-kvm.log`). Those two logs are a warning that was allowed, which is the `WarningsNotAsErrors` behavior the KVM section describes. `evidence/nativeaot-x64-validate-publish.log` is the log that is supposed to show the warning gone. It is 14 lines, the two ILC "will always throw" notes are present, `warning` and `error` are absent, and the file has no exit line. `XA1040` is absent there too.

`NoWarn` drops `IL3053` wherever the SDK forwards that list. A code in `NoWarn` is not emitted, so it cannot fail the build. "It is not in `WarningsNotAsErrors`" is a different list and is not what keeps a warning visible. `IL3050` is not in `NoWarn`. An `IL3050` that is actually emitted for this repo would fail under `TreatWarningsAsErrors`.

The ILLink targets on this machine turn single-warn off for the entry assembly and for project references (`Microsoft.NET.ILLink.tasks` 10.0.14, lines 284–304), and leave it on for other assemblies. That is the setting that keeps a project warning as `IL3050` instead of folding it into `IL3053`. The checked-in test `Release_publish_hides_only_the_workload_il3053` only reads those csproj strings. This pass did not add a `MakeGenericType` call to `NativePoc.Core` and publish. The ILC task that prints `IL3053` during "Generating native code" was not in the local package cache, so this pass did not confirm that ILC reads the per-assembly single-warn flag. With the current SDK defaults, a warning in this repo stays an `IL3050`. The csproj itself does not scope `NoWarn` to `Mono.Android.dll`.

### Release listener

`HotReloadServer.cs` is `#if DEBUG` from the first line through the last. `HotReloadServer.Start(this)` is inside `#if DEBUG` (`MainActivity.cs:43-45`). A Release compile of this csproj does not contain that call.

The APK check the report cites is not in the cited file. `evidence/release-build-token-contract.log` is `dotnet build -c Release -p:PublishAot=false`: 13 AOT images, `0 Warning(s)`, `RELEASE:0`. It does not print a DLL length or a string search. The sentence that the Release DLL is 12800 bytes and lacks `HotReloadServer`, `nativepoc-hotreload.token`, `LocalServerSocket`, and the other loader strings is not in that log. The arm64 section says a scan of the three NativePoc APKs found none of those strings and names no evidence file. The NativeAOT validate log names one `libNativePoc.Android.so` and does not scan it. This pass did not have the APK bytes.

### Report numbers that were checked

KVM `TotalTime`, ten `COLD` starts each, even-n median. Recomputed from the logs:

| Series | Log | Recomputed p50 | Report |
| --- | --- | --- | --- |
| NativeAOT explicit, primary | `evidence/startup-nativeaot-kvm.log` | 866.0 | 866.0 |
| Profiled, primary | `evidence/startup-profiled-kvm.log` | 967.0 | 967.0 |
| Full AOT, primary | `evidence/startup-fullaot-kvm.log` | 949.5 | 949.5 |
| NativeAOT default | `evidence/startup-default-kvm.log` | 968.5 (865–1153) | 968.5 |
| MAUI after that default | `evidence/startup-maui-final-kvm.log` | 1066.5 (961–1276) | 1066.5 |
| MAUI steady | `evidence/startup-maui-steady-kvm.log` | 1102.5 | 1102.5 |

The default NativeAOT median is under the MAUI median beside it. The ranges overlap, and the report says so. Hot-reload series (a) in `evidence/hot-reload-a.log` is the 20 samples in the table, all pid 14365, all `collectible=True`.

Mac arm64, `pixel_8`, API 36, n=20, is recorded as coordinator percentiles only. The report says the raw samples were not provided. They are not in `evidence/`. The script's type-7 definition is the one the report states: for samples 1 through 20, p50 is 10.5 and p90 is 18.1. That arithmetic is right. It does not confirm 1300, 1428.5, 1533.5, or 1604. NativeAOT is the lowest of the four recorded p50 figures. Its recorded p90, 1711.3, is not the lowest of the four.

### Remaining blockers

- The Mac arm64 startup comparison cannot be recomputed. Item 16 on that device rests on percentiles with no sample list.
- No Release APK was available here, and the string-scan claim for the Release DLL is not in the log that is cited for it. The `#if DEBUG` wall is the evidence this pass could check.
- The abstract socket is still named `npoc-` plus the token. `/proc/net/unix` was unreadable with `run-as` on that boot.
- The KVM section contradicts the cnative section and the csproj on the socket name and on `WarningsNotAsErrors`.
- `NoWarn` for `IL3053` is project-wide. The reason a warning from this repo would still fail is the SDK's single-warn exception plus `IL3050` staying out of `NoWarn`, and that path was not probed with a call in this repo.
- The validate publish log that drops the `IL3053` line has no exit status. `XA1040` is still absent.
- Not run on device: a second `Start`, a root swap through `SetContentView`, `View.Dispose`, and an IME session across a keyed move. The keyed device move runs in `OnCreate` before input.
- `dotnet watch` is still a restart, from the earlier evidence. iOS was not built.

## Re-review of c9f454f

Re-reviewed the fixes on `c9f454f` ("Authenticate the debug loader and record the review fixes"). Sources were read from that commit. `src/` was not modified. The signed APKs are gitignored, so the new NativeAOT uncompressed size and the APK hashes were not recomputed here.

### Verdict

The loader, the receiver, the single listener, the render rollback, the `button-mounted` label, and the `EditText` content description are fixed in the code and show up in the logs. Keyed identity works when every sibling has a `Key`. `Screen.Render` sets none, so the screen that was measured still diffs by index. The new "Review fixes" section of the report matches the logs. Later sections of the same report still describe the TCP listener as the current design. Cold-start p50 **1517.5 ms** against the MAUI template's **1101 ms** matches the new `am start` logs. Those review edits do not account for the gap.

### What was executed

- `dotnet test tests/NativePoc.Core.Tests` on the `c9f454f` tree, SDK 10.0.403. **Passed 15, failed 0, skipped 0, 74 ms.** `evidence/unit-tests-review.log` is the same 15/0 result at 66 ms.
- A throwaway in-memory host, left outside the repo, for keyed identity and native child order: middle delete, insert at the front, insert in the middle, a parent swap that also changes a child's kind, a nested column, and 2000 random keyed renders. Identity and order mismatches: 0. Deleting one of four keyed siblings removed all four direct children and reinserted the three that stayed. The same host, with no keys, still displayed the surviving text on the removed sibling's view.
- The emulator was not booted again. Device conclusions are from `evidence/logcat-review-fixes.log`, `evidence/review-device.log`, the UI dumps, the loader scan, and the `am start` logs.

### Per-item verdicts

| Earlier finding | Verdict | Code at c9f454f | Log evidence |
| --- | --- | --- | --- |
| 1. Unauthenticated TCP loader, open `Resolve`, exported recreate receiver | **Resolved, with a Debug residual** | Abstract socket `npoc-` plus the token hex (`HotReloadServer.cs:57`). First 16 bytes checked with `FixedTimeEquals` (`HotReloadServer.cs:107-113`). `Start` runs only when the `nativepoc_hotreload` extra is non-empty (`MainActivity.cs:40-43`). `Resolve` returns `NativePoc.Core` or a default-context `System.Private.CoreLib` / `netstandard` / `mscorlib` / `System.*` name (`HotReloadServer.cs:206-230`). Receiver is `Exported = false` with `android.permission.DUMP` (`RecreateReceiver.cs:6`). `INTERNET` is gone from `AndroidManifest.xml`. | Pid 5119: one `hotreload-listen localabstract:npoc-<redacted-token>`, then `UnauthorizedAccessException`, then `Counter 0 collectible=True`. `tcp-has-494D=False`, `unix-has-token=True`. Shell broadcast `result=0`, `listen-already-after-broadcast=0`, pid stayed 5119. Release DLL scan line is 12800 bytes and lists `title=:utf16` only. Release manifest has the activity and no `INTERNET` or `RECREATE` receiver. |
| 2. `Start` bound 18765 again on every Debug `OnCreate` | **Resolved** | `Interlocked.CompareExchange` (`HotReloadServer.cs:25-28`). The bind sits inside the try (`HotReloadServer.cs:59-68`) and a bind failure clears `_started`. | `listen-lines=1`, `listen-already=2` after two rotations. The following send returned `Counter 1 collectible=True` on pid 5119. |
| 3. A thrown `Render` left the broken delegate installed | **Resolved for that failure** | `UseRender` restores the previous delegate when `RenderNow` throws (`MainActivity.cs:86-98`). `Apply` stores the load context after `UseRender` returns (`HotReloadServer.cs:176-178`). | One `MissingMethodException`. The next keystroke logged `rendered count=1 name-length=4 title=Reloaded 1`. `shape-missing-count` stayed 1. The restored DLL returned `Counter 1 collectible=True` on the same pid. |
| 4. Index identity; middle delete and reorder reused the wrong view | **Partial** | `Element.Key`, `DiffKeyedChildren`, and `ApplyReorder` are in the tree. Mixed keys and duplicate keys throw. | The 15 unit tests pass, including the three keyed identity tests. The throwaway host found no keyed identity or order bug. `Screen.Render` (`Screen.cs:9-31`) sets no `Key`, and the unkeyed middle delete still keeps the removed view. |
| 5. Report figures disagreed with the artifacts | **Partial** | The "Review fixes" section and the size note under the measurements table were rewritten. The loader section (~line 111), the source tree (~line 139), and the Debug run steps (~line 175) still describe a current `TcpListener` on `127.0.0.1:18765` and `INTERNET` in the app manifest. | p50 arithmetic, the new `force-stop` lines, the withdrawn pids, and the content description match the files named below. The new NativeAOT `.so` size 5212960 is not in `evidence/apk-listings.txt`. |
| 6. Listeners never detached; `button-mounted` logged from `SetButton` | **Resolved in code; dispose was not exercised on device** | `button-mounted` is logged from `OnMounted` (`AndroidHost.cs:108`, `:136-141`). `OnUnmounted` detaches and calls `Dispose` (`AndroidHost.cs:118-122`). `Retarget` rebinds without that log (`AndroidHost.cs:111-116`). | `button-mounted` stayed 1 across the title change to `Store` and the restore, then 3 after two rotations (new activities). The session never removed a widget. `MemoryHost.OnUnmounted` is empty. |
| 7. `SetContentView` once, so a replaced root stays off-screen | **Open** | Still the single call at `MainActivity.cs:39`. | No new trial. `Screen.Render` still returns a `Column`. |
| 8. `EditText` content description | **Resolved** | `SetTextField` assigns `ContentDescription` (`AndroidHost.cs:82`). The screen passes `"Name"` as placeholder and accessibility label (`Screen.cs:19-20`). | Empty Release dump: `EditText` `text="Name"` `content-desc="Name"`. Saved dump: `text="Ada"` `content-desc="Name"`. Device log: `empty-content-desc=Name`, `typed-content-desc=Name`. |

Depth 4 in `NodePath` and the Save path that increments `Count` before `CopyNonce` (`Screen.cs:24-27`) are unchanged. The new Debug log does contain `clipboard-ok poc-1` before `rendered count=1`.

### Residuals and new issues

The token check holds for a listener that is already up. It is the whole of the access control.

- **Debug, medium.** `MainActivity` is still `Exported = true` (`MainActivity.cs:8`). `OnCreate` starts the listener with whatever `nativepoc_hotreload` extra that launch carries, and the socket name is that token (`HotReloadServer.cs:57`, logged at `:70`). A process that has not called `Start` — a launcher start is this case — will bind a token chosen by whoever starts the activity. A second `Start` logs `hotreload-listen already` and keeps the first token. The well-known TCP port is gone. A Debug code-load remains available to an app that can start this activity.
- **Low.** The same hex is in logcat, in `/proc/net/unix` (`@npoc-<redacted-token>`, seven lines in `evidence/review-proc-net-unix.txt`), and in the host temp file `nativepoc-hotreload.token` (`Program.cs:507`). On API 34 another app generally cannot read logcat or `/proc/net`. `adb` can.
- **`Resolve` is an allow-list, and the loaded method still runs in-process.** A legitimate Screen DLL loaded (`collectible=True`). There is no device trial of a DLL whose dependency is `Mono.Android`. A `Render` that is already running can reflect over assemblies the process has loaded. The report describes the resolver, and the resolver matches that description.
- **Rollback restores the delegate.** `ViewReconciler.Render` assigns `_current` before it applies patches (`ViewReconciler.cs:53-57`). A throw from a later patch would leave a half-updated tree and the next diff would start from the previous element. The recorded `MissingMethodException` is thrown inside the new `Render`, before `Render` on the reconciler.
- **Keyed structural edits detach every sibling.** Identity survives; the view is removed and reinserted, so focus and the IME do not. The measured screen does not set keys, so it neither gains the identity fix nor pays that detach.
- **Dispose runs while the view can still be a child.** `UnmountDescendants` calls `OnUnmounted` before `Detach` removes the ancestor. That was not captured on a device.

### Startup, 1517.5 ms versus 1101 ms

Both series are ten `LaunchState: COLD` runs with `force-stop` before each.

PoC, `evidence/am-start-mono-aot-x64-review.log`: 1704, 1429, 1775, 1416, 1596, 1585, 1450, 1634, 1406, 1216. Sorted center pair 1450 and 1585, mean **1517.5**. Range 1216–1775.

MAUI, `evidence/am-start-maui-x64.log`: 1093, 1129, 1109, 1144, 1135, 1010, 1057, 1067, 1177, 1083. Sorted center pair 1093 and 1109, mean **1101**. Range 1010–1177.

The previous PoC series on workload 36.1.2 is in `evidence/mono-aot-x64-functional.log`: 1433, 1488, 1124, 1449, 1068, 1152, 1331, 1382, 1551, 1513. That even-n p50 is 1398, range 1068–1551. The new range overlaps it. A 119 ms median move sits inside that spread. `evidence/workload-list-review.txt` records the android workload at 36.1.118. MAUI was measured on that same workload, so the upgrade can sit inside the PoC-versus-earlier-PoC shift and leaves the gap to MAUI in place.

The review edits are a poor fit for about 400 ms of Release cold start:

- The listener is `#if DEBUG`, and `Start` runs only when the intent extra is set. A Release `am start` does not take that path.
- `Screen.Render` sets no `Key`, so `ApplyReorder` does not run.
- The widget-path additions are a `ContentDescription` assignment and the listener dictionary. Pid 5806 logs `activity-start tick=524199` and `button-mounted tick=524295`, a **96 ms** span (`evidence/logcat-mono-aot-x64-review.log`). The report's functional `TotalTime` for that launch is 1623 ms and is outside the ten. The ten cold starts have no logcat, so their `TotalTime` is not split into time before `OnCreate` and time after the button log.
- The new Mono publish AOT-compiles 13 assemblies, including `System.Collections.dll.so` (`evidence/mono-aot-x64-review.log`, `EXIT:0`, no `warning` line). The 36.1.2 rebuild compiled 12 and did not list `System.Collections` (`evidence/mono-aot-x64-rebuild.log`). One extra facade image is a real difference and a small one.

The publish logs show a different shape, and a faster one on the larger app. The PoC APK is 3795146 bytes, with `RunAOTCompilation` and `TrimMode=partial` (`NativePoc.Android.csproj:17-20`). Pid 5806 logged `IsDynamicCodeCompiled=True`. The MAUI publish (`evidence/maui-x64-publish.log`) compiles **89** AOT images, enables XAML source generation, and exits 0. The report states both packages contain uncompressed `libmonosgen-2.0.so` at 3290280 bytes. This review did not stat that file. A 19 MB APK with 89 AOT images and a lower `TotalTime` means the gap is not extra native code in the PoC. The 96 ms widget span cannot account for 416 ms. The rest is process and runtime startup before `activity-start`, plus the time from `button-mounted` to the frame `am start -W` reports. MAUI has no logcat ticks here, so the two managed spans were not compared. The concrete reading of the build is that this activity builds the tree on the UI thread and then calls `SetContentView`, on a Mono AOT runtime that still reports a JIT, while the MAUI template's first frame is a source-generated AppCompat screen with a much larger precompiled set. That is the build shape. It is not a startup trace.

### Acceptance items, scored again

| Item | Verdict | Why this score moved, or stayed |
| --- | --- | --- |
| 1. Four widgets and runtime flags | **Pass** | Pid 5806: `IsDynamicCodeSupported=True IsDynamicCodeCompiled=True`, empty UI `Counter 0` / `Name` / `Save` / `Enter a name`. |
| 2. `Ada`, 20 keystrokes, same `EditText` | **Pass** | The 20-line handle log is unchanged. Pid 5119 typed `Ada` and then `X`. |
| 3. Save keeps the field and shows `Counter 1` | **Pass** | Pid 5119: `clipboard-ok poc-1`, `rendered count=1 name-length=3`. |
| 4. Empty name still increments | **Pass** | The earlier empty-save logs still stand. This session did not repeat that save. |
| 5. Recreate and rotation keep state | **Pass** | Rotation on pid 5119 kept `count=1 name-length=4` with one listen line and two `already` lines. The shell broadcast returned `result=0` and did not enter `OnReceive`. |
| 6. Clipboard round-trip | **Pass** | `clipboard-ok poc-1` is in `evidence/logcat-review-fixes.log`. |
| 7–10. In-place edit, n=20, same pid, field kept | **Partial** | The n=20 series is the TCP loader. The abstract-socket path has four `once` samples, 27.2, 56.1, 16.4, and 19.3 ms, all on pid 5119. `dotnet watch` still restarts the process. |
| 11. Rude edit recorded | **Pass** | The shape-change trial now includes the keystroke that stays on `Reloaded 1`. |
| 12. Release Mono AOT, items 1–6 | **Partial** | The new x64 APK installed and showed the empty screen on pid 5806. Items 2–6 of this build ran on Debug pid 5119. Arm64 was not installed. |
| 13. `libaot-*.so` under `lib/arm64-v8a/` | **Pass** | The arm64 listing is unchanged. |
| 14. No trim/AOT warnings from this repo | **Pass** | The review Mono log has no `warning` line. NativeAOT still logs one `IL3053` on `Mono.Android`. |
| 15. APK smaller than `dotnet new maui` | **Pass** | 3795146 versus 19074917, same emulator and SDK. The figures are the report's; the APKs were not re-hashed here. |
| 16. Startup median at or below MAUI | **Not met** | 1517.5 versus 1101. |
| 17. NativeAOT publish, XA1040 present | **Fail** | `evidence/xa1040-review-grep.txt`: `rg` exits 1 on the new log. The publish exits 0. |
| 18. NativeAOT runs items 1–4 | **Pass** (previous x64 run) | The review NativeAOT APK was not installed. The 464b63e process is still the last NativeAOT execution. |
| 19. NativeAOT failed | **Not applicable** | That process reached `rendered`. |

Items 1–16 are still short of the plan's "fully working" bar: item 16 is not met, and items 7–10 are still the watch-versus-loader split.

The first review below is the pass against `a2d0103`. Its `file:line` citations are that tree.

## First review, tree a2d0103

Reviewed against `research/android-poc-report.md`, `research/runtime-feasibility.md`, the tree at `a2d0103`, and `evidence/`. Sources under `src/` were not modified. The signed APKs cited by the report are gitignored (`evidence/apk/`), so this review did not re-hash those binaries. It did re-read the logs, the UI dumps, `evidence/apk-listings.txt`, and `evidence/mono-aot-x64-loader-scan.txt`.

## Verdict

The proof is **not fully working** under the plan. Items 1–16 are the plan's bar. Items 15 and 16 were not run, `dotnet watch` does not update the Android process in place, and neither arm64 APK was installed.

The x64 screen itself is real. Debug, Release Mono AOT, and experimental NativeAOT all showed the four widgets, a stable `EditText` handle while typing, Save, the empty-name greeting, clipboard read-back, and rotation that kept `AppSession`. The hot-reload percentiles and the `am start -W` medians that are written down in the logs recompute exactly. Shipping code (`Release`, NativeAOT) has no reflection and no runtime codegen in this repo. The collectible loader is `#if DEBUG`, binds `127.0.0.1` only, and the Mono AOT APK scan does not contain its strings.

That loader is still an unauthenticated code-load socket. On Android, loopback is shared by every app on the device, so "loopback-only" is weaker than the report's wording. Two code defects sit on paths the acceptance run already cares about: `HotReloadServer.Start()` runs again on every `OnCreate`, and a failed apply leaves the broken `Render` delegate installed.

## What was executed in this review

- `dotnet test tests/NativePoc.Core.Tests` on SDK `10.0.403` (installed in this environment; it was not on `PATH` at the start). **Passed 12, failed 0, skipped 0, 72 ms.** The checked-in log `evidence/unit-tests-hotreload.log` reports the same 12/0 result at 52 ms on the original machine.
- A throwaway harness against `NativePoc.Core` (not committed, not under `src/`) for sibling identity and 16,000 random re-renders. Structural mismatches: 0. Middle-delete and reorder reused the wrong view instance. Details are under finding 4.
- The Android workload and the emulator are not on this machine. Release publish, NativeAOT, and the on-device checks were not repeated.

`git diff af90e2b..HEAD -- src/NativePoc.Android/AndroidHost.cs src/NativePoc.Core/ViewReconciler.cs src/NativePoc.Core/Diff.cs` is empty. Handle-stability logs from before the screen assembly was split still describe the current reconciler and the current Android host.

## Acceptance items

Statuses are against the plan's items in `research/runtime-feasibility.md`. "Pass" means the recorded run meets the item. A pass on `android-x64` is not an arm64-device pass.

| Item | Verdict | Why |
| --- | --- | --- |
| 1. Four widgets and runtime flags | **Pass** (x64 Debug, Mono AOT, NativeAOT) | `evidence/debug-empty-ui.xml` has `TextView` `Counter 0`, `EditText`, and `Button` text `Save` with `content-desc="Save name"`. Flags match the logs: Debug `Supported=True Compiled=False` (`evidence/logcat-debug-runtime.log`), Mono AOT `True/True` (`evidence/logcat-mono-x64-series.log`), NativeAOT `False/False` (`evidence/logcat-nativeaot-x64-series.log` and the 464b63e rebuild). |
| 2. `Ada`, 20 keystrokes, same `EditText` | **Pass** | `evidence/logcat-field-handles.log` is 20 lines, pid 9673, handle `15046`, `name-length` 1 through 20. `evidence/logcat-nativeaot-handles.log` is the same shape on pid 12212, handle `12006`. The 464b63e rebuilds only retyped `Ada` (three keystrokes, plus the initial mount log of the same handle). The host that logs the handle did not change after those 20-keystroke runs. |
| 3. Save keeps the field and shows `Counter 1` | **Pass** | Debug pid 6501 (`evidence/logcat-save.log`), Mono AOT pid 7935 (`evidence/logcat-mono-x64-save.log`), and the later rebuilds (pids 13736 and 14459) all log `clipboard-ok poc-1` and `rendered count=1 name-length=3`. |
| 4. Empty name still increments | **Pass** | Debug pid 9673: `name-length=0` then `clipboard-ok poc-1` / `count=1` (`evidence/logcat-empty-save.log`). NativeAOT pid 12212: the same pair (`evidence/logcat-nativeaot-empty-save.log`). |
| 5. Recreate and rotation keep state | **Partial** | Mono AOT pid 13736 and NativeAOT pid 14459 rotate on the same pid, with a new field handle and `count=1 name-length=3` (`evidence/logcat-mono-aot-x64-rebuild.log`, `evidence/logcat-nativeaot-x64-rebuild.log`). The Debug broadcast log is two lines at pid 6501 and has no `hotreload-listen` (`evidence/logcat-recreate.log`). It does not show a second runtime line. Current `OnCreate` calls `HotReloadServer.Start()` on every create, which the recorded Debug recreate never exercised. See finding 2. |
| 6. Clipboard round-trip, no binding project | **Pass** | `PlatformBridge.CopyNonce` (`src/NativePoc.Android/AndroidHost.cs:152-161`) uses `ClipboardManager` and throws when the read differs. The tree has no `ApiDefinition` or binding project. |
| 7–10. In-place edit, n=20, p50 ≤ 1500 ms, p95 ≤ 3000 ms, same pid, field kept | **Partial** | The Debug assembly loader meets the numbers and the stability rules. Series (a) p50 is 17.5 ms, warm p50 17.4 ms, max 758.4 ms, pid 15262, `Ada` kept (`evidence/hot-reload-a.log`). Unpublished p95 on that series is 52.8 ms (nearest rank on the sorted samples). `dotnet watch` does not. The plan required `dotnet watch` method-body edits. The watch logs restart the app, and `evidence/hot-reload-poll.txt` shows pid 7320 with `Tally 0` and `Enter a name`. The 5459 ms / 5502 ms table and pids 7177 and 7443 are not in `evidence/dotnet-watch.log`. |
| 11. Rude edit recorded | **Pass** | The loader's refusal is in `evidence/hot-reload-edits.log`: `MissingMethodException` for `AppState.get_Extra()`, pid stayed 15856, UI left on `Rebound 1` / `Ada`. The watch log does record restarts (`Project does not support Hot Reload`, `Stopping: com.scratch.nativepoc`). The cited pid pair 9931 → 10056 is not in `evidence/dotnet-watch-rude.log`. That file also builds only `NativePoc.Core` and `NativePoc.Android` (no `NativePoc.Screen`), so it is a pre-split session. The item only requires the outcome to be recorded. |
| 12. Release Mono AOT installs and items 1–6 pass | **Partial** | `android-x64` rebuild pid 13736: widgets, three-keystroke `Ada` on handle 14758, Save, rotation, clipboard, ten `COLD` starts, p50 1398 ms. Arm64 was built (`evidence/release-mono-aot-arm64.log`, 11 AOT images, no `NativePoc.Screen`) and not installed. The 20-keystroke count was not repeated on pid 13736. |
| 13. `libaot-*.so` under `lib/arm64-v8a/` | **Pass** | `evidence/apk-listings.txt`: Mono AOT arm64 APK is 3606628 bytes and contains `libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so` (3123640), and `libmonodroid.so`. It has no `libNativePoc.Android.so`. It also has no `libaot-NativePoc.Screen.dll.so`. That listing is the pre-split APK. The x64 rebuild scan is the one that contains `libaot-NativePoc.Screen.dll.so` (5168 bytes). |
| 14. No trim/AOT warnings from this repo | **Pass** | Release Mono logs have no `warning` line. `evidence/release-build-after-hotreload.log` reports `0 Warning(s)`. NativeAOT's `IL3053` is on `obj/.../linked/Mono.Android.dll`. The IL2026/IL2070/IL2072/IL2075 suppressions are on the Debug loader only (`HotReloadServer.cs:96-99` and `:143`). |
| 15. APK smaller than `dotnet new maui` | **Fail** | No MAUI template was built. The report says this. |
| 16. Startup median ≤ MAUI, 10 runs | **Fail** | No MAUI process was started. Own `TotalTime` series exist and are not a comparison. |
| 17. NativeAOT publish, XA1040 present | **Fail** | `android-arm64` and `android-x64` publishes got far enough to run ILC and, on x64, to boot. `XA1040` is absent from `evidence/nativeaot-arm64.log`, `evidence/nativeaot-x64.log`, and `evidence/nativeaot-x64-rebuild.log`. The csproj lists `XA1040` in `WarningsNotAsErrors`, which did not matter because the warning was not emitted. The plan's item requires the warning to be present. |
| 18. NativeAOT runs items 1–4 | **Pass** (x64 only) | Pid 14459: `Supported=False Compiled=False`, widgets, `Ada` on handle 12006 across the three keystrokes, `clipboard-ok poc-1`, rotation kept `Counter 1`. Empty-save and the 20-handle series are the earlier pid 12212 publish of the same host. Arm64 was not installed. Ten cold starts, p50 1298 ms, all `LaunchState: COLD`. |
| 19. NativeAOT failed | **Not applicable** | The x64 process reached `rendered`. The earlier arm64 link failure was a missing NDK (`libc++_static.a`) and the later publish succeeded. Item 19 is the failure branch of 17–18. |

The plan's "fully working" sentence is items 1–16. That bar is **Fail**. NativeAOT on x64 is a real experimental pass (item 18), with item 17 failed because XA1040 never appeared.

## Findings

### 1. High — Debug loader is unauthenticated code execution, and Android loopback is not per-app

`src/NativePoc.Android/HotReloadServer.cs:29` listens on `IPAddress.Loopback` port 18765. `Handle` (`HotReloadServer.cs:59-93`) reads a length-prefixed blob up to 8 MB and loads it. There is no token, no signature check, and no allow-list of callers. `Resolve` (`HotReloadServer.cs:153-162`) binds any dependency whose simple name matches an assembly already in the default context, including `Mono.Android`. A payload whose `NativePoc.Screen.Render` exists therefore runs with the app's privileges and the full SDK surface.

`evidence/hot-reload-edits.log` shows `/proc/net/tcp` state `0A` on `0100007F:494D` (127.0.0.1:18765) and no 18765 listener in `/proc/net/tcp6`. That matches the code. It does not isolate the port from other apps. On Android the loopback interface is shared by every app on the device, so any local app can connect to a Debug process that has reached `OnCreate` and send a DLL. `adb forward` makes the same port available to the host; that part is the measurement tool.

The same Debug surface exports a recreate broadcast with no permission: `src/NativePoc.Android/RecreateReceiver.cs:6-7` (`Exported = true`, action `com.scratch.nativepoc.RECREATE`). Any app can send it. Combined with finding 2, that broadcast is enough to hit the second bind.

Release does not compile this listener. See the AOT section. The manifest still declares `android.permission.INTERNET` for every configuration (`src/NativePoc.Android/AndroidManifest.xml:3`), including the Release manifests in `evidence/mono-aot-x64-AndroidManifest.xml` and `evidence/nativeaot-x64-AndroidManifest.xml`. Those Release manifests have no `RECREATE` receiver. The permission is unused once the listener is compiled out.

### 2. Medium — every Debug `OnCreate` starts another listener

`MainActivity.OnCreate` calls `HotReloadServer.Start()` unconditionally inside `#if DEBUG` (`src/NativePoc.Android/MainActivity.cs:40-42`). `Start` (`HotReloadServer.cs:17-24`) always creates a new background thread. `Listen` calls `listener.Start()` (`HotReloadServer.cs:29-30`) **before** the accept loop's try/catch (`HotReloadServer.cs:35`). There is no "already bound" flag and `OnDestroy` (`MainActivity.cs:45-49`) does not stop the thread.

Activity recreate and rotation both run `OnDestroy` then `OnCreate` in the same process. The first thread still holds 18765. The second `TcpListener.Start()` throws `SocketException` (address in use) on a thread with no handler. On .NET an unhandled exception on a non-UI thread terminates the process. This review did not boot the emulator, so that crash is not an observed log. The Debug recreate evidence cannot clear it: `evidence/logcat-recreate.log` is from pid 6501 at 10:49 and contains no `hotreload-listen`. The listener logs that do exist (`evidence/logcat-hotreload-a.log`, pid 15262) are from a fresh start, one `hotreload-listen` line, no rotation.

Mono AOT and NativeAOT rotation are unaffected. The call is compiled out of those builds.

### 3. Medium — a rejected screen DLL still replaces the render delegate

`MainActivity.UseRender` assigns `_render` and then calls `RenderNow` (`src/NativePoc.Android/MainActivity.cs:84-88`). `HotReloadServer.Apply` calls `UseRender` before it stores the new `AssemblyLoadContext` (`HotReloadServer.cs:122-125`).

If `Render` throws, as it did for `AppState.get_Extra()`, the exception escapes `RenderNow` before any patch is applied, so the widgets stay on the previous tree. `_render` already points at the new delegate. The next keystroke runs the old `TextField.OnChange`, which updates `AppState` and then calls `RequestRender` (`src/NativePoc.Screen/Screen.cs:14-17`). That render calls the broken delegate again. The new context was never stored in `_loaded`, so nothing unloads it, and the delegate keeps it alive until a later successful apply.

The recorded shape-change trial matches the "UI unchanged" half: pid 15856 stayed on `Rebound 1` / `Ada`, and a later good DLL restored `Counter 1` (`evidence/hot-reload-edits.log`). Nothing in that log types into the field between the `MissingMethodException` and the restore. The report's "leaves the previous screen and the previous state in place" is true for that instant and false for the next edit.

`collectible=True` in the apply reply is the flag passed to the `AssemblyLoadContext` constructor (`HotReloadServer.cs:105-107`), written out at `HotReloadServer.cs:138`. It does not mean the previous context was collected. `Unload` (`HotReloadServer.cs:126-136`) does not throw when references remain, and no `hotreload-unload` line appears in the hot-reload logs. The non-collectible fallback (`HotReloadServer.cs:109-112`) never unloads. Every sample in the n=20 logs took the collectible path, so that fallback was not hit.

### 4. Medium — identity is the sibling index, so reorder and middle removal reuse the wrong view

There are no keys. `TreeDiff.DiffChildren` (`src/NativePoc.Core/Diff.cs:141-153`) diffs the shared prefix by index, unmounts only the tail, and mounts only the new tail.

A throwaway run against this `ViewReconciler` showed:

- `[Text a, Text b, Text c]` → `[Text a, Text c]`: index 0 kept `a`'s view; index 1 kept `b`'s view and then displayed `c`. `c`'s view was the one unmounted.
- `[Text a, Text b]` → `[Text b, Text a]`: both views stayed put; the text was overwritten.
- `[TextField keep, TextField shifted]` → `[TextField shifted]`: the surviving view was the first field's instance, with the second field's value.

Props end up equal to the new tree, because `SetText` / `SetTextField` / `SetButton` run on whatever view still occupies that index. Handlers do the same: `AndroidHost.OnMounted` captures the path and reads `TreeDiff.At` on the current tree (`AndroidHost.cs:106-137`). After a middle delete, the click or text callback is the new element's callback. The native object is still the one that used to be the deleted sibling, so cursor, IME composition, focus, and scroll belong to the wrong widget.

The fixed four-child screen never reorders, so items 1–6 do not hit this. The report's reconciler section is right that a patch did not leave an orphan view or drop a sibling that the new tree still contains: 16,000 random re-renders in the throwaway harness (columns, texts, buttons, fields, nesting up to three levels) produced 0 structural mismatches. The same section understates the identity bug. "Inserting at the front still rewrites the shared prefix" is this algorithm; middle removal is the sharper case, and no test covers it.

Tests that do exist only insert or remove at the end (`tests/NativePoc.Core.Tests/ReconcilerTests.cs:151-163` and `:242-253`). `MemoryHost.OnMounted` is empty (`tests/NativePoc.Core.Tests/Harness.cs:55-61`), so the suite cannot see a leaked or double-subscribed Android listener.

`UnmountDescendants` (`src/NativePoc.Core/ViewReconciler.cs:112-127`) drops descendant dictionary entries and does not call `Remove` on them. `Detach` then removes the ancestor. That is consistent for `ViewGroup.RemoveViewAt` and for the in-memory host, and the nested-column test locks the dictionary half of it.

### 5. Medium — several report figures are not in the cited artifacts

These are report defects. They do not change the pass/fail of the screen.

- **NativeAOT uncompressed library size is three different numbers.** `evidence/apk-listings.txt` (generated 2026-11-08T11:06:42Z) lists `lib/arm64-v8a/libNativePoc.Android.so` at **5331248** bytes and `lib/x86_64/libNativePoc.Android.so` at **5161952** bytes. The measurements table says **5330832** and **5161568**. The 464b63e verification paragraph says the x64 library is **5164368**. Compressed APK lengths in that same table match the listing exactly (arm64 Mono 3606628, x64 Mono 3774498, arm64 NativeAOT 2187616, x64 NativeAOT 2294109). `libmonosgen-2.0.so` at 3123640 and 3290280 also matches. Only the NativeAOT `.so` uncompressed sizes are wrong.
- **The loader scan does not show what the report attributes to it.** `evidence/mono-aot-x64-loader-scan.txt` records `18765`, `hotreload-listen`, `HotReloadServer`, `AssemblyLoadContext`, `nativepoc-screen`, `MetadataUpdater`, `DOTNET_STARTUP_HOOKS`, `activity-start`, and `title=`. It does not search for `TcpListener` or `LoadFromStream`. The Debug DLL line in that file is **18432** bytes and does contain `HotReloadServer`, `AssemblyLoadContext`, and UTF-16 `hotreload-listen`. A **11264**-byte Release `NativePoc.Android.dll` is not in the scan. The APK half of the claim holds: every Mono AOT x64 entry in the scan is `-1` for `hotreload-listen`, `HotReloadServer`, `18765`, and `nativepoc-screen`. `libmonosgen-2.0.so` and `libmonodroid.so` do contain the runtime string `AssemblyLoadContext`, which the report already says.
- **Watch-restart pids and timestamps are not in the cited logs.** `9931`, `10056`, `7177`, and `7443` occur in the report and nowhere under `evidence/`. `evidence/hot-reload-poll.txt` line 1 is the support that exists for the Tally restart: pid 7320, texts `Tally 0`, `Name`, `Save`, `Enter a name`. `evidence/dotnet-watch-rude.log` shows `StartupHookSupport` false and `Stopping: com.scratch.nativepoc`. It does not show those pids, and its build output has no `NativePoc.Screen`.
- **`logcat-recreate.log` has one runtime line, not two.** Pid 6501 matches `evidence/logcat-save.log` about 17 seconds earlier, and the line is `rendered count=1 name-length=3`. The "second runtime line" is not in the file.
- **`am start` logs do not contain `force-stop`.** `evidence/am-start-mono-aot-x64-rebuild.log` and `evidence/am-start-nativeaot-x64-rebuild.log` each have ten `LaunchState: COLD` rows and the `TotalTime` values the report lists. Cold state is consistent with a force-stop. The command itself is not in the file.

The clocks that are in the logs do match, including the even-n median (mean of the two central sorted samples):

| Series | Report | Recomputed from the log |
| --- | --- | --- |
| In-place n=10, `evidence/hot-reload-inplace.log` | p50 18.1 ms | 18.1 |
| (a) n=20 all / warm, `evidence/hot-reload-a.log` | 17.5 / 17.4 | 17.5 / 17.4 |
| (b) n=20 all / warm, `evidence/hot-reload-b.log` | 5.8 / 5.8 | 5.8 / 5.8 |
| (c) n=20 all, true center mean, `evidence/hot-reload-c.log` | 6.75, tool prints 6.7 | 6.75 and 6.7 |
| Mono AOT x64 rebuild, 10 `TotalTime` | p50 1398 | 1398 |
| NativeAOT x64 rebuild, 10 `TotalTime` | p50 1298 | 1298 |
| Earlier Mono series, discard first, `evidence/am-start-mono-x64-series.log` | 1689.5 | 1689.5 |
| Earlier NativeAOT series | 1816 | 1816 |
| Mono button span, 11 runs then median of the last 10 | 60 ms | 60 |
| NativeAOT button span, same method | 114 ms | 114 |

Series (a)'s clock starts before `File.WriteAllText` and stops when the post-render TCP reply is read (`tools/NativePoc.HotReload.Host/Program.cs:74-78`). The UI dump is after that. The plan's item 8 is "file save timestamp to the on-screen text." The recorded number ends at `RenderNow` plus the reply write, which is earlier than a `uiautomator` observation. The gap is small next to a 1500 ms gate: the slowest sample in series (a) is 758.4 ms, and the warm samples are under 53 ms. The report's description of what the clock includes matches the host.

`button-mounted` is a mislabeled log line, not a wrong subtraction. It is written from `SetButton` on every title or label change (`src/NativePoc.Android/AndroidHost.cs:65-71`), not from `OnMounted`. `evidence/hot-reload-edits.log` prints it again at 11:47:40 and 11:47:44 for handle `15302`, which was already the button, when the title changed. The startup spans above are `activity-start tick` minus that first `SetButton` tick, and the arithmetic matches. The name says the button was mounted.

The two startup tables (1689.5 ms vs 1398 ms, 1816 ms vs 1298 ms) are different runs. The report cites different files for them. They are not a single median told two ways.

### 6. Low — listeners are attached once and never removed; removed views are not disposed

`OnMounted` subscribes `Click` and `TextChanged` (`AndroidHost.cs:106-124`). `OnUnmounted` is empty (`AndroidHost.cs:127-129`). Re-render does not subscribe again, and the text-field callback bails out while `SetTextField` assigns `edit.Text` (`AndroidHost.cs:84-91`), which is what keeps the cursor during typing. That part of the report is accurate.

The subscription does not by itself pin the activity after unmount: the view holds the delegate, the delegate holds `AndroidHost`, and the reconciler drops the view. The Java peer stays until the finalizer because nothing calls `Dispose`. The plan's portability section asked the reconciler to drop the native reference on remove. This proof does not.

### 7. Low — a new root is reconciled and not installed as the content view

`SetContentView` runs once, after the first render (`MainActivity.cs:36-39`). `ViewReconciler.MountView` updates `Root` when the root path is replaced (`ViewReconciler.cs:92-93`). A later render whose root kind changes builds a new view, leaves it off-screen, and leaves the old tree installed. `Screen.Render` always returns a `Column`, and a same-kind root is reused, so the acceptance screen does not hit this. A hot reload that returned a non-column root would.

### 8. Low — other bounds

- `NodePath` throws past depth 4 (`src/NativePoc.Core/Diff.cs:15-16`). The acceptance screen is depth 2.
- Save increments `Count` before `CopyNonce` (`src/NativePoc.Screen/Screen.cs:23-26`). A clipboard throw leaves the counter bumped and the widgets unrendered until some later successful render. The recorded runs logged `clipboard-ok` before `rendered count=1`.
- `RequestRender` posts to the Android looper (`MainActivity.cs:78-81`). `Screen.Render` only calls `IPlatform.RequestRender`. That split matches the plan. The activity method is the Android half of it.

## AOT and trimming

Non-Debug application code does not reflect and does not generate IL.

- `HotReloadServer.cs`, `HotReload.cs`, and `RecreateReceiver.cs` are wrapped in `#if DEBUG` from the first line through the last. `MainActivity` calls `HotReloadServer.Start()` only inside `#if DEBUG` (`MainActivity.cs:40-42`). The csproj does not define `DEBUG` for Release.
- The reflection is `GetType`, `GetMethod`, and `CreateDelegate` (`HotReloadServer.cs:115-122`), plus `AssemblyLoadContext.LoadFromStream` (`HotReloadServer.cs:144-149`). Roslyn `Emit` is in `tools/NativePoc.HotReload.Host/Program.cs:262-280`, which is a host console app. The report says that tool is not in the APK. Nothing in the app project references the tool.
- `ViewReconciler`, `TreeDiff`, and `AndroidHost.Create` switch on closed types and assign properties directly (`view.Text`, `button.Text`, `button.ContentDescription`, `edit.Text`, `edit.Hint`). There is no `PropertyInfo`, `MakeGenericType`, `Activator`, or `Reflection.Emit` under `src/` outside the Debug loader.
- `IsAotCompatible` is set on Core, Screen, and the Android app. Release Mono sets `PublishTrimmed` and `TrimMode=partial` only when `PublishAot` is not true (`NativePoc.Android.csproj:17-20`).
- NativeAOT x64 logged `IsDynamicCodeSupported=False` and `IsDynamicCodeCompiled=False` and still ran Save and rotation. That is the run that actually exercises the trim. Mono AOT x64 logged `IsDynamicCodeCompiled=True`, so a Release Mono success would not by itself prove the NativeAOT path. The report says this, and the flags match the logs.

The suppressions are unconditional and would hide IL2026/IL2070/IL2072/IL2075 if the loader were ever compiled into a trimmed build. Today the methods are not in that compilation. Item 14 still passes on the evidence that exists. This review did not rebuild the APKs.

`MetadataUpdateHandler` (`HotReload.cs:3-20`) is also Debug-only and returns immediately when `MetadataUpdater.IsSupported` is false. `dotnet watch` never delivered a delta to the Android process (`evidence/dotnet-watch.log` waits on a pipe, then disposes it; `evidence/dotnet-watch-rude.log` says the project does not support hot reload). The handler is the plan's mechanism. It is not the mechanism that produced the 17.5 ms number.

## Accessibility

Item 1's button requirement is met. `SetButton` sets `ContentDescription` from `Button.AccessibilityLabel` (`AndroidHost.cs:70`, `Elements.cs:22`). The empty Debug dump has `content-desc="Save name"` on the `Button`. TalkBack prefers that over the visible label `Save`, which is what the plan asked for.

The other widgets are only as accessible as their visible text:

- `Text` has no label property. `SetText` (`AndroidHost.cs:58-62`) sets `TextView.Text` and leaves `content-desc` empty. TalkBack can read the text. The counter and the greeting are not marked as headings, and the greeting is not tied to the field as an error.
- `TextField` has a placeholder and no accessibility label (`Elements.cs:24`). `SetTextField` sets `Hint` and does not set `ContentDescription` (`AndroidHost.cs:74-80`). In `evidence/debug-empty-ui.xml` the `EditText` has `content-desc=""` and `text="Name"` (uiautomator reports the hint as text while the field is empty). After `Ada` is entered, the hint is no longer the node's text. There is no separate label widget and no `labelFor`.

That is enough for the acceptance dump and thin for TalkBack once the field is non-empty. The shared tree only carries an accessibility string on `Button`. A UIKit host would map that one field to `AccessibilityLabel` and would have nothing to map for the title, the field, or the greeting.

## iOS / UIKit portability

The plan's split is real, and it is not a UIKit port. iOS was not built. What is portable:

- `NativePoc.Core` has no Android or Java types. `Column`, `Text`, `Button`, `TextField`, `AppState`, and `AppSession` are plain C#. `IPlatform` (`Elements.cs:26-31`) is the clipboard and the render request. `Screen.Render` does not mention a looper or a view.
- `IElementHost<TView>` is the platform seam (`ViewReconciler.cs:1-21`). An iOS host can create `UIStackView` or a plain `UIView`, `UILabel`, `UIButton`, and `UITextField` without changing the diff.
- Prop updates are direct calls. NativeAOT on Android already ran this screen with the JIT off, which is evidence the shared tree is trim-safe. It is not evidence about the iOS static registrar or full AOT on device.
- State lives in `AppSession.Shared`, not on the activity (`Elements.cs:40-45`). A view controller can hold the same object. Process death drops it. The plan accepts that.

What does not transfer as written:

- The reload that actually stays in-process is `AssemblyLoadContext.LoadFromStream` of a new `NativePoc.Screen`. The plan calls that the wrong mechanism and says hot reload is `MetadataUpdater` on a method body, with no assembly load of a component assembly. The load is in the Android project, not in Core, so the sentence "no `Assembly.Load` in shared code" is literally true. The working design is still the one the plan said not to ship, and Apple's guideline 2.5.2 is why the `#if DEBUG` wall has to stay. iOS Debug on ARM64 is AOT plus the interpreter. The collectible-context fallback that already exists (`HotReloadServer.cs:109-112`) is the branch that would be taken if the runtime refuses a collectible context, and that branch never unloads. None of this was run on an iOS simulator.
- `OnUnmounted` does not dispose. UIKit peers need that. Finding 6.
- The cursor guard is in `AndroidHost.SetTextField`, not in the shared reconciler. A UIKit host that assigns `UITextField.Text` on every `SetTextField` will move the caret. The Android host skips the assign when the strings already match (`AndroidHost.cs:81-83`).
- Finding 4 (index identity) and finding 7 (root installed once) would show up on iOS with the same patch list.
- Accessibility data is only on `Button`, as above. The plan's "accessibility is data on the element" is true for the button and false for the other three widgets.
- `NodePath`'s depth cap is shared. A UIKit layout that nests further than four levels throws in the diff, on both platforms.

A second reconciler for this one screen is a plausible port. The hot-reload story the plan wanted for iOS was not demonstrated on Android either: `dotnet watch` restarted the process, and the in-place path is a Debug assembly swap.

## Reconciler and tests, short form

The in-memory reconciler keeps the mounted path set equal to the new tree for the cases the tests cover and for the random trees tried here. Button and text-field listeners read the latest element, so a title-only reload can keep the same widget identity, which is what `A_title_only_render_keeps_the_field_the_button_and_the_state` locks. The gaps are index identity, no detach, the activity not installing a replaced root, and the Debug loader's lifecycle and failure path. None of those are covered by the 12 unit tests.
