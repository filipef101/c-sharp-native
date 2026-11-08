# Android proof of concept

Measured on this machine on 2026-11-08. Nothing here is a guessed latency, a guessed size, or a screenshot that was not captured. iOS was not built and was not run.

The plan's "fully working" bar is items 1–16 in [runtime-feasibility.md](runtime-feasibility.md). Item 15 was measured after the review fixes: the Release Mono AOT x64 APK is smaller than `dotnet new maui` on the same emulator. Item 16 was measured and was not met: this app's cold-start median is higher than that MAUI app. Debug in-place reload of the screen assembly works. `dotnet watch` still restarts the process. The screen works on Debug and on Release Mono AOT, on an x86_64 emulator. The NativeAOT x64 process in the 464b63e section was not started again after these fixes. A new NativeAOT publish was built and not installed.

Installing the `maui-android` workload upgraded `android` from 36.1.2 to 36.1.118. `dotnet workload install maui` is rejected on this SDK with `Workload ID maui isn't supported on this platform`. The template comes from `maui-android` 10.0.110. Numbers under "Verification of 464b63e" and the older tables were taken on 36.1.2. Numbers under "Review fixes" were taken on 36.1.118. `evidence/workload-list-review.txt`.

Clone URL: `github.com/filipef101/c-sharp-native`

## Review fixes

The independent review's numbered findings, on the tree after those fixes. Unit tests: `Passed: 15, Failed: 0, Skipped: 0, Duration: 66 ms`. `evidence/unit-tests-review.log`. The three new tests keep view identity for a middle keyed delete, a keyed reorder, and a leading keyed field delete.

### Loader, receiver, and one listener

Debug only, `#if DEBUG`. The device does not open a TCP port. `Start` reads the `nativepoc_hotreload` intent extra. Without that extra it does not listen, so a launcher start is silent. The extra is 32 hex characters. The process listens on an abstract local socket named `npoc-` plus that hex, checks the first 16 bytes of each connection with `CryptographicOperations.FixedTimeEquals`, and only then reads the DLL. `Resolve` returns the already loaded `NativePoc.Core` assembly, or a default-context assembly whose simple name is `System.Private.CoreLib`, `netstandard`, `mscorlib`, or starts with `System.`. It does not return `Mono.Android`, `Java.Interop`, `NativePoc.Android`, or `NativePoc.Screen`. `Start` uses `Interlocked.CompareExchange`. A second call logs `hotreload-listen already` and does not bind again.

`RecreateReceiver` is `Exported = false` and declares `android.permission.DUMP`. On pid 5119, `adb shell am broadcast -a com.scratch.nativepoc.RECREATE -n com.scratch.nativepoc/crc64ee53024f7ed0aa60.RecreateReceiver` printed `Broadcast completed: result=0`. The pid stayed 5119 and the `hotreload-listen already` count stayed 0, so that broadcast did not enter `OnReceive`. Rotation did. `user_rotation` 1, then 0, kept pid 5119. The log has one `hotreload-listen localabstract:` line and two `hotreload-listen already` lines. A screen DLL sent after that returned `Counter 1 collectible=True` on pid 5119. `evidence/logcat-review-fixes.log`, `evidence/review-device.log`.

A 16-byte zero token was rejected: `hotreload-error UnauthorizedAccessException Rejected hot reload token.` The next good send applied. `/proc/net/tcp` from that process has no `494D` (18765). `/proc/net/unix` contains the same `npoc-` name as the log line. `evidence/review-proc-net-tcp.txt`, `evidence/review-proc-net-unix.txt`.

The Release `NativePoc.Android.dll` is 12800 bytes. Its strings include UTF-16 `title=` and do not include `TcpListener`, `LoadFromStream`, `HotReloadServer`, `AssemblyLoadContext`, `18765`, `hotreload-listen`, or `LocalServerSocket`. The Debug DLL is 22016 bytes. It includes `LoadFromStream`, `HotReloadServer`, `LocalServerSocket`, and UTF-16 `hotreload-listen`, `localabstract`, and `nativepoc-screen`. It does not include `TcpListener`; the TCP listener was removed. Inside the new Mono AOT APK, no entry contains `TcpListener`, `HotReloadServer`, `18765`, `hotreload-listen`, or `LocalServerSocket`. `libmonosgen-2.0.so` contains `LoadFromStream` and `AssemblyLoadContext`. `libmonodroid.so` contains `AssemblyLoadContext`. `libarc.bin.so` contains `MetadataUpdater`. `libSystem.IO.Compression.Native.so` contains ASCII `title=`, the same kind of hit as the earlier scan. `aapt dump permissions` on this APK prints the package line and no `INTERNET` permission. The merged Release manifest has the main activity and no `RECREATE` receiver. The Debug merged manifest still has `INTERNET`; the SDK adds it for the debugger. `evidence/mono-aot-x64-review-loader-scan.txt`, `evidence/mono-aot-x64-review-AndroidManifest.xml`.

