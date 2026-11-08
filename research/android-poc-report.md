# Android proof of concept

Measured on this machine on 2026-11-08. Nothing here is a guessed latency, a guessed size, or a screenshot that was not captured. iOS was not built and was not run.

The plan's "fully working" bar is items 1–16 in [runtime-feasibility.md](runtime-feasibility.md). Items 15 and 16 are still unmet: no MAUI baseline was built. Debug in-place reload of the screen assembly works. `dotnet watch` still restarts the process. The screen works on Debug, on Release Mono AOT, and on experimental NativeAOT, on an x86_64 emulator.

Clone URL: `github.com/filipef101/c-sharp-native`

## Verification of 464b63e

App sources for the Release rebuilds match `464b63ed71629aee53dfc24a00acc2ff74c58f06`. `git diff 464b63e -- src tests` is empty. The measurement host under `tools/NativePoc.HotReload.Host/Program.cs` changed after that commit and is not in either APK.

| | BUILT | TESTED (unit) | RUN ON EMULATOR | UNVERIFIED |
| --- | --- | --- | --- | --- |
| Release Mono AOT `android-x64` from that commit | publish exit 0, log has no `warning` line, APK 3778681 bytes | — | functional checks and 10 cold starts | arm64 was not reinstalled |
| NativeAOT `android-x64`, experimental, separate publish | publish exit 0, IL3053, XA1040 absent, APK 2294109 bytes | — | the same checks, its own process | arm64 was not reinstalled |
| Debug clocks (a)(b)(c), n=20 | host build 0 warnings | — | pid stable, `Ada` and counter 1 kept | Release has no listener, so these clocks were not run there |
| Edit classes | the failing `Badge` edit failed in the host compiler | — | the device trials below | a Core shape change was not installed as a new process |

### Release Mono AOT x64

Clean `bin/Release` and `obj/Release`, then `dotnet publish -c Release -f net10.0-android -r android-x64`. Log: `evidence/mono-aot-x64-rebuild.log`. Installed `evidence/apk/mono-aot-x64-rebuild.apk` (3778681 bytes). It contains `libaot-NativePoc.Screen.dll.so` (5168 uncompressed). Activity `crc64ee53024f7ed0aa60.MainActivity`.

Pid 13736. `IsDynamicCodeSupported=True IsDynamicCodeCompiled=True`. Empty UI: `Counter 0`, hint `Name`, `Save`, `Enter a name`. Typing `Ada` logged `field-handle 14758` three times and showed `Hello Ada`. Save logged `clipboard-ok poc-1` and showed `Counter 1` / `Ada` / `Hello Ada`. Rotation and the return to portrait each logged a new `activity-start` on pid 13736, with the same three strings. The rotated activity's field handle was 16870, then 17638 after rotating back: the views were recreated and `AppSession` was kept. `evidence/logcat-mono-aot-x64-rebuild.log`.

`am start -W` for the 10 cold starts, force-stop between them, every `LaunchState: COLD`, no run dropped. `TotalTime`: 1488, 1124, 1449, 1068, 1152, 1331, 1382, 1551, 1513, 1414. Sorted: 1068, 1124, 1152, 1331, 1382, 1414, 1449, 1488, 1513, 1551. p50 is the mean of the 5th and 6th, **1398 ms**. Min 1068, max 1551. The functional launch before that series was also `COLD` at 1433 ms and is not in the ten. `evidence/am-start-mono-aot-x64-rebuild.log`.

### NativeAOT x64

Separate clean publish with `-p:PublishAot=true -p:EnablePreviewFeatures=true -p:TreatWarningsAsErrors=false`. Log: `evidence/nativeaot-x64-rebuild.log`. Workload warning IL3053. ILC repeated the `monodroid_unhandled_exception` / `monodroid_debugger_unhandled_exception` "will always throw" notes. XA1040 is absent. APK `evidence/apk/nativeaot-x64-rebuild.apk`, 2294109 bytes, sha256 `00b0bd4ab81bca73f16fbb1764710d6e999a34a866bfe2d28e5bc4284ce8153a`. The pre-split NativeAOT x64 APK hashes to `919087af22ee67ce63e01ed4cbcb9b12ca85a3c3e6df4cb0e4795732fa7ec5ab`. Uncompressed `lib/x86_64/libNativePoc.Android.so` is 5164368 bytes and contains the ASCII name `NativePoc.Screen` and the UTF-16 string `activity-start`. The manifest provider is `net.dot.jni.nativeaot.NativeAotRuntimeProvider`.

