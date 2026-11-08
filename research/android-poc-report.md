# Android proof of concept

Measured on this machine on 2026-11-08. Nothing here is a guessed latency, a guessed size, or a screenshot that was not captured. iOS was not built and was not run.

The plan's "fully working" bar is items 1–16 in [runtime-feasibility.md](runtime-feasibility.md). That bar is **not met**: items 15 and 16 were not run, and the original hot-reload gate asked for n=20. Debug in-place reload of the screen assembly does work, measured below at n=10. `dotnet watch` still restarts the process. The screen works on Debug, on Release Mono AOT, and on experimental NativeAOT, on an x86_64 emulator. The Mono AOT and NativeAOT emulator runs predate the screen-assembly split and were not repeated.

## File tree

```
global.json
NativePoc.slnx
README.md
research/runtime-feasibility.md
research/android-poc-report.md
src/NativePoc.Core/
  NativePoc.Core.csproj
  Elements.cs          Column, Text, Button, TextField, AppState, AppSession
  Diff.cs              NodePath, patches, TreeDiff
  ViewReconciler.cs    IElementHost<TView>, applies the patch list
src/NativePoc.Screen/
  NativePoc.Screen.csproj
  Screen.cs            Screen.Render, the assembly the Debug reloader swaps
src/NativePoc.Android/
  NativePoc.Android.csproj
  AndroidManifest.xml  includes INTERNET, used by the Debug listener
  MainActivity.cs      holds the render delegate, logs RuntimeFeature
  AndroidHost.cs       LinearLayout, TextView, Button, EditText, ClipboardManager
  HotReload.cs         DEBUG MetadataUpdateHandler, calls RequestRender
  HotReloadServer.cs   DEBUG TCP listener, collectible AssemblyLoadContext
  RecreateReceiver.cs  DEBUG broadcast com.scratch.nativepoc.RECREATE
  Resources/values/strings.xml
tools/NativePoc.HotReload.Host/
  Program.cs           compiles Screen.cs with the SDK Roslyn and pushes the DLL
tests/NativePoc.Core.Tests/
  Harness.cs
  ReconcilerTests.cs
evidence/              logs, UI dumps, screenshots, apk-listings.txt
```

`bin/`, `obj/`, and `*.apk` are gitignored. The signed APKs used for the runs are on this machine under `evidence/apk/` and are not in git. `evidence/apk-listings.txt` is the `unzip -l` of those files.

There is no MAUI package reference. The Android project is `Microsoft.NET.Sdk` with `TargetFramework` `net10.0-android`.

## How to build and run

SDK `10.0.403`, workload `android` `36.1.2`. `ANDROID_HOME=/home/ubuntu/android-sdk`. JDK `/usr/lib/jvm/java-21-openjdk-amd64`. NDK `26.3.11579264` (required for NativeAOT link). Emulator image `system-images;android-34;google_apis;x86_64`, AVD `poc34`.

Host tests:

```bash
dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj
```

Debug deploy (fast deployment; do not `adb install` the Debug APK by itself):

```bash
dotnet build src/NativePoc.Android/NativePoc.Android.csproj \
  -c Debug -f net10.0-android -r android-x64 -t:Run -p:AdbTarget=-e
```

Debug in-place reload, after that deploy. The app listens on `127.0.0.1:18765`. The host forwards the port, compiles `src/NativePoc.Screen/Screen.cs` with the SDK Roslyn compiler, and sends the DLL. `AppSession` stays in the already-loaded `NativePoc.Core` assembly.

```bash
dotnet run --project tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
```

Release Mono AOT, then NativeAOT:

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64

dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64 \
  -p:PublishAot=true -p:EnablePreviewFeatures=true \
  -p:TreatWarningsAsErrors=false