### Render delegate and keyed identity

`UseRender` assigns the new delegate, calls `RenderNow`, and on any exception assigns the previous delegate back before the exception leaves. `Apply` stores the new load context only after `UseRender` returns.

On pid 5119, after `Ada` and Save (`Counter 1` / `Hello Ada`), a screen DLL changed the title to `Reloaded ` and the button to `Store`. Reply `Reloaded 1 collectible=True`. UI `Reloaded 1` / `Ada` / `Store` / `Hello Ada`. Same pid. `button-mounted` was still the one line from process start (handle 15270). The button title change went through `SetButton` and did not log another mount.

Then `AppState.Extra` was added, Core was rebuilt on the host only, and Screen read `state.Extra`. The device logged one line: `hotreload-error MissingMethodException Method not found: int NativePoc.AppState.get_Extra()`. Pid stayed 5119. Typing `X` into the field then logged `rendered count=1 name-length=4 title=Reloaded 1` and did not log a second `MissingMethodException`. UI: `Reloaded 1` / `AdaX` / `Store` / `Hello AdaX`. Sending the restored Screen returned `Counter 1 collectible=True`, UI `Counter 1` / `AdaX` / `Save` / `Hello AdaX`, still pid 5119, and `button-mounted` was still one line. `evidence/review-shape-rollback.png`, `evidence/review-ui-shape.xml`, `evidence/review-host-shape.log`.

The n=20 clocks from the TCP listener were not run again. Single `once` sends, clocked around `Send` only, were 27.2 ms, 56.1 ms, 16.4 ms, and 19.3 ms. Those are four observations, not a median.

The `EditText` content description is the field's accessibility label, `Name`. With the text `Ada`, the dump is `text=Ada` and `content-desc=Name`. `evidence/review-ui-saved.xml`. The empty Release dump reports the hint as `text=Name` and `content-desc=Name`. `evidence/mono-aot-x64-review-empty.xml`.

### Release Mono AOT x64 and the MAUI template

Both publishes used SDK 10.0.403 and workload `android` 36.1.118, on `emulator-5554`, API 34, x86_64. Clean `bin/Release` and `obj/Release` before the PoC publish.

PoC: `dotnet publish -c Release -f net10.0-android -r android-x64`. Log `evidence/mono-aot-x64-review.log`, exit 0, no `warning` line. APK 3795146 bytes, sha256 `0ae55fceaf68edbcd421717f7447aedf026a3992975f84ddd4f0fd00dafd93c9`. Uncompressed `libmonosgen-2.0.so` is 3290280. `libaot-NativePoc.Screen.dll.so` is 5168. Installed over the previous package. Pid 5806. `IsDynamicCodeSupported=True IsDynamicCodeCompiled=True`. Empty UI: `Counter 0`, `Name`, `Save`, `Enter a name`. `evidence/mono-aot-x64-review-empty.png`, `evidence/logcat-mono-aot-x64-review.log`. The functional `am start -W` was `COLD` at 1623 ms and is not in the ten.

Ten cold starts, `force-stop` before each, every `LaunchState: COLD`, none dropped. `TotalTime`: 1704, 1429, 1775, 1416, 1596, 1585, 1450, 1634, 1406, 1216. Sorted: 1216, 1406, 1416, 1429, 1450, 1585, 1596, 1634, 1704, 1775. p50 is the mean of 1450 and 1585, **1517.5 ms**. Min 1216, max 1775. `evidence/am-start-mono-aot-x64-review.log` contains the `force-stop` lines and the ten `am start -W` blocks.

