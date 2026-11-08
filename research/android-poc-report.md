# Android proof of concept

Measured on this machine on 2026-11-08. Nothing here is a guessed latency, a guessed size, or a screenshot that was not captured. iOS was not built and was not run.

The plan's "fully working" bar is items 1–16 in [runtime-feasibility.md](runtime-feasibility.md). That bar is **not met**. The screen works on Debug, on Release Mono AOT, and on experimental NativeAOT, on an x86_64 emulator. In-place `dotnet watch` hot reload does not. The MAUI size and startup comparison was not built.

## File tree

```
global.json
NativePoc.slnx
README.md
research/runtime-feasibility.md
research/android-poc-report.md
src/NativePoc.Core/
  NativePoc.Core.csproj
  Elements.cs          Column, Text, Button, TextField, Screen.Render, AppSession
  Diff.cs              NodePath, patches, TreeDiff
  ViewReconciler.cs    IElementHost<TView>, applies the patch list
src/NativePoc.Android/
  NativePoc.Android.csproj
  AndroidManifest.xml
  MainActivity.cs      renders Screen, logs RuntimeFeature
  AndroidHost.cs       LinearLayout, TextView, Button, EditText, ClipboardManager
  HotReload.cs         DEBUG MetadataUpdateHandler, calls RequestRender
  RecreateReceiver.cs  DEBUG broadcast com.scratch.nativepoc.RECREATE
  Resources/values/strings.xml
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
| Reconciler and session state | yes | 10/10 on the host | the screen was exercised separately | unit tests do not create Android views |
| Debug `android-x64` | yes, 0 warnings | — | yes, KVM, API 34 | — |
| Release Mono AOT `android-x64` | yes | — | yes | — |
| Release Mono AOT `android-arm64` | yes, `libaot-*.so` and `libmonosgen-2.0.so` | — | no image for this ABI | install and startup on a phone or an arm64 image |
| NativeAOT `android-x64` | yes | — | yes | — |
| NativeAOT `android-arm64` | yes, single `libNativePoc.Android.so` | — | no image | same as arm64 Mono |
| `dotnet watch` in-place edit | the command runs | — | the app restarted | an in-place method-body update was not observed |
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
| 7–10. In-place hot reload, n=20, p50 ≤ 1500 ms, pid and text stable | **Fail** | `dotnet watch` logged `Project does not support Hot Reload: 'StartupHookSupport' property is 'False'. Application will be restarted`. Setting `StartupHookSupport=true` made watch wait on a named pipe injected into `dotnet run` via `DOTNET_STARTUP_HOOKS`. That process exited in about 3 seconds (`Process id 9775 ran for 2986ms`) and watch logged `Disposing agent communication pipe`. The Android process never connected. Two method-body edits (`Counter ` → `Tally `, then back) restarted the process. |
| 11. Rude edit | **Recorded, not an in-place update** | Adding `AppState.Extra` made watch launch `DOTNET_WATCH_ITERATION=2`. Pid went 9931 → 10056. The field was reverted. `evidence/dotnet-watch-rude.log`. |
| 12. Release Mono AOT installs and items 1–6 pass | **Pass on android-x64**, with item 2's 20-handle count done on Debug and NativeAOT rather than on this process | `adb install` streamed install, `am start -W` `Status: ok`. Widgets, `Ada` → `Hello Ada`, Save, rotation, clipboard as above. **Not run** on arm64. |
| 13. `libaot-*.so` in the arm64 APK | **Pass** | `lib/arm64-v8a/libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so`, `libmonodroid.so`. No `libNativePoc.Android.so`. `evidence/apk-listings.txt`. APK 3606628 bytes. |
| 14. No trim/AOT warnings in this repo's source | **Pass** | Debug build `0 Warning(s)`. Release Mono logs have no `warning` lines. NativeAOT's only warning is IL3053 on `Mono.Android.dll` (workload). |
| 15. APK smaller than `dotnet new maui` | **Not run** | No MAUI template was built. Own sizes are below. |
| 16. Startup median ≤ MAUI, 10 runs | **Not run** as a comparison | Own `TotalTime` series is below. No MAUI process was started. |
| 17. NativeAOT publish, XA1040 present | **Partial** | `dotnet publish -p:PublishAot=true` exit 0 for `android-arm64` and `android-x64`. **XA1040 does not appear** in `evidence/nativeaot-arm64.log` or `evidence/nativeaot-x64.log`. Workload warning IL3053 does. |
| 18. NativeAOT runs items 1–4 | **Pass on android-x64** | The rebuilt APK: widgets and runtime flags in the startup series, 20 handles all `12006`, empty Save → `Counter 1` / `Enter a name` / `clipboard-ok poc-1` at pid 12212. The previous NativeAOT publish (pid 8254) typed `Ada`, showed `Hello Ada`, and kept it across rotation. **Not run** on arm64. |
| 19. NativeAOT failed | **Does not apply** to the x64 run | The arm64 link failed once, before the NDK was installed. After `ndk;26.3.11579264` the link succeeded. |

Hot-reload samples, both process restarts, not in-place updates. File mtime to the new process's first `rendered` line:

| Edit | Write (UTC) | First render | Delta | Pid | Field |
| --- | --- | --- | --- | --- | --- |
| `Counter ` → `Tally ` | 10:51:41.107 | 10:51:46.566 | 5459 ms | 7177 → 7320 | `Ada` wiped, dump was `Tally 0` / `Enter a name` |
| `Tally ` → `Counter ` | 10:52:32.595 | 10:52:38.097 | 5502 ms | 7320 → 7443 | empty again |

n=2. No p50 of an in-place update exists. The 1500 ms target was not measured as a hot-reload success. `evidence/debug-watch-restart-tally.png` is the restarted `Tally 0` screen. `evidence/dotnet-watch.log`.

## Measurements

Host unit tests, after the final source: `Passed: 10, Failed: 0, Skipped: 0, Duration: 53 ms`. `evidence/unit-tests.log`.

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

Own projects: `TreatWarningsAsErrors` is true. Debug and Release Mono publishes reported 0 warnings. `IsAotCompatible` is set. A search of `src/` for `System.Reflection` hits only `System.Reflection.Metadata` in `HotReload.cs`, which is the `MetadataUpdateHandler` attribute. There is no `Assembly.Load`, no `Emit`, and no source generator in this repo.

Workload, NativeAOT only:

- `warning IL3053: Assembly 'Mono.Android' produced AOT analysis warnings` on the linked `Mono.Android.dll`.
- Earlier verbose ILC output, from the attempt that then failed to link: `Method '...monodroid_unhandled_exception...' will always throw` and the same for `monodroid_debugger_unhandled_exception`, reason `Invalid IL or CLR metadata`. `evidence/nativeaot-arm64-nowarnserror.log`. The later successful publish was quiet and did not reprint those lines. The x64 process still reached `rendered`.

XA1040 was not emitted. `WarningsNotAsErrors` includes it so a future emission would not fail the build. It was not needed for the publishes that succeeded.

## Blockers

- In-place method-body hot reload via `dotnet watch` does not reach the Android process. The SDK defaults `StartupHookSupport` to false. Turning it on attaches the delta applier to the short-lived `dotnet run` host, which exits and disposes the pipe. Edits restart the app, change the pid, and drop `AppSession` state. Items 7–10 fail.
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
- `evidence/mono-aot-x64.png` and `evidence/mono-aot-x64-saved.png`
- `evidence/nativeaot-x64.png` and `evidence/nativeaot-x64-saved.png`
- `evidence/debug-launch.png` — home screen after the fast-deployment abort, not the app