```

`android-x64` is the RID that matches this emulator. `android-arm64` is the phone RID. An arm64 APK was not installed here because the image is x86_64.

## What ran where

| | BUILT | TESTED (unit) | RUN ON EMULATOR | UNVERIFIED |
| --- | --- | --- | --- | --- |
| Reconciler and session state | yes | 12/12 on the host after the screen split | the screen was exercised separately | unit tests do not create Android views |
| Debug `android-x64` | yes, 0 warnings | — | yes, KVM, API 34 | — |
| Release Mono AOT `android-x64` | yes | — | yes | — |
| Release Mono AOT `android-arm64` | yes, `libaot-*.so` and `libmonosgen-2.0.so` | — | no image for this ABI | install and startup on a phone or an arm64 image |
| NativeAOT `android-x64` | yes | — | yes | — |
| NativeAOT `android-arm64` | yes, single `libNativePoc.Android.so` | — | no image | same as arm64 Mono |
| `dotnet watch` in-place edit | the command runs | — | the app restarted | an in-place method-body update was not observed |
| Debug screen-assembly reload | yes, 0 warnings | the title-swap test is in the 12 | yes, pid 13248, 10 samples | Release and NativeAOT were not reloaded this way |
| MAUI template comparison | not built | — | — | size and startup gates 15 and 16 |

KVM was present as `/dev/kvm` owned by group 103, and the `ubuntu` user was not in that group. `sudo chmod a+rw /dev/kvm` made it usable. The emulator then reported `CPU Acceleration status: KVM (version 12) is installed and usable` and `sys.boot_completed=1`. The first `adb install` of the Debug APK, without `-t:Run`, aborted in `libmonodroid` with `No assemblies found ... Assuming this is part of Fast Deployment`. That failure is `evidence/debug-launch.png` (the launcher, because the process died) and the log line in the notes below.

## Acceptance checklist

Statuses use the plan's item numbers. "Pass" means the check was observed. A pass on x86_64 is not an arm64-device pass.

| Item | Status | Evidence |
| --- | --- | --- |
| 1. Four widgets, runtime flags | **Pass** (Debug, Mono AOT x64, NativeAOT x64) | Debug UI dump: `Button` text `Save`, content-desc `Save name`, `TextView` `Counter 0`, `EditText`. `evidence/debug-empty-ui.xml`, `evidence/debug-empty.png`. Debug log: `IsDynamicCodeSupported=True IsDynamicCodeCompiled=False` (`evidence/logcat-debug-runtime.log`). Mono AOT x64: `Supported=True Compiled=True` (`evidence/logcat-mono-x64-series.log`). NativeAOT x64: `Supported=False Compiled=False` (`evidence/logcat-nativeaot-x64-series.log`). |
| 2. `Ada` / 20 keystrokes, same `EditText` | **Pass** on Debug and NativeAOT x64. Mono AOT x64 was one typing sequence, not 20 handles | Debug: 20 `field-handle` lines, all `15046`, UI `Hello abcdefghijklmnopqrst`. `evidence/logcat-field-handles.log`. NativeAOT rebuild: 20 lines, all `12006`. `evidence/logcat-nativeaot-handles.log`. Debug pid 6501 typed `Ada` and the dump was `Hello Ada` (`evidence/debug-typed-ui.xml`). Mono AOT pid 7935 typed `Ada` and the dump was `Hello Ada`; that process did not log 20 handles. |
| 3. Save → `Counter 1`, field kept | **Pass** | Debug pid 6501: `clipboard-ok poc-1`, dump `Counter 1` / `Ada` / `Hello Ada`. `evidence/logcat-save.log`, `evidence/debug-saved.png`. Mono AOT pid 7935: same text, `evidence/logcat-mono-x64-save.log`. NativeAOT pid 8254 (the publish before the handle log was added): same text. |
| 4. Empty name, Save still counts | **Pass** | Debug pid 9673: `rendered count=1 name-length=0`, dump `Counter 1` / `Enter a name`. `evidence/logcat-empty-save.log`. NativeAOT pid 12212: `clipboard-ok poc-1`, same dump. `evidence/logcat-nativeaot-empty-save.log`. |
| 5. Recreate and rotation keep state | **Pass** on Debug and Mono AOT; rotation **Pass** on NativeAOT | Debug broadcast `com.scratch.nativepoc.RECREATE`: pid stayed 6501, second `runtime` line, dump still `Counter 1` / `Ada`. Then rotation, pid 6501, same dump. `evidence/logcat-recreate.log`, `evidence/debug-recreate-ui.xml`, `evidence/debug-rotate-ui.xml`. The receiver is `#if DEBUG`. It is absent from the Release APK (`RECREATE` is not in the binary manifest). Mono AOT rotation: pid 7935, new `runtime` line, dump still `Counter 1` / `Ada`. NativeAOT rotation: pid 8254, `rendered count=1 name-length=3`. |
| 6. Clipboard round-trip, no binding project | **Pass** | `PlatformBridge.CopyNonce` uses `ClipboardManager` and throws if the read differs. Log `clipboard-ok poc-1` on Debug, Mono AOT, and NativeAOT. No `ApiDefinition` / binding project in the tree. |
| 7–10. In-place hot reload, n=20, p50 ≤ 1500 ms, pid and text stable | **Pass for the follow-up bar (n=10, same pid, text and counter kept). The plan's n=20 was not repeated. `dotnet watch` still fails.** | See the hot-reload section. p50 of the 10 samples is 18.1 ms. Pid stayed 13248. The field stayed `Ada` and the counter stayed 1. |
| 11. Rude edit | **Recorded, not an in-place update** | Adding `AppState.Extra` made watch launch `DOTNET_WATCH_ITERATION=2`. Pid went 9931 → 10056. The field was reverted. `evidence/dotnet-watch-rude.log`. |
| 12. Release Mono AOT installs and items 1–6 pass | **Pass on android-x64**, with item 2's 20-handle count done on Debug and NativeAOT rather than on this process | `adb install` streamed install, `am start -W` `Status: ok`. Widgets, `Ada` → `Hello Ada`, Save, rotation, clipboard as above. **Not run** on arm64. |
| 13. `libaot-*.so` in the arm64 APK | **Pass** | `lib/arm64-v8a/libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so`, `libmonodroid.so`. No `libNativePoc.Android.so`. `evidence/apk-listings.txt`. APK 3606628 bytes. |
| 14. No trim/AOT warnings in this repo's source | **Pass** | Debug build `0 Warning(s)`. Release Mono logs have no `warning` lines. NativeAOT's only warning is IL3053 on `Mono.Android.dll` (workload). |
| 15. APK smaller than `dotnet new maui` | **Not run** | No MAUI template was built. Own sizes are below. |
| 16. Startup median ≤ MAUI, 10 runs | **Not run** as a comparison | Own `TotalTime` series is below. No MAUI process was started. |
| 17. NativeAOT publish, XA1040 present | **Partial** | `dotnet publish -p:PublishAot=true` exit 0 for `android-arm64` and `android-x64`. **XA1040 does not appear** in `evidence/nativeaot-arm64.log` or `evidence/nativeaot-x64.log`. Workload warning IL3053 does. |
| 18. NativeAOT runs items 1–4 | **Pass on android-x64** | The rebuilt APK: widgets and runtime flags in the startup series, 20 handles all `12006`, empty Save → `Counter 1` / `Enter a name` / `clipboard-ok poc-1` at pid 12212. The previous NativeAOT publish (pid 8254) typed `Ada`, showed `Hello Ada`, and kept it across rotation. **Not run** on arm64. |
| 19. NativeAOT failed | **Does not apply** to the x64 run | The arm64 link failed once, before the NDK was installed. After `ndk;26.3.11579264` the link succeeded. |