MAUI: `dotnet new maui -n MauiBaseline -o /tmp/maui-baseline -f net10.0`, no other flags. On Linux the generated project targets only `net10.0-android`. `ApplicationId` is `com.companyname.mauibaseline`. Android `SupportedOSPlatformVersion` is 21.0. The project was not committed. `dotnet publish -c Release -f net10.0-android -r android-x64` with no extra AOT property. Log `evidence/maui-x64-publish.log`, exit 0, no `warning` line. It optimized assemblies and produced `libaot-MauiBaseline.dll.so` (319744 uncompressed) and `libmonosgen-2.0.so` (3290280 uncompressed). APK 19074917 bytes, sha256 `a0da0ce891ec06527b47899dc4358d278663e741276935cd07ceb4817f7e4581`. Activity `crc64d72af147769f1039.MainActivity`. First UI: `Home`, `Hello, World!`, `Welcome to .NET Multi-platform App UI`, `Click me`. `evidence/maui-x64-empty.png`. The functional `am start -W` was `COLD` at 1412 ms and is not in the ten.

Ten cold starts, `force-stop` before each, every `LaunchState: COLD`, none dropped. `TotalTime`: 1093, 1129, 1109, 1144, 1135, 1010, 1057, 1067, 1177, 1083. Sorted: 1010, 1057, 1067, 1083, 1093, 1109, 1129, 1135, 1144, 1177. p50 is the mean of 1093 and 1109, **1101 ms**. Min 1010, max 1177. `evidence/am-start-maui-x64.log`.

The PoC APK is smaller (3795146 vs 19074917). The PoC cold-start median is not lower (1517.5 vs 1101). These are different screens. The metric is `am start -W` `TotalTime` for each package on the same emulator.

### NativeAOT publish, not installed

`dotnet publish -c Release -f net10.0-android -r android-x64 -p:PublishAot=true -p:EnablePreviewFeatures=true -p:TreatWarningsAsErrors=false`. Log `evidence/nativeaot-x64-review.log`, exit 0. One `IL3053` on `Mono.Android.dll`. The ILC notes for `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception` are in that log. `rg -n XA1040 evidence/nativeaot-x64-review.log` exits 1 and prints no line. `evidence/xa1040-review-grep.txt`. The same command on `evidence/nativeaot-arm64.log`, `evidence/nativeaot-x64.log`, and `evidence/nativeaot-x64-rebuild.log` also exits 1. `evidence/nativeaot-arm64-nowarnserror.log` is not in the tree. APK 2318685 bytes, sha256 `76e5acd224e7f29798d26ca659c08493764250e06bf4e136d80837b842d0f021`, uncompressed `lib/x86_64/libNativePoc.Android.so` 5212960. This APK was not installed. The 464b63e NativeAOT run remains the last one that was started.

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

`am start -W` for the 10 cold starts, every `LaunchState: COLD`, no run dropped. `TotalTime`: 1488, 1124, 1449, 1068, 1152, 1331, 1382, 1551, 1513, 1414. Sorted: 1068, 1124, 1152, 1331, 1382, 1414, 1449, 1488, 1513, 1551. p50 is the mean of the 5th and 6th, **1398 ms**. Min 1068, max 1551. The functional launch before that series was also `COLD` at 1433 ms and is not in the ten. `evidence/am-start-mono-aot-x64-rebuild.log` contains those ten `am start -W` blocks and does not contain the `force-stop` command. `evidence/mono-aot-x64-functional.log` does.

### NativeAOT x64

Separate clean publish with `-p:PublishAot=true -p:EnablePreviewFeatures=true -p:TreatWarningsAsErrors=false`. Log: `evidence/nativeaot-x64-rebuild.log`. Workload warning IL3053. ILC repeated the `monodroid_unhandled_exception` / `monodroid_debugger_unhandled_exception` "will always throw" notes. XA1040 is absent. APK `evidence/apk/nativeaot-x64-rebuild.apk`, 2294109 bytes, sha256 `00b0bd4ab81bca73f16fbb1764710d6e999a34a866bfe2d28e5bc4284ce8153a`. The pre-split NativeAOT x64 APK hashes to `919087af22ee67ce63e01ed4cbcb9b12ca85a3c3e6df4cb0e4795732fa7ec5ab`. Uncompressed `lib/x86_64/libNativePoc.Android.so` is 5164368 bytes and contains the ASCII name `NativePoc.Screen` and the UTF-16 string `activity-start`. The manifest provider is `net.dot.jni.nativeaot.NativeAotRuntimeProvider`.