Pid 14459. `IsDynamicCodeSupported=False IsDynamicCodeCompiled=False`. Empty UI: `Counter 0`, `Name`, `Save`, `Enter a name`. `Ada` logged `field-handle 12006` three times, then `Hello Ada`. Save logged `clipboard-ok poc-1`, UI `Counter 1` / `Ada` / `Hello Ada`. Rotation kept those strings on pid 14459. `evidence/logcat-nativeaot-x64-rebuild.log`. That log has no `hotreload` line.

Ten cold starts, all `LaunchState: COLD`, none dropped. `TotalTime`: 1475, 1254, 1342, 1411, 1088, 1246, 1204, 1348, 1251, 1368. Sorted: 1088, 1204, 1246, 1251, 1254, 1342, 1348, 1368, 1411, 1475. p50 **1298 ms**. Min 1088, max 1475. The functional launch before the ten was `COLD` at 1140 ms and is not in the ten. `evidence/am-start-nativeaot-x64-rebuild.log`.

### What the earlier 18.1 ms covers

That figure is the n=10 series on pid 13248 in `evidence/hot-reload-inplace.log`. The host `Stopwatch` started before `File.WriteAllText` of `Screen.cs` and stopped when the TCP reply had been read. The app writes that reply after `RenderNow` returns, so the interval contains the file write, the Roslyn compile, the socket transfer, the collectible `AssemblyLoadContext` load, and the render. The `uiautomator` dump ran after the clock. An untimed warmup compile-and-send ran before sample 0, so sample 0 was not the first load after process start. Sorted samples 13.8, 14.6, 15.3, 16.0, 17.8, 18.4, 18.7, 21.1, 30.1, 129.9. p50, the mean of the 5th and 6th, is 18.1 ms.

### n=20 clocks, measured again

Debug deploy of the same commit, interpreter (`IsDynamicCodeCompiled=False`). Each series restarts the process, types `Ada`, presses Save, and then takes sample 0 with no earlier compile and no earlier send in that process. Sample 0 is the cold sample. Samples 1–19 are warm. The UI dump is outside every clock. Every sample kept the pid, `Ada`, `Hello Ada`, and counter 1. `collectible=True` on every apply.

**(a) file save, Roslyn compile, transfer, load, render.** Clock starts before the write and stops when the post-render reply is read. Pid 15262. Milliseconds: 758.4, 52.8, 24.2, 23.6, 22.0, 23.7, 18.2, 16.7, 17.0, 17.1, 15.2, 17.4, 18.5, 15.4, 14.5, 17.6, 16.1, 16.7, 24.7, 16.4. Cold 758.4. Sorted center pair 17.4 and 17.6, so p50 of all 20 is **17.5 ms**. p50 of the 19 warm samples is **17.4 ms**. Min 14.5, max 758.4. `evidence/hot-reload-a.log`, `evidence/logcat-hotreload-a.log`.

**(b) send, load, and render of a DLL compiled before the clock.** The 20 DLLs were compiled, then the process was restarted, then each sample timed only `Send` through the post-render reply. The file write in this mode sits before the clock. Pid 15856. Milliseconds: 99.6, 47.8, 8.3, 8.4, 10.2, 5.6, 6.7, 5.8, 5.8, 5.6, 5.5, 5.4, 5.5, 5.8, 5.7, 5.8, 5.6, 5.9, 13.5, 6.3. Cold 99.6. p50 of all 20 is **5.8 ms**. p50 of the warm 19 is **5.8 ms**. Min 5.4, max 99.6. `evidence/hot-reload-b.log`, `evidence/logcat-hotreload-b.log`.