## In-place hot reload

`dotnet watch` on SDK 10.0.403 does not update this app in place. Workload `android` 36.1.2 has no `HotReloadWebSockets` capability, does not package the startup hook onto the device, and does not reverse a socket. With `StartupHookSupport` left false, watch logged `Project does not support Hot Reload` and restarted. Setting it true attached `Microsoft.Extensions.DotNetDeltaApplier` to the short-lived `dotnet run` host (`Process id 9775 ran for 2986ms`, then `Disposing agent communication pipe`). The Android process never connected. Two edits restarted the process:

| Edit | Write (UTC) | First render | Delta | Pid | Field |
| --- | --- | --- | --- | --- | --- |
| `Counter ` → `Tally ` | 10:51:41.107 | 10:51:46.566 | 5459 ms | 7177 → 7320 | `Ada` wiped, dump was `Tally 0` / `Enter a name` |
| `Tally ` → `Counter ` | 10:52:32.595 | 10:52:38.097 | 5502 ms | 7320 → 7443 | empty again |

`evidence/dotnet-watch.log`, `evidence/debug-watch-restart-tally.png`.

The path that did stay in place is a Debug-only assembly swap, the same shape as MauiReactor's component reload and React Native Fast Refresh: replace the module, keep the state. `Screen.Render` lives in `NativePoc.Screen`. `AppState` and `AppSession` stay in `NativePoc.Core`. `HotReloadServer` listens on `127.0.0.1:18765` (`#if DEBUG`). The host compiles `Screen.cs` and sends the DLL. The app loads it in a collectible `AssemblyLoadContext`, resolves `NativePoc.Core` to the assembly already in the process, binds `NativePoc.Screen.Render`, and calls the activity's render delegate. The listener is absent from Release: a Release `android-x64` build after the split reported `0 Warning(s)` (`evidence/release-build-after-hotreload.log`) and was not installed.

Clock: host `Stopwatch` starts before the `Screen.cs` write and stops when the app writes the response, which happens after `RenderNow` on the UI thread. The `uiautomator` dump is after that clock. One untimed warmup compile-and-send of the original `Counter ` DLL ran first. The 10 timed samples rewrite `TitlePrefix` to `H0 ` … `H9 `. No sample was dropped.