Pid 14459. `IsDynamicCodeSupported=False IsDynamicCodeCompiled=False`. Empty UI: `Counter 0`, `Name`, `Save`, `Enter a name`. `Ada` logged `field-handle 12006` three times, then `Hello Ada`. Save logged `clipboard-ok poc-1`, UI `Counter 1` / `Ada` / `Hello Ada`. Rotation kept those strings on pid 14459. `evidence/logcat-nativeaot-x64-rebuild.log`. That log has no `hotreload` line.

Ten cold starts, all `LaunchState: COLD`, none dropped. `TotalTime`: 1475, 1254, 1342, 1411, 1088, 1246, 1204, 1348, 1251, 1368. Sorted: 1088, 1204, 1246, 1251, 1254, 1342, 1348, 1368, 1411, 1475. p50 **1298 ms**. Min 1088, max 1475. The functional launch before the ten was `COLD` at 1140 ms and is not in the ten. `evidence/am-start-nativeaot-x64-rebuild.log` contains those ten `am start -W` blocks and does not contain the `force-stop` command. `evidence/nativeaot-x64-functional.log` and `evidence/nativeaot-x64-functional-console.log` do.

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
| 5. Recreate and rotation keep state | **Pass** on Debug and Mono AOT; rotation **Pass** on NativeAOT | `evidence/logcat-recreate.log` has one `runtime` line and one `rendered count=1 name-length=3` line, both pid 6501. It does not contain a second `runtime` line. That pid matches `evidence/logcat-save.log` at 10:48:50, about 17 seconds earlier. `evidence/debug-recreate-ui.xml` and `evidence/debug-rotate-ui.xml` are the dumps. The receiver is `#if DEBUG`. Mono AOT rotation: pid 7935, new `runtime` line, dump still `Counter 1` / `Ada`. NativeAOT rotation: pid 8254, `rendered count=1 name-length=3`. |
| 6. Clipboard round-trip, no binding project | **Pass** | `PlatformBridge.CopyNonce` uses `ClipboardManager` and throws if the read differs. Log `clipboard-ok poc-1` on Debug, Mono AOT, and NativeAOT. No `ApiDefinition` / binding project in the tree. |
| 7–10. In-place hot reload, n=20, p50 ≤ 1500 ms, pid and text stable | **Pass on the n=20 end-to-end series. `dotnet watch` still restarts.** | Series (a): pid 15262, p50 17.5 ms, cold 758.4 ms, `Ada` and counter 1 kept. The earlier 18.1 ms is the n=10 combined clock described above. |
| 11. Rude edit | **Watch restarts. The in-place loader refuses a shape change and keeps the process.** | `evidence/dotnet-watch-rude.log` logs `Stopping: com.scratch.nativepoc` and the `dotnet` host process ids 11554, 11652, 11724, 11829, 11920, and 12024. It does not contain app pids 9931 or 10056. The screen-DLL shape change on pid 15856 returned `MissingMethodException` for `AppState.get_Extra()` and left `Rebound 1` / `Ada` on screen. That trial did not type into the field after the exception. The later trial that does is in the review section. |
| 12. Release Mono AOT installs and items 1–6 pass | **Pass on android-x64** for the 464b63e rebuild. The 20-handle count was not repeated on this process | Pid 13736: widgets, `Ada` with handle 14758 across three keystrokes, Save, rotation, clipboard. Ten cold starts, p50 1398 ms. **Not run** on arm64. |
| 13. `libaot-*.so` in the arm64 APK | **Pass** | `lib/arm64-v8a/libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so`, `libmonodroid.so`. No `libNativePoc.Android.so`. `evidence/apk-listings.txt`. APK 3606628 bytes. |
| 14. No trim/AOT warnings in this repo's source | **Pass** | Debug build `0 Warning(s)`. Release Mono logs have no `warning` lines. NativeAOT's only warning is IL3053 on `Mono.Android.dll` (workload). |
| 15. APK smaller than `dotnet new maui` | **Pass** on android-x64, workload 36.1.118 | PoC Release Mono AOT 3795146 bytes. MAUI template Release 19074917 bytes. Same emulator. See the review section. |
| 16. Startup median ≤ MAUI, 10 runs | **Not met** | PoC p50 1517.5 ms. MAUI p50 1101 ms. Ten `COLD` runs each, none dropped. The PoC median is higher. |
| 17. NativeAOT publish, XA1040 present | **Partial** | Older publishes and the review publish exit 0. `rg -n XA1040` exits 1 on `evidence/nativeaot-arm64.log`, `evidence/nativeaot-x64.log`, `evidence/nativeaot-x64-rebuild.log`, and `evidence/nativeaot-x64-review.log`. IL3053 does appear. The review APK was not installed. |
| 18. NativeAOT runs items 1–4 | **Pass on android-x64** for the 464b63e rebuild | Pid 14459: `Supported=False Compiled=False`, widgets, `Ada` with handle 12006 across three keystrokes, Save `clipboard-ok poc-1`, rotation kept `Counter 1` / `Ada`. Ten cold starts, p50 1298 ms. The earlier pid 12212 empty-save and the 20-handle series were the pre-split publish. **Not run** on arm64. |
| 19. NativeAOT failed | **Does not apply** to the x64 run | The arm64 link failed once, before the NDK was installed. After `ndk;26.3.11579264` the link succeeded. |