**(c) compile only.** In-memory Roslyn emit, no file write, no socket, first compilation in that host process. Milliseconds: 406.5, 10.5, 8.4, 7.4, 7.0, 6.1, 6.8, 6.6, 6.9, 7.0, 7.4, 7.5, 6.7, 6.1, 6.5, 6.6, 6.4, 6.6, 6.7, 6.7. Cold 406.5. Sorted center pair 6.7 and 6.8, so p50 of all 20 is **6.75 ms**. The tool's one-decimal line prints `p50-all-ms=6.7`. p50 of the warm 19 is **6.7 ms**. Min 6.1, max 406.5. `evidence/hot-reload-c.log`.

### Edit classes

Tried on pid 15856 after series (b), still `Ada` and count 1. Log: `evidence/hot-reload-edits.log`.

| Edit | Result |
| --- | --- |
| Render body that still emits `Column`, `Text`, `TextField`, and `Button`. Button title `Save` changed to `Store`. A new `Text` appended. A new non-element helper type `Marker` in the screen assembly. | Applied. UI `Counter 1`, `Ada`, `Store`, `Hello Ada`, `Extra line`. Pid stayed 15856. The button handle stayed 15302. |
| Screen rebuilt against a Core DLL that differs by a comment, public shape unchanged, title set to `Rebound `. | Applied. UI `Rebound 1`, `Ada`, `Save`, `Hello Ada`. Pid stayed 15856. The running process kept the Core it started with; the host resolved `NativePoc.Core` by simple name. |
| `public sealed record Badge(string Value) : Element` in the screen assembly. | Host compile failed: `CS0122: 'Element.Element()' is inaccessible due to its protection level`. Nothing was sent. |
| `AppState.Extra` added, Core rebuilt, Screen read `state.Extra`. | The device refused the DLL. Log: `hotreload-error MissingMethodException Method not found: int NativePoc.AppState.get_Extra()`. Pid stayed 15856. The screen stayed on `Rebound 1` / `Ada` / `Hello Ada`. Sending the original Screen afterward restored `Counter 1` / `Ada` / `Hello Ada` on the same pid. |

A Core edit is not pushed by this loader. The listener accepts one `NativePoc.Screen` DLL. A new `Element` subtype cannot be declared outside `NativePoc.Core`. A new member on `AppState` fails at apply time and leaves the previous screen and the previous state in place.

### Loader is Debug-only and loopback-only

`src/NativePoc.Android/HotReloadServer.cs` is wrapped in `#if DEBUG` from the first line through the end of the file. `MainActivity.OnCreate` calls `HotReloadServer.Start()` inside `#if DEBUG` (`MainActivity.cs` lines 40–41). `HotReload.cs` and `RecreateReceiver.cs` are also `#if DEBUG`. The socket is `new TcpListener(IPAddress.Loopback, 18765)`.

On the running Debug process, `/proc/net/tcp` shows `0100007F:494D` in state `0A` (LISTEN). `0x494D` is 18765 and `0100007F` is 127.0.0.1. `/proc/net/tcp6` has no 18765 listener.

The Release `NativePoc.Android.dll` from the Mono AOT build (11264 bytes) has ASCII strings `NativePoc`, `NativePoc.Screen`, and the assembly attributes. It has no `HotReload`, `HotReloadServer`, `TcpListener`, `LoadFromStream`, or `AssemblyLoadContext`. The Debug DLL from the same tree has `HotReloadServer`, `TcpListener`, `LoadFromStream`, `AssemblyLoadContext`, and UTF-16 `hotreload-listen`. Scan: `evidence/mono-aot-x64-loader-scan.txt`.

Inside the Mono AOT APK, `hotreload-listen`, `HotReloadServer`, `18765`, and `nativepoc-screen` are absent from every entry, including `libassemblies.x86_64.blob.so` and `libaot-NativePoc.Android.dll.so`. `libmonosgen-2.0.so` and `libmonodroid.so` contain the runtime type name `AssemblyLoadContext`. That string is the Mono runtime, and the same scan shows it is absent from `NativePoc.Android.dll`. `aapt dump permissions` on the APK prints `android.permission.INTERNET`. The merged manifest has that permission and the main activity, and it has no `RECREATE` receiver. `evidence/mono-aot-x64-AndroidManifest.xml`.