| Sample | ms | Pid | Reply | UI |
| --- | --- | --- | --- | --- |
| 0 | 129.9 | 13248 | `H0 1 collectible=True` | `H0 1`, `Ada`, `Hello Ada` |
| 1 | 30.1 | 13248 | `H1 1 collectible=True` | `H1 1`, `Ada`, `Hello Ada` |
| 2 | 17.8 | 13248 | `H2 1 collectible=True` | `H2 1`, `Ada`, `Hello Ada` |
| 3 | 18.7 | 13248 | `H3 1 collectible=True` | `H3 1`, `Ada`, `Hello Ada` |
| 4 | 16.0 | 13248 | `H4 1 collectible=True` | `H4 1`, `Ada`, `Hello Ada` |
| 5 | 18.4 | 13248 | `H5 1 collectible=True` | `H5 1`, `Ada`, `Hello Ada` |
| 6 | 15.3 | 13248 | `H6 1 collectible=True` | `H6 1`, `Ada`, `Hello Ada` |
| 7 | 14.6 | 13248 | `H7 1 collectible=True` | `H7 1`, `Ada`, `Hello Ada` |
| 8 | 13.8 | 13248 | `H8 1 collectible=True` | `H8 1`, `Ada`, `Hello Ada` |
| 9 | 21.1 | 13248 | `H9 1 collectible=True` | `H9 1`, `Ada`, `Hello Ada` |

Sorted: 13.8, 14.6, 15.3, 16.0, 17.8, 18.4, 18.7, 21.1, 30.1, 129.9. p50 is the mean of the 5th and 6th, **18.1 ms**. Min 13.8 ms, max 129.9 ms. The counter stayed 1, so Save was not run again. `evidence/hot-reload-inplace.log`, `evidence/logcat-hotreload.log` (every `hotreload-applied` line is pid 13248, `name-length=3`), `evidence/hot-reload-h0.png`, `evidence/hot-reload-h9.png`. Logcat also shows the setup keystrokes on the same pid: `field-handle 15014` three times, then `clipboard-ok poc-1`, then `title=Counter 1`, then the ten titles. The process that logged `hotreload-listen 18765` is the same pid.

## Reconciler review

Checked the patch application against the in-memory host. No case was found where a patch left a mounted view that the new tree does not contain, or dropped a sibling that the new tree still contains. `UnmountDescendants` removes descendant entries and does not call `Remove` on them; `Detach` removes the ancestor, and on Android that view takes its children with it. `Replacing_a_nested_column_drops_its_descendants_and_keeps_the_sibling` locks that: after a nested `Column` becomes a `Text`, the grandchild path throws and the sibling identity is unchanged. `A_title_only_render_keeps_the_field_the_button_and_the_state` is the hot-reload shape: a new tree with a different counter prefix keeps the field, button, and greeting identities and leaves `AppState` at `Ada` / count 1. Button and text-field listeners are attached once and read the reconciler's current tree, so a later render's callbacks are the ones that run when the widget itself is not replaced. Identity is the sibling index. That is the design, and inserting at the front still rewrites the shared prefix.

## Measurements

Host unit tests before the screen split: `Passed: 10, Failed: 0, Skipped: 0, Duration: 53 ms`. `evidence/unit-tests.log`. After the split and the two added tests: `Passed: 12, Failed: 0, Skipped: 0, Duration: 52 ms`. `evidence/unit-tests-hotreload.log`.

Signed APK bytes (`stat`):

| APK | Bytes | Native contents that identify the runtime |
| --- | --- | --- |
| Mono AOT `android-arm64` | 3606628 | `libmonosgen-2.0.so` 3123640 uncompressed, `libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so` |
| Mono AOT `android-x64` | 3774498 | `libmonosgen-2.0.so` 3290280 uncompressed, `libaot-*.so` |
| NativeAOT `android-arm64` | 2187616 | only `lib/arm64-v8a/libNativePoc.Android.so` (5330832 uncompressed) and `classes.dex` |
| NativeAOT `android-x64` | 2294109 | only `lib/x86_64/libNativePoc.Android.so` (5161568 uncompressed) and `classes.dex` |

`am start -W` `TotalTime`, force-stop between runs, first run discarded. One device: `emulator-5554`, API 34, x86_64, KVM.