## In-place hot reload

`dotnet watch` on SDK 10.0.403 does not update this app in place. Workload `android` 36.1.2, the version installed for that run, has no `HotReloadWebSockets` capability, does not package the startup hook onto the device, and does not reverse a socket. With `StartupHookSupport` left false, `evidence/dotnet-watch.log` says `Project does not support Hot Reload` and then `Stopping: com.scratch.nativepoc`. An earlier draft of this section gave write timestamps, 5459 ms and 5502 ms, and app pids 7177, 7320, and 7443. Those timestamps, deltas, and the pids 7177 and 7443 are not in `evidence/`. The saved UI poll is `evidence/hot-reload-poll.txt`: pid 7320, texts `Tally 0` / `Name` / `Save` / `Enter a name`. `evidence/debug-watch-restart-tally.png` is that cleared screen. `evidence/dotnet-watch-rude.log` is the later `AppState.Extra` watch run. Its process ids are the `dotnet` host, and it logs `Stopping: com.scratch.nativepoc`. Setting `StartupHookSupport` true attached `Microsoft.Extensions.DotNetDeltaApplier` to the short-lived `dotnet run` host (`Process id 9775 ran for 2986ms`, then `Disposing agent communication pipe`). The Android process never connected.

The path that did stay in place is a Debug-only assembly swap, the same shape as MauiReactor's component reload and React Native Fast Refresh: replace the module, keep the state. `Screen.Render` lives in `NativePoc.Screen`. `AppState` and `AppSession` stay in `NativePoc.Core`. On the commit those clocks were measured, `HotReloadServer` listened on `127.0.0.1:18765` (`#if DEBUG`). The host compiled `Screen.cs` and sent the DLL. The app loaded it in a collectible `AssemblyLoadContext`, resolved `NativePoc.Core` to the assembly already in the process, bound `NativePoc.Screen.Render`, and called the activity's render delegate. That TCP listener is the one the review replaced. The listener was absent from the Release build after the split: `0 Warning(s)` in `evidence/release-build-after-hotreload.log`, and that APK was not installed.

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

Checked the patch application against the in-memory host. No case was found where a patch left a mounted view that the new tree does not contain, or dropped a sibling that the new tree still contains. `UnmountDescendants` removes descendant entries and does not call `Remove` on them; `Detach` removes the ancestor, and on Android that view takes its children with it. `Replacing_a_nested_column_drops_its_descendants_and_keeps_the_sibling` locks that: after a nested `Column` becomes a `Text`, the grandchild path throws and the sibling identity is unchanged. `A_title_only_render_keeps_the_field_the_button_and_the_state` is the hot-reload shape: a new tree with a different counter prefix keeps the field, button, and greeting identities and leaves `AppState` at `Ada` / count 1. Button and text-field listeners are attached in `OnMounted`, detached and disposed on unmount, and retargeted when a keyed move changes the path. `button-mounted` is logged from `OnMounted` only. Unkeyed children still use the sibling index, so an insert at the front still rewrites the shared prefix. A list where every child has a `Key` is matched by that key. `Deleting_a_middle_keyed_child_keeps_the_other_views`, `Reordering_keyed_children_keeps_each_view`, and `Deleting_a_leading_keyed_field_keeps_the_shifted_field` keep the surviving view identities. Mixing keyed and unkeyed children throws.