The NativeAOT `.so` has no `hotreload-listen`, `HotReloadServer`, or `18765`. Its manifest also declares `INTERNET` and names `NativeAotRuntimeProvider`. `evidence/nativeaot-x64-AndroidManifest.xml`.

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
dotnet build tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
dotnet tools/NativePoc.HotReload.Host/bin/Debug/net10.0/NativePoc.HotReload.Host.dll a
```

`a` times file write, compile, transfer, and render. `b` times send and render of a DLL already compiled. `c` times compile only.

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
| 2. `Ada` / 20 keystrokes, same `EditText` | **Pass** on the pre-split Debug and NativeAOT 20-keystroke runs. The 464b63e Mono AOT and NativeAOT runs typed `Ada` (three keystrokes, one handle each) and did not repeat 20 keystrokes | Debug: 20 `field-handle` lines, all `15046`. `evidence/logcat-field-handles.log`. Pre-split NativeAOT: 20 lines, all `12006`. `evidence/logcat-nativeaot-handles.log`. On 464b63e, Mono AOT pid 13736 and NativeAOT pid 14459 each kept one handle across `A`, `d`, and `a` (`14758` and `12006`). |
| 3. Save → `Counter 1`, field kept | **Pass** | Debug pid 6501: `clipboard-ok poc-1`, dump `Counter 1` / `Ada` / `Hello Ada`. `evidence/logcat-save.log`, `evidence/debug-saved.png`. Mono AOT pid 7935: same text, `evidence/logcat-mono-x64-save.log`. NativeAOT pid 8254 (the publish before the handle log was added): same text. |
| 4. Empty name, Save still counts | **Pass** | Debug pid 9673: `rendered count=1 name-length=0`, dump `Counter 1` / `Enter a name`. `evidence/logcat-empty-save.log`. NativeAOT pid 12212: `clipboard-ok poc-1`, same dump. `evidence/logcat-nativeaot-empty-save.log`. |
| 5. Recreate and rotation keep state | **Pass** on Debug and Mono AOT; rotation **Pass** on NativeAOT | Debug broadcast `com.scratch.nativepoc.RECREATE`: pid stayed 6501, second `runtime` line, dump still `Counter 1` / `Ada`. Then rotation, pid 6501, same dump. `evidence/logcat-recreate.log`, `evidence/debug-recreate-ui.xml`, `evidence/debug-rotate-ui.xml`. The receiver is `#if DEBUG`. It is absent from the Release APK (`RECREATE` is not in the binary manifest). Mono AOT rotation: pid 7935, new `runtime` line, dump still `Counter 1` / `Ada`. NativeAOT rotation: pid 8254, `rendered count=1 name-length=3`. |
| 6. Clipboard round-trip, no binding project | **Pass** | `PlatformBridge.CopyNonce` uses `ClipboardManager` and throws if the read differs. Log `clipboard-ok poc-1` on Debug, Mono AOT, and NativeAOT. No `ApiDefinition` / binding project in the tree. |
| 7–10. In-place hot reload, n=20, p50 ≤ 1500 ms, pid and text stable | **Pass on the n=20 end-to-end series. `dotnet watch` still restarts.** | Series (a): pid 15262, p50 17.5 ms, cold 758.4 ms, `Ada` and counter 1 kept. The earlier 18.1 ms is the n=10 combined clock described above. |
| 11. Rude edit | **Watch restarts. The in-place loader refuses a shape change and keeps the process.** | `dotnet watch` on `AppState.Extra`: pid 9931 → 10056, field reverted (`evidence/dotnet-watch-rude.log`). The same shape change sent as a screen DLL on pid 15856 returned `MissingMethodException` for `AppState.get_Extra()` and left `Rebound 1` / `Ada` on screen. |
| 12. Release Mono AOT installs and items 1–6 pass | **Pass on android-x64** for the 464b63e rebuild. The 20-handle count was not repeated on this process | Pid 13736: widgets, `Ada` with handle 14758 across three keystrokes, Save, rotation, clipboard. Ten cold starts, p50 1398 ms. **Not run** on arm64. |
| 13. `libaot-*.so` in the arm64 APK | **Pass** | `lib/arm64-v8a/libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so`, `libmonodroid.so`. No `libNativePoc.Android.so`. `evidence/apk-listings.txt`. APK 3606628 bytes. |
| 14. No trim/AOT warnings in this repo's source | **Pass** | Debug build `0 Warning(s)`. Release Mono logs have no `warning` lines. NativeAOT's only warning is IL3053 on `Mono.Android.dll` (workload). |
| 15. APK smaller than `dotnet new maui` | **Not run** | No MAUI template was built. Own sizes are below. |
| 16. Startup median ≤ MAUI, 10 runs | **Not run** as a comparison | Own `TotalTime` series is below. No MAUI process was started. |
| 17. NativeAOT publish, XA1040 present | **Partial** | `dotnet publish -p:PublishAot=true` exit 0 for `android-arm64` and `android-x64`. **XA1040 does not appear** in `evidence/nativeaot-arm64.log` or `evidence/nativeaot-x64.log`. Workload warning IL3053 does. |
| 18. NativeAOT runs items 1–4 | **Pass on android-x64** for the 464b63e rebuild | Pid 14459: `Supported=False Compiled=False`, widgets, `Ada` with handle 12006 across three keystrokes, Save `clipboard-ok poc-1`, rotation kept `Counter 1` / `Ada`. Ten cold starts, p50 1298 ms. The earlier pid 12212 empty-save and the 20-handle series were the pre-split publish. **Not run** on arm64. |
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