Mono AOT x64, clean rebuild that logs `activity-start`: first 1668; next 10: 1656, 1789, 1842, 1869, 1617, 1591, 1806, 1421, 1723, 1628. Median of those 10: **1689.5 ms**. Min 1421, max 1869. `evidence/am-start-mono-x64-series.log`.

Managed span, `button-mounted tick` minus `activity-start tick`, same 11 runs (milliseconds): 108, 144, 44, 60, 36, 60, 68, 104, 36, 36, 80. Median of the last 10: **60 ms**.

NativeAOT x64: first 1682; next 10: 1855, 2069, 1794, 1777, 1600, 1732, 1916, 1838, 1618, 1850. Median **1816 ms**. Min 1600, max 2069. `evidence/am-start-nativeaot-x64-series.log`.

Button span, same 11 runs: 188, 148, 564, 188, 28, 32, 112, 36, 100, 116, 140. Median of the last 10: **114 ms**.

One Debug fast-deployment cold start, not a series: `TotalTime: 1803`, `WaitTime: 1818`, `LaunchState: COLD`. `evidence/am-start-debug-fastdev.log`.

Runtime flags actually logged:

| Build | IsDynamicCodeSupported | IsDynamicCodeCompiled |
| --- | --- | --- |
| Debug | True | False |
| Release Mono AOT x64 | True | True |
| NativeAOT x64 | False | False |

Release Mono AOT still reports a JIT (`IsDynamicCodeCompiled=True`) on this workload. That is the risk the plan called out: a Release Android run can hide code that fails a full-AOT or NativeAOT runtime. NativeAOT on this emulator did not have that JIT, and the screen still ran.

## Warnings

Own projects: `TreatWarningsAsErrors` is true. The Debug deploy and the Release `android-x64` build after the screen split both reported `0 Warning(s)`. `IsAotCompatible` is set. `HotReloadServer.cs` is `#if DEBUG` and does use reflection to bind `Screen.Render`. The IL2026/IL2070/IL2072/IL2075 suppressions on that method are unconditional and scoped to that Debug loader. Release does not compile the method. The host tool, not the app, calls Roslyn `Emit`. There is no source generator in this repo.

Workload, NativeAOT only:

- `warning IL3053: Assembly 'Mono.Android' produced AOT analysis warnings` on the linked `Mono.Android.dll`.
- Earlier verbose ILC output, from the attempt that then failed to link: `Method '...monodroid_unhandled_exception...' will always throw` and the same for `monodroid_debugger_unhandled_exception`, reason `Invalid IL or CLR metadata`. `evidence/nativeaot-arm64-nowarnserror.log`. The later successful publish was quiet and did not reprint those lines. The x64 process still reached `rendered`.

XA1040 was not emitted. `WarningsNotAsErrors` includes it so a future emission would not fail the build. It was not needed for the publishes that succeeded.

## Blockers

- `dotnet watch` still does not reach the Android process on workload 36.1.2. The in-place path that was measured is the Debug assembly swap, not MetadataUpdater deltas and not `dotnet watch`. A rude edit that changes `AppState` was not retried on that path. The plan's n=20 series was not repeated.
- Items 15 and 16 have no MAUI baseline. `dotnet new maui` was not built or installed.
- arm64 Mono AOT and arm64 NativeAOT were built and not installed. This host's emulator image is x86_64.
- NativeAOT is experimental. IL3053 is in the workload assembly. XA1040 did not show up, which disagrees with the plan's expectation that the warning is present. The x64 app ran anyway.
- The first NativeAOT link failed because the NDK was not installed (`libc++_static.a` and `libc++abi.a` missing). Installing `ndk;26.3.11579264` fixed the link. That failure is environmental, and it is fixed on this machine.
- Debug cannot be installed with `adb install` alone. Fast deployment aborts if the assemblies were not pushed.
- iOS is out of scope and was not attempted.

## Screenshots

- `evidence/debug-empty.png` — Debug, `Counter 0`, hint `Name`, `Save`, `Enter a name`
- `evidence/debug-saved.png` — Debug after typing `Ada` and Save
- `evidence/debug-watch-restart-tally.png` — after a watch restart, `Tally 0`, field cleared
- `evidence/hot-reload-h0.png` — in-place reload, pid 13248, `H0 1`, field `Ada`, `Hello Ada`
- `evidence/hot-reload-h9.png` — same process, `H9 1`, field still `Ada`
- `evidence/mono-aot-x64.png` and `evidence/mono-aot-x64-saved.png`
- `evidence/nativeaot-x64.png` and `evidence/nativeaot-x64-saved.png`
- `evidence/debug-launch.png` — home screen after the fast-deployment abort, not the app