## Measurements

Host unit tests before the screen split: `Passed: 10, Failed: 0, Skipped: 0, Duration: 53 ms`. `evidence/unit-tests.log`. After the split and the two added tests: `Passed: 12, Failed: 0, Skipped: 0, Duration: 52 ms`. `evidence/unit-tests-hotreload.log`. After the keyed tests: `Passed: 15, Failed: 0, Skipped: 0, Duration: 66 ms`. `evidence/unit-tests-review.log`.

Signed APK bytes (`stat`):

| APK | Bytes | Native contents that identify the runtime |
| --- | --- | --- |
| Mono AOT `android-arm64` | 3606628 | `libmonosgen-2.0.so` 3123640 uncompressed, `libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so` |
| Mono AOT `android-x64` | 3774498 | `libmonosgen-2.0.so` 3290280 uncompressed, `libaot-*.so` |
| NativeAOT `android-arm64` | 2187616 | only `lib/arm64-v8a/libNativePoc.Android.so` (5331248 uncompressed) and `classes.dex` |
| NativeAOT `android-x64` | 2294109 | only `lib/x86_64/libNativePoc.Android.so` (5161952 uncompressed) and `classes.dex` |

The NativeAOT uncompressed sizes above are the pre-split APKs in `evidence/apk-listings.txt` (generated 2026-11-08T11:06:42Z). The later 464b63e NativeAOT x64 rebuild is a different file: same compressed length 2294109, sha256 `00b0bd4ab81bca73f16fbb1764710d6e999a34a866bfe2d28e5bc4284ce8153a`, uncompressed `libNativePoc.Android.so` **5164368**. The pre-split x64 APK hashes to `919087af22ee67ce63e01ed4cbcb9b12ca85a3c3e6df4cb0e4795732fa7ec5ab` and its uncompressed `.so` is the table's 5161952. An earlier draft of this table said 5330832 and 5161568. Those two figures are not the listing and are not the rebuild.

`am start -W` `TotalTime`, first run discarded from the median below. One device: `emulator-5554`, API 34, x86_64, KVM. The series logs name the `am start -W` output. The `force-stop` commands for these older series are not repeated inside `evidence/am-start-mono-aot-x64-rebuild.log` or `evidence/am-start-nativeaot-x64-rebuild.log`.

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

XA1040 was not emitted. `rg -n XA1040` exits 1 on the NativeAOT logs named in item 17, including the review publish. `WarningsNotAsErrors` includes it so a future emission would not fail the build. It was not needed for the publishes that succeeded.

## Blockers

- `dotnet watch` still does not reach the Android process. The in-place path is the Debug screen-assembly swap, now on an abstract socket with a per-start token. An `AppState` shape change fails in place with `MissingMethodException`, the previous delegate is restored, and a later keystroke renders the previous screen. The new Release APK does not declare `INTERNET`. The Debug manifest still does, because the SDK adds it.
- Item 15 passed on this emulator. Item 16 did not: the PoC cold-start p50 is 1517.5 ms and the MAUI template p50 is 1101 ms.
- arm64 Mono AOT and arm64 NativeAOT were built and not installed. This host's emulator image is x86_64. The review NativeAOT x64 APK was not installed either.
- NativeAOT is experimental. IL3053 is in the workload assembly. XA1040 did not show up, which disagrees with the plan's expectation that the warning is present. The earlier x64 app ran anyway.
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
- `evidence/review-empty.png` — Debug after the review fixes, pid 5119, `Counter 0`
- `evidence/review-saved.png` — same process, `Counter 1`, `Ada`, `Hello Ada`, clipboard chip `poc-1`
- `evidence/review-reloaded.png` — same process, `Reloaded 1`, button `Store`, `Ada` kept
- `evidence/review-shape-rollback.png` — same process after the rejected `AppState.Extra` edit and one more keystroke, `Reloaded 1`, `AdaX`, `Store`, `Hello AdaX`
- `evidence/review-rotated.png` — same process after rotation back to portrait, `Counter 1`, `AdaX`
- `evidence/mono-aot-x64-review-empty.png` — Release Mono AOT on workload 36.1.118, pid 5806, `Counter 0`
- `evidence/maui-x64-empty.png` — `dotnet new maui` Release, `Hello, World!`, `Click me`