Sorted: 13.8, 14.6, 15.3, 16.0, 17.8, 18.4, 18.7, 21.1, 30.1, 129.9. p50 is the mean of the 5th and 6th, **18.1 ms**. Min 13.8 ms, max 129.9 ms. The counter stayed 1, so Save was not run again. `evidence/hot-reload-inplace.log`, `evidence/logcat-hotreload.log` (every `hotreload-applied` line is pid 13248, `name-length=3`), `evidence/hot-reload-h0.png`, `evidence/hot-reload-h9.png`. Logcat also shows the setup keystrokes on the same pid: `field-handle 15014` three times, then `clipboard-ok poc-1`, then `title=Counter 1`, then the ten titles. The process that logged `hotreload-listen 18765` is the same pid. What that 18.1 ms includes, and the later n=20 split into file-to-render, transfer-only, and compile-only, is in the verification section above.

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

- `dotnet watch` still does not reach the Android process on workload 36.1.2. The in-place path is the Debug screen-assembly swap. An `AppState` shape change fails in place with `MissingMethodException` and leaves the previous screen up. The Release APKs declare `INTERNET` and contain no listener.
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
- `evidence/mono-aot-x64-rebuild-empty.png` — Mono AOT rebuild, pid 13736, `Counter 0`
- `evidence/mono-aot-x64-rebuild-saved.png` — same process, `Counter 1`, `Ada`, `Hello Ada`
- `evidence/mono-aot-x64-rebuild-rotated.png` — same process after rotation, state kept
- `evidence/nativeaot-x64-rebuild-empty.png` — NativeAOT rebuild, pid 14459, `Counter 0`
- `evidence/nativeaot-x64-rebuild-saved.png` — same process, `Counter 1`, `Ada`, `Hello Ada`
- `evidence/nativeaot-x64-rebuild-rotated.png` — same process after rotation, state kept
- `evidence/hot-reload-a-cold.png` — end-to-end sample 0, pid 15262, `A00 1`, `Ada`
- `evidence/hot-reload-a-warm.png` — end-to-end sample 19, same process, `A19 1`, `Ada`
- `evidence/hot-reload-extra-line.png` — pid 15856, button `Store`, text `Extra line`, `Ada` kept
- `evidence/hot-reload-shape.png` — same process after the rejected `AppState.Extra` edit, still `Rebound 1` / `Ada`
- `evidence/mono-aot-x64.png` and `evidence/mono-aot-x64-saved.png`
- `evidence/nativeaot-x64.png` and `evidence/nativeaot-x64-saved.png`
- `evidence/debug-launch.png` — home screen after the fast-deployment abort, not the app
