# Android proof of concept

Measured on this machine on 2026-11-08. Nothing here is a guessed latency, a guessed size, or a screenshot that was not captured. iOS was not built and was not run.

The current measured result is the KVM round below. Release publish is NativeAOT. On that x64 boot its 10-run `TotalTime` median is under the MAUI template median measured next. Profiled Mono AOT and full Mono AOT were not. TCG startup numbers further down are withdrawn. Sentences that describe a `TcpListener` inside the app are an earlier listener. The current Debug listener is the cnative contract below: the abstract name is the fixed non-secret `npoc-hotreload`, and the file token is checked in the handshake. The host forwards `tcp:18765` to that socket. `localabstract:npoc-<hex>` was the previous name and is not current. Release does not contain the listener. The android-arm64 APKs in the next section were built later, on workload `android` 36.1.118. They were not timed on this host. The coordinator measured them on a Mac arm64 emulator. NativeAOT had the lowest p50. Its p90 was not the lowest.

## cnative token contract

Debug only. `OnCreate` calls `HotReloadServer.Start` with no launch extra. The process draws 16 random bytes, deletes a stale `files/hotreload.token` if that older file is present, binds the fixed abstract name `localabstract:npoc-hotreload`, and only then writes the 32 hex characters to the private file `files/nativepoc-hotreload.token`. The socket name is not the token. The token is a per-process bearer value. The host copies it to its temp file, `nativepoc-hotreload.token` under `Path.GetTempPath()`, and sends those 16 bytes on each connection. A new process mints a new token. The first 16 handshake bytes are compared with the process token. The log line is `hotreload-listen`, with no token and no socket name. In code, a second `Start` logs `hotreload-listen already` and does not bind again. `tools/NativePoc.HotReload.Host` forwards `tcp:18765` to `localabstract:npoc-hotreload`. Release does not compile the listener. `tools/devx` was not changed.

The previous device check, `evidence/token-contract-check.log`, is the loader that bound `localabstract:npoc-<hex>`. On that boot a forward to `localabstract:npoc-hotreload` did not complete the handshake, because that name was not the socket. A launch with `--es nativepoc_hotreload 00112233445566778899aabbccddeeff` published a different 32-hex file. The forged extra and the file token were absent from logcat. `files/hotreload.token` was gone. `run-as` could not read `/proc/net/unix` (`Permission denied`). A forward to `localabstract:npoc-<file token>` rejected the forged 16 bytes with `UnauthorizedAccessException` and accepted the file token through to `Rejected screen assembly length 0.`

The host `once` path, given the temp file `cnative` writes, applied the current screen on the previous socket: `once-ms=25.1`, `Counter 0 collectible=True`, pid 15786. `evidence/hot-reload-once-token.log`. That file is unchanged. On the fixed socket, the first `once` was rejected: the temp file `/tmp/nativepoc-hotreload.token` still held an older token. After that file was removed, the host read `files/nativepoc-hotreload.token` and applied: `once-ms=24.4`, `Counter 0 collectible=True`, pid 17069. `evidence/hot-reload-once-fixed-socket.log`.

The sentence that a 12800-byte Release DLL lacked the listener strings cited `evidence/release-build-token-contract.log`. That log is a `dotnet build -c Release -p:PublishAot=false` with 0 warnings. It does not contain a string search. The scan for this tree is the Release NativeAOT APK below.

### Re-review blockers

**TESTED.** `dotnet test` passed 27, failed 0. `evidence/unit-tests-rereview.log`.

**RUN** on the same KVM boot. QEMU pid 4661, `/dev/kvm` open, `emulator-5554`, API 34, `x86_64`. Debug APK built with `EmbedAssembliesIntoApk=true` and `AndroidUseSharedRuntime=false`, `DEBUG_BUILD_EXIT:0`. `evidence/debug-embed-rereview.log`. Installed `com.scratch.nativepoc-Signed.apk`. Activity `crc64ee53024f7ed0aa60.MainActivity`, `launchMode` singleTop.

Pid 16763. One `hotreload-listen` line, no token and no `npoc-` hex. The field was tapped, `A` was typed, and the IME stayed up: `focused=true`, `mInputShown=true`, served `EditText{964d9a7 ... 42,93-1038,211}`. `evidence/rereview-ime-before.png`. A second `am start` with `--es nativepoc_keyed_move 1` was delivered to that activity (`Activity not started`). The greeting handle 15434 moved to index 0 and the title handle 14822 moved to index 3. Field handle 14982 and button handle 15270 stayed. The served `EditText` was still `964d9a7` with the same bounds, `focused=true`, and `mInputShown=true`. `evidence/rereview-ime-after-move.png`. A further `input text d` left the field as `Ad`, still focused. `adb shell cat /proc/net/unix` contained `npoc-hotreload` and did not contain the file token. The token file was 32 lowercase hex and was not in logcat. `files/hotreload.token` was absent. `evidence/rereview-device.log` has no 32-hex token.

Rotation on pid 16763 logged `hotreload-listen already` and did not add another `hotreload-listen`. The pid stayed 16763. `evidence/rereview-after-rotate.png`.

A new process, pid 17069, got one `set-content-view` on launch. A second `am start` with `--es nativepoc_root_swap 1` stayed on pid 17069 and logged a second `set-content-view`. The UI was the title `Counter 0` and no `EditText` or `Button`. `evidence/rereview-root-swap.png`. The fixed-socket `once` above ran on that same pid.

**BUILT.** Release NativeAOT `android-x64` publish, `PUBLISH_EXIT:0`, no warning line in `evidence/nativeaot-release-scan-publish.log`. APK 2490717 bytes, sha256 `83ba4c35264e90113c16d77b77e4f5693b4d2e393ebf177bdef70e3846e4a05a`. Uncompressed `lib/x86_64/libNativePoc.Android.so` is 5753808 bytes. ASCII and UTF-16 searches for `HotReloadServer`, `npoc`, and `nativepoc-hotreload` are absent from the APK bytes, the manifest, `classes.dex`, and that library. The linked `NativePoc.Android.dll` from the same publish is 13312 bytes and also lacks those three strings. It does contain UTF-16 `set-content-view` and `nativepoc_root_swap`, and it does not contain `AotProbe`. `evidence/nativeaot-release-listener-scan.log`.

`NoWarn` for `IL3053` stays project-wide. A per-assembly `nowarn` is not in this ILC. The probe that shows an `IL3050` in this repo still fails is in the NativeAOT section.

**UNVERIFIED.** `View.Dispose` was not observed on an `android.view.View`. iOS was not built. The arm64 APKs were not installed here. The time from `RenderNow` to the next scanned framebuffer stays unverified. A redeploy drops the typed name; persona B ran that drop on pid 20726.

### `cnative run android`

**TESTED.** `dotnet test tools/devx/NativePoc.Devx.Cli.Tests` on the merge passed 49, failed 0. `evidence/cnative-merge-tests.log`.

**RUN** on the same KVM boot, QEMU pid 4661, `emulator-5554`. `tools/devx/cnative run android --device emulator-5554 --restart-on-rude` launched pid 19193. Three `Screen.cs` edits became `Smoke0 0`, `Smoke1 0`, and `Smoke2 0` on that pid, with `Ada` and `Hello Ada` still on screen. A broken prefix was reported as `src/NativePoc.Screen/Screen.cs:5:36: error CS1525: Invalid expression term ';'`, and pid 19193 stayed on `Smoke2 0` / `Ada`. Restoring the file applied `Counter 0` on the same pid. Adding `RudeGeneration` to `AppState` printed `rude edit -> restart required` and `restarting`. The new process was pid 19511 and the UI was `Counter 0` / `Enter a name`. `evidence/cnative-smoke-main.log`, `evidence/cnative-smoke-main.txt`.

**RUN** again for persona B, same boot. A cleared data dir launched pid 20410. `Rio` stayed through `Expo 1`, `src/NativePoc.Screen/Screen.cs:5:36: error CS1525`, and `Fixed 1`. Declining the rude restart printed `pass --restart-on-rude to rebuild and relaunch` and left pid 20410. Accepting it launched pid 20726 at `Fixed 0` / `Enter a name`. A second launch, with that process's token file still on disk, launched pid 20904, read a different token, and applied `Dirty 0` on that pid. Neither log contains `EndOfStreamException`. `evidence/persona-b-clean.log`, `evidence/persona-b-dirty.log`.

The n=20 series is the CLI branch `feat-cnative-resident-reload-025b` at `73dc6e2`, written up in [docs/devx/comparison.md](../docs/devx/comparison.md). On pid 11096 the edit-to-screen clock, from the `Screen.cs` write until the reply line, is p50 59.8246 ms and p90 74.8963 ms. The CLI `edit-to-reply` line on that series is p50 60.0 ms and p90 72.0 ms. `Ada` stayed for all 20. The same compile error stayed on pid 11096. The rude redeploy was pid 11820 and the field was empty. Rows and frames: [docs/devx/evidence/fixed-socket-samples.tsv](../docs/devx/evidence/fixed-socket-samples.tsv), [docs/devx/evidence/fixed-socket-percentiles.txt](../docs/devx/evidence/fixed-socket-percentiles.txt), [docs/devx/evidence/cnative-fixed-socket.log](../docs/devx/evidence/cnative-fixed-socket.log), [docs/devx/evidence/fixed-socket-reload-00.png](../docs/devx/evidence/fixed-socket-reload-00.png), [docs/devx/evidence/fixed-socket-reload-19.png](../docs/devx/evidence/fixed-socket-reload-19.png).

Items 7–10 **pass** on that series, the merged smoke, and the persona B rerun. The reply clock is not a framebuffer scan. That lag stays **UNVERIFIED**. A redeploy drops the typed name. That drop was **RUN**: pid 20726 showed `Enter a name` after `Rio`.

### Debug loader clocks after the socket rename

**RUN** on the same KVM boot. QEMU pid 4661, `emulator-5554`, API 34, `x86_64`. These are the Debug loader in `tools/NativePoc.HotReload.Host`, not `cnative run android`, and they do not pass items 7–10. The socket is `localabstract:npoc-hotreload`. The token is the per-process bearer value. The host forwards `tcp:18765`. Cold is sample 0. No sample was dropped. Every device sample stayed on one pid, kept `Ada` and `Hello Ada`, and the reply was `collectible=True`. The listen line is `hotreload-listen`, with no token. `evidence/logcat-hotreload-a-handshake.log`, `evidence/logcat-hotreload-b-handshake.log`.

| Mode | Clock | Samples ms | Cold | p50 all | p50 warm | min | max | Pid |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| (a) end to end | file write, Roslyn compile, abstract-socket send, post-render reply | 641.3, 36.8, 20.7, 16.8, 17.9, 15.5, 18.4, 24.0, 14.7, 15.2, 14.6, 14.7, 13.2, 12.3, 14.2, 13.1, 13.6, 14.4, 15.2, 13.3 | 641.3 | 14.95 | 14.7 | 12.3 | 641.3 | 17211 |
| (b) precompiled DLL | send through the post-render reply; 20 DLLs of 3584 bytes compiled first | 130.5, 17.5, 7.5, 6.7, 7.3, 5.6, 6.8, 8.2, 6.3, 6.9, 5.7, 10.0, 6.3, 6.3, 6.3, 5.8, 6.6, 7.4, 7.7, 6.3 | 130.5 | 6.75 | 6.7 | 5.6 | 130.5 | 17828 |
| (c) compile only | in-memory Roslyn emit, no file write, no socket | 431.2, 9.5, 6.1, 6.6, 5.3, 5.7, 5.4, 5.4, 7.1, 5.4, 6.1, 5.3, 5.3, 5.2, 4.6, 8.4, 5.3, 4.9, 15.9, 6.3 | 431.2 | 5.55 | 5.4 | 4.6 | 431.2 |  |

(a) sorted center pair is 14.7 and 15.2, so the p50 is 14.95. The tool prints `p50-all-ms=14.9` and `p50-warm-ms=14.7`. Warm n=19, middle sample 14.7. (b) sorted center pair is 6.7 and 6.8, so the p50 is 6.75. The tool prints `p50-all-ms=6.8` and `p50-warm-ms=6.7`. (c) sorted center pair is 5.4 and 5.7, so the p50 is 5.55. The tool prints `p50-all-ms=5.5` and `p50-warm-ms=5.4`. Logs: `evidence/hot-reload-a.log`, `evidence/hot-reload-b.log`, `evidence/hot-reload-c.log`. Screenshots: `evidence/hot-reload-a-cold.png`, `evidence/hot-reload-a-warm.png`. The earlier series on this boot is `evidence/hot-reload-a-pre-handshake.log`, `evidence/hot-reload-b-pre-handshake.log`, and `evidence/hot-reload-c-pre-handshake.log`.

## android-arm64 APKs

**BUILT.** Not installed. Not timed. This host is x86_64 and has no `pixel_8` arm64 AVD, so none of the KVM `TotalTime` numbers apply to these files. The x64 series used workload `android` 36.1.2. These four APKs were published after `dotnet workload update --version android@36.1.118 maui-android@10.0.110`. `dotnet workload list` then reported workload version 10.0.403, `android` `36.1.118/10.0.100`, and `maui-android` `10.0.110/10.0.100`. `evidence/workload-list-arm64.txt`. The NativeAOT and MAUI `build.props` files both record `androidnetsdkversion=36.1.118`. Each publish exited 0.

RID is `android-arm64`. `aapt dump badging` reports `native-code: 'arm64-v8a'` and no other ABI. `apksigner verify` reports signature schemes v2 and v3. The signer is the SDK debug certificate `CN=Android Debug, O=Android, C=US`, SHA-256 `4bae00fb1165509f3080e2e3689a43a6149bb7abeccdda51ad132f46b236097c`. No release keystore was configured. The three Mono APKs contain the same `libmonosgen-2.0.so`, 3123640 bytes, sha256 `f03c6356b2412f4879005e8877c785441bf6380e6cd79700a39c0e7264bb4542`, which is the file in `Microsoft.NETCore.App.Runtime.Mono.android-arm64` 10.0.14.

The MAUI project is `dotnet new maui -n MauiBaseline -o /tmp/maui-baseline-arm64 -f net10.0`. Restore succeeded. On Linux the template's own condition leaves `TargetFrameworks` at `net10.0-android` and appends iOS and Mac Catalyst only when the host is not Linux. That project is not in this repo. Publish: `dotnet publish -c Release -f net10.0-android -r android-arm64`. Log `evidence/arm64-publish-maui.log`. 89 `libaot-*.so` entries. Uncompressed CoreLib AOT is 2429384 bytes.

NativePoc profiled Mono AOT is `dotnet publish -c Release -f net10.0-android -r android-arm64 -p:PublishAot=false`. Release `bin` and `obj` for the three projects were deleted before it. 13 AOT images. CoreLib AOT is 1185568 bytes. Log `evidence/arm64-publish-profiled.log`. No `warning` line.

Full Mono AOT is the same command plus `-p:AndroidEnableProfiledAot=false`, again after deleting those Release outputs. 13 AOT images. CoreLib AOT is 2621688 bytes. `libaot-NativePoc.Core.dll.so` is 83976 bytes and `libaot-NativePoc.Screen.dll.so` is 8512 bytes, against 28064 and 5336 in the profiled APK. Log `evidence/arm64-publish-fullaot.log`. No `warning` line.

NativeAOT is `dotnet publish -c Release -f net10.0-android -r android-arm64` with no extra AOT property, after the same clean. The only native library is `lib/arm64-v8a/libNativePoc.Android.so`, 5948968 bytes, sha256 `5496a94291a58fac70e1c2d60c45acd38eb1dfe1b990400423092fc58848d39e`. Log `evidence/arm64-publish-nativeaot.log`: one `IL3053` on the linked `Mono.Android.dll`, and the two ILC notes that `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception` will always throw. No `XA1040`.

A scan of the three NativePoc arm64 APKs was reported to find no `nativepoc-hotreload.token`, `hotreload-listen`, `LocalServerSocket`, `nativepoc_hotreload`, or `npoc-hotreload`. That search was not saved as a log. The saved scan is the later x64 NativeAOT APK in "Re-review blockers".

| File | Bytes | sha256 | Package / activity |
| --- | --- | --- | --- |
| `maui-baseline-android-arm64-release.apk` | 18604174 | `72f71f151fb6f661329f22e09b26c70d1e48130872531157439dc189b475f8bc` | `com.companyname.mauibaseline` / `crc64d72af147769f1039.MainActivity` |
| `nativepoc-profiled-aot-android-arm64-release.apk` | 3668239 | `69a43923cc1b9106b8bc65e4246403be06cf1095c4e183976a307162bc267cb2` | `com.scratch.nativepoc` / `crc64ee53024f7ed0aa60.MainActivity` |
| `nativepoc-full-aot-android-arm64-release.apk` | 4376847 | `28ead001f58aba1f001a3f4fb348283f14885ef970fe542840ddd8d88e631401` | `com.scratch.nativepoc` / `crc64ee53024f7ed0aa60.MainActivity` |
| `nativepoc-nativeaot-android-arm64-release.apk` | 2359648 | `6d13987efec45db1a517e1f801f61ab25976f4cd01f1d2759e4d883515563739` | `com.scratch.nativepoc` / `crc64ee53024f7ed0aa60.MainActivity` |

The copies and `measure.sh` are under `/opt/cursor/artifacts/`. The script in this repo is `tools/measure-arm64-startup.sh`, the same file. It checks those sha256 values, installs with `adb install -r -d`, then runs 20 `adb shell am start -W -S -n <package>/<activity>` starts. A sample without exactly one `TotalTime`, or with `LaunchState` other than `COLD`, stops the script. Nothing is dropped. p50 and p90 are Hyndman-Fan type 7: for n=20, p50 is the mean of the 10th and 11th sorted `TotalTime` values, and p90 interpolates the 18th and 19th at h=18.1. The script uses `adb`, `sh`, `awk`, `sort`, and `sha256sum` or `shasum`. It was exercised here against a fake `adb` that returned `TotalTime` 1 through 20. That run printed p50 `10.5` and p90 `18.1` for each APK. This host did not run it against an arm64 emulator.

Measured on Mac arm64 emulator by coordinator. The device was `pixel_8`, API 36. Each figure is `TotalTime` from `adb shell am start -W -S`, n=20. The raw samples are `evidence/mac-arm64-startup-run2.txt`. Recomputed with the script's type-7 definition (n=20, p50 is the mean of the 10th and 11th sorted samples, p90 is the 18th plus 0.1 times the gap to the 19th), the percentiles match the table. NativeAOT's p90, 1711.3, is not the lowest. Full AOT's p90 is 1682.9.

| Config | p50 | p90 |
| --- | --- | --- |
| MAUI empty baseline | 1604 | 1729 |
| Mono profiled AOT | 1533.5 | 1804 |
| Mono full AOT | 1428.5 | 1682.9 |
| NativeAOT | 1300 | 1711.3 |

An earlier pass on that emulator, also measured on Mac arm64 emulator by coordinator, gave NativeAOT p50 1305.5 and full AOT p50 1548.5. No p90 was given for that pass. NativeAOT is the lowest p50 in both passes. These numbers are not from the x64 KVM boot.

## NativeAOT on the same KVM boot

**RUN** for the screen. **BUILT** for the warning change. Not a new timing series.

`dotnet publish -c Release -f net10.0-android -r android-x64` after `IL3053` was removed from the warning list. Log `evidence/nativeaot-x64-validate-publish.log`, exit 0. `rg -n "warning |error "` on that log prints nothing. `XA1040` is absent. ILC still prints two notes, not warnings, that `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception` will always throw. The signed APK is 2490717 bytes, `native-code: x86_64`, activity `crc64ee53024f7ed0aa60.MainActivity`, one native library `lib/x86_64/libNativePoc.Android.so` of 5748992 bytes. Workload `android` is `36.1.118`. The emulator is the same QEMU pid 4661, API 34, `x86_64`, holding `/dev/kvm`.

Turning `TrimmerSingleWarn` off makes the old `IL3053` into three `IL3050` errors, all inside the linked `Mono.Android.dll`, none in `NativePoc.Android.dll`, `NativePoc.Core.dll`, or `NativePoc.Screen.dll`. `evidence/il3053-detail.log`.

| Method | Call |
| --- | --- |
| `ManagedTypeManager.<GetInvokerTypeCore>g__MakeGenericType\|4_1` | `Type.MakeGenericType` |
| `JNIEnv.MakeArrayType` | `Type.MakeArrayType` |
| `JNINativeWrapper.CreateDelegate` | `DynamicMethod` constructor |

The workload source disables `IL3050` with a pragma around those calls (dotnet/android issue 8724). The pragma does not put `UnconditionalSuppressMessage` for `IL3050` on the IL, so ILC still reports them and `--singlewarn` collapses them to `IL3053`. `CreateDelegate` has no caller in the linked assemblies. `MakeArrayType` is called from `JNIEnv.CopyArray` and `JNIEnv._GetArray`. `MakeGenericType` is the managed type map's invoker helper. Workload 36.1.118 has no trimmable type map to switch to. An `ILLink.LinkAttributes.xml` does not reach ILC: the linker applies it only to its own pass and does not write the attribute into the assembly ILC reads. `<NoWarn>$(NoWarn);IL3053</NoWarn>` is what ILC receives as `--nowarn`. That list is project-wide. `ilc` in `runtime.linux-x64.microsoft.dotnet.ilcompiler` 10.0.14 has a `singlewarn` switch and no per-assembly `nowarn`, so the `IL3053` suppression cannot be limited to `Mono.Android.dll`.

`WarningsNotAsErrors` is `XA1040` only. That property is not what keeps a warning from this repo visible. A code in `WarningsNotAsErrors` is still printed and does not fail the build, so a future `XA1040` would not fail the publish. It was not emitted. `IL3053` is not in that list. A code in `NoWarn` is not emitted at all.

The ILLink targets turn single-warn off for the entry assembly and for project references (`Microsoft.NET.ILLink.tasks` 10.0.14). A warning in `NativePoc.Core`, `NativePoc.Screen`, or `NativePoc.Android` therefore stays `IL3050` instead of collapsing into `IL3053`. `IL3050` is not in `NoWarn`. `TreatWarningsAsErrors` makes it an error.

A call to `Type.MakeGenericType` in `NativePoc.Core` failed `dotnet publish` before ILC: `error IL3050` and `error IL2055` in `AotProbe.cs`, `PROBE_EXIT:1`. `evidence/il3050-probe-compiler.log`. The same call, with those two compiler warnings disabled by pragma and with no `UnconditionalSuppressMessage`, reached ILC. ILC reported `AOT analysis error IL3050` and `Trim analysis error IL2055` on `NativePoc.AotProbe.Touch`, then `MSB3077` because the command returned 0 and the build still detected errors. `PROBE_EXIT:1`. The method was not folded into `IL3053`. `evidence/il3050-probe-publish.log`. The probe source was removed after that publish. Host tests after the socket change: 27 passed, 0 failed. `evidence/unit-tests-rereview.log`.

The installed process is pid 16198. `IsDynamicCodeSupported=False` and `IsDynamicCodeCompiled=False`. The first frame is `Counter 0`, an empty `EditText` whose hint is `Name`, `Save`, and `Enter a name`. `evidence/kvm-nativeaot-validate-render.png`. Typing `Ada` kept field handle 11974, title handle 11814, button handle 12294, and greeting handle 12458, and the greeting became `Hello Ada`. `evidence/kvm-nativeaot-validate-typed.png`. Save logged `clipboard-ok poc-1` and showed `Counter 1` on those same four handles. `evidence/kvm-nativeaot-validate-saved.png`. `button-mounted` happened once before the rotation.

`settings put system user_rotation 1` recreated the activity on pid 16198. The new field handle is 14310 and the new button handle is 12846. The UI stayed `Counter 1`, `Ada`, `Hello Ada`. `evidence/kvm-nativeaot-validate-rotated.png`. Rotation back to portrait was another `activity-start` on pid 16198, field handle 14918, button handle 15110, same three strings. `evidence/kvm-nativeaot-validate-portrait.png`.

A second process, pid 16467, was started with `--es nativepoc_keyed_move 1` after `force-stop`. The first render mounted title 11814, field 11974, button 12294, greeting 12458. The second render, still inside `OnCreate`, moved the greeting to index 0 and the title to index 3 and kept those four handles. `button-mounted` was logged once. The UI order was `Enter a name`, `Name`, `Save`, `Counter 0`. `evidence/kvm-nativeaot-validate-keyed.png`. The full log is `evidence/kvm-nativeaot-validate.log`. It has no `hotreload` line. A launch without the extra keeps the original order. The Mac percentiles above were measured on the APKs built before this extra and the tree log existed.

Release publish stays NativeAOT. Mono AOT remains `-p:PublishAot=false`, and full AOT adds `-p:AndroidEnableProfiledAot=false`.

## KVM round

One boot of AVD `poc34`. API 34, `x86_64`, `google_apis`. Emulator 37.2.13.0. Command line: `-accel on -gpu swiftshader_indirect -memory 2048 -cores 2 -no-window -no-snapshot -port 5554`. The log then says RAM was raised to 2560MB. `adb shell getprop ro.build.version.sdk` is 34, `ro.product.cpu.abi` is `x86_64`, `sys.boot_completed` is 1. Boot completed in 41238 ms. `evidence/emulator-kvm-boot.log`.

`emulator -accel-check` exited 0 and printed `KVM (version 12) is installed and usable.` `evidence/emulator-accel-check-kvm.txt`. QEMU pid 4661 holds `/dev/kvm`. `/dev/kvm` is mode `crw-rw-rw-`. `window_animation_scale` was left at `1.0`. `evidence/kvm-animation-scale.txt`. No sample below was taken with `-no-accel` or TCG.

SDK 10.0.403. Workload `android` is `36.1.2/10.0.100`. Workload `maui-android` is `10.0.0/10.0.100`. `evidence/workload-list-kvm.txt`. These x64 numbers were taken before this machine moved to `android` 36.1.118. NDK `26.3.11579264`. JDK 21.

`dotnet new maui -n MauiBaseline -o /tmp/maui-baseline -f net10.0` now targets `android;ios;maccatalyst`. The restore failed with `NETSDK1178` because `Microsoft.iOS.Sdk.net10.0_26.0` is not installed. `evidence/maui-new-kvm.log`. The generated `TargetFrameworks` was then set to `net10.0-android` only. That project is not in this repo. Publish log `evidence/maui-x64-publish-kvm.log`, exit 0. 88 `libaot-*.so` entries. Activity `crc64d72af147769f1039.MainActivity`.

### What was already on main, and what this round changed

At `c9f5bb1` the re-review items were still open in the tree this round started from. The token was still read from the `nativepoc_hotreload` launch extra and put in the socket name and the log line. `Screen.Render` set no keys. A keyed reorder detached every sibling and reinserted the survivors. `SetContentView` ran once. `OnUnmounted` disposed a view that could still be a child. Later sections of this report still described `TcpListener` on `127.0.0.1:18765` as the current listener.

This table is the KVM boot. It is not the current loader and it is not the current warning list. The current listener is the cnative section. The current warning list is the NativeAOT section.

| Item | State after this round |
| --- | --- |
| App-generated token, launch extra ignored, token not logged | **RUN.** Pid 14082 was started with `--es nativepoc_hotreload 00112233445566778899aabbccddeeff`. The private file was `<redacted-token>`. Neither hex is in that logcat. The listen line is `hotreload-listen localabstract:npoc-hotreload`. A connection with the extra's bytes returned `UnauthorizedAccessException: Rejected hot reload token.` The file token then got past the check and returned `InvalidOperationException: Rejected screen assembly length 0.` `evidence/kvm-token-check.log`. |
| Second `Start` logs `hotreload-listen already` and does not bind again | **BUILT.** `Interlocked.CompareExchange` is in `HotReloadServer.Start`. **UNVERIFIED** on device this round: nothing called `Start` twice. |
| Keys on `Screen.Render` | **BUILT** and **TESTED.** Keys are `title`, `name`, `save`, `greeting`. `Screen_render_assigns_a_key_to_every_child`. |
| Keyed move keeps the view | **TESTED** in the memory host. A middle delete, a reorder, a middle insert, and 200 edits from `Random(20261108)` leave stationary children attached (`Removed == 0`). **UNVERIFIED** on an Android view: this round did not reorder the live screen. The hot-reload title edits are property updates. Pid 14365 logged `field-handle 15014` four times (mount plus `Ada`) and `button-mounted` once, then 20 applies, and did not log another mount. |
| `SetContentView` when the root instance changes | **BUILT.** `RenderNow` calls it only when the root view is a different instance. **TESTED** by `Replacing_the_root_mounts_a_new_view_and_disposes_the_old_tree`. **UNVERIFIED** on device. The acceptance screen always returns a `Column`, so the device path is the first `SetContentView`. |
| Dispose after the view has left its parent | **BUILT.** `RemoveSubtree` removes the node, then `OnUnmounted` detaches listeners, removes the view if it still has a parent, and disposes. The memory host records `DisposedWhileAttached` and the new tests require it to be false. **UNVERIFIED** against `android.view.View`. |
| Report | This table records that boot. The current listener is the cnative section. The TCP how-to and the TCG startup section are earlier passes. The TCP n=20 files were copied to `evidence/hot-reload-*-tcp.log` and `evidence/hot-reload-a-tcp-*.png` before the new clocks overwrote the old paths. |

Host tests after those edits: `Passed: 23, Failed: 0, Skipped: 0, Duration: 70 ms`. `evidence/unit-tests-kvm.log`. Debug deploy `dotnet build -c Debug -f net10.0-android -r android-x64 -t:Run -p:AdbTarget=-e` exited 0 with 0 warnings. `evidence/debug-run-kvm.log`. The installed package has `.__override__` and `hotreload.token`.

### Release configs

`TreatWarningsAsErrors` stayed on for that publish. The sentence that `WarningsNotAsErrors` was `XA1040;IL3053` described an earlier csproj. The file now has `IL3053` in `NoWarn` and `WarningsNotAsErrors` is `XA1040` only. The publish log cited next still contains one `IL3053`, which is what that older list allowed.

Release now sets `PublishAot` and `EnablePreviewFeatures`. `dotnet publish -c Release -f net10.0-android -r android-x64` is NativeAOT. That publish is `evidence/startup-publish-default-kvm.log`, exit 0, one `IL3053` on the linked `Mono.Android.dll`, plus the two ILC notes that `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception` will always throw. `rg -n XA1040` on that log prints nothing.

Mono AOT is the same publish with `-p:PublishAot=false`. Profiled AOT is that default (`AndroidEnableProfiledAot` stays true, 13 assemblies, `libaot-System.Private.CoreLib.dll.so` 1203832 bytes). Full AOT adds `-p:AndroidEnableProfiledAot=false` (CoreLib AOT image 2669288 bytes). Those two logs have no `warning` line. `evidence/startup-publish-profiled-kvm.log`, `evidence/startup-publish-fullaot-kvm.log`, `evidence/startup-publish-nativeaot-kvm.log`.

The explicit NativeAOT APK and the later default-publish APK are the same length and the same uncompressed `.so` length. The hashes differ, so the 10-run for the explicit APK is not the shipped binary's time. Both were installed and both reported `IsDynamicCodeSupported=False` and `IsDynamicCodeCompiled=False`.

After that csproj change, `dotnet publish -c Release -f net10.0-android -r android-x64 -p:PublishAot=false` was run once. Log `evidence/startup-publish-profiled-after-default-kvm.log`, exit 0, no `warning` line, 13 AOT images. The signed APK is 3836114 bytes, sha256 `314346a9533f3fb0df36924ec1d29740f5086f62c311c454b932cbfc93b89d66`. Uncompressed CoreLib AOT is 1203768 bytes, not the 1203832 bytes in the timed profiled APK. Same length as the timed APK, different hash. **BUILT.** It was not installed and it was not timed. Full AOT was not republished after the default change.

| APK | Bytes | sha256 | Native library |
| --- | --- | --- | --- |
| Profiled Mono AOT | 3836114 | `7cdb86cfeef72d0d54f38886470baeaf7136695d9929c0646a674bbed15e5bdb` | `libmonosgen-2.0.so` 3290280, CoreLib AOT 1203832, Screen AOT 5168 |
| Full Mono AOT | 4667602 | `932d647aa60f5debf05e9855b89b54804e3af175276c449dcab04fdbfbf91d9a` | same `libmonosgen-2.0.so`, CoreLib AOT 2669288, Screen AOT 7848, Core AOT 76680 |
| NativeAOT, explicit publish, the primary/reversed/steady series | 2482525 | `c47b7438cb8744e1595cb1e37037c1e584fbe775866408ce853c3f32801b58c3` | `libNativePoc.Android.so` 5730976, sha256 `6f22dca37c77cc7806de9c3b545e7e29a4bb239d198c17f50c6fd912c4f7bbb5` |
| NativeAOT, default `dotnet publish -c Release` | 2482525 | `d37c701a23500e0f4c7287ae6e53e8eeb798f12bcb8b0db293b5895ded7eae60` | `libNativePoc.Android.so` 5730976, sha256 `a7bc8499200e82ae4ae48ba9757b0469d29e91c4ed94670b0db17be908034a17` |
| MAUI template, TFM patched | 18915080 | `5c1ff7fe00fa5667dbf7ec7aee1121104e1139db3c7f6d01b668f1294f76664d` | `libmonosgen-2.0.so` 3290280, CoreLib AOT 2434288, 88 AOT images |

The two `libmonosgen-2.0.so` copies in the Mono APKs and the MAUI APK are the same sha256, `edfa02c346f7eafc6cab665b27c667a93ecc84b7f40d042110ca4e343b2de8e4`.

### Cold start

Headline is `am start -W` `TotalTime`. Every counted start was `LaunchState: COLD` after `am force-stop`. None were dropped. p50 for 10 samples is the mean of the 5th and 6th sorted values. A functional launch before a series is not one of the ten.

Slices, from `logcat -v epoch`, first occurrence of each marker: runtime init is `Start proc` to `NativePoc: activity-start`; widget build is `activity-start` to `NativePoc: rendered`; first frame is `rendered` to `ActivityTaskManager: Displayed`. `start_to_displayed` is `Start proc` to `Displayed`. MAUI has no `activity-start` line, so its runtime init and widget build are blank. On profiled sample 0 the three slices are 374, 83, and 597 ms and sum to the recorded `start_to_displayed` of 1054; `TotalTime` is 1075. The gap is the time before `Start proc`. First frame includes the SwiftShader present. `window_animation_scale` was 1.0.

The primary four were back to back on this boot, in this order, each `adb install -r` then a functional start then the ten: MAUI, profiled AOT, explicit NativeAOT, full AOT. The primary log files mark each sample `force-stop then am start -W` and do not echo the force-stop process. Later series were written by the script that records `adb shell am force-stop` in the block. MAUI's primary median is not the steady number. The same package, later on this boot, with no reinstall, lands next to the older KVM median of 1101 ms.

Primary, same boot:

| Config | TotalTime ms | Sorted | p50 | min | max | runtime p50 | widget p50 | first frame p50 | Log |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| MAUI, first | 1849, 2139, 1903, 2226, 1936, 1952, 1513, 1730, 1614, 1567 | 1513, 1567, 1614, 1730, 1849, 1903, 1936, 1952, 2139, 2226 | 1876.0 | 1513 | 2226 |  |  |  | `evidence/startup-maui-kvm.log` |
| Profiled AOT | 1075, 927, 1011, 961, 998, 830, 912, 1032, 949, 973 | 830, 912, 927, 949, 961, 973, 998, 1011, 1032, 1075 | 967.0 | 830 | 1075 | 282.0 | 66.0 | 583.0 | `evidence/startup-profiled-kvm.log` |
| NativeAOT, explicit | 773, 920, 881, 962, 1008, 851, 851, 814, 977, 836 | 773, 814, 836, 851, 851, 881, 920, 962, 977, 1008 | 866.0 | 773 | 1008 | 233.5 | 36.5 | 588.5 | `evidence/startup-nativeaot-kvm.log` |
| Full AOT | 1007, 1044, 925, 894, 1152, 945, 922, 922, 954, 1169 | 894, 922, 922, 925, 945, 954, 1007, 1044, 1152, 1169 | 949.5 | 894 | 1169 | 327.5 | 37.0 | 604.0 | `evidence/startup-fullaot-kvm.log` |

`start_to_displayed` p50 in that primary pass: MAUI 1827.5, profiled 943.0, NativeAOT 850.5, full AOT 936.0. Every PoC sample in that pass is under every MAUI sample (full AOT max 1169, MAUI min 1513). That MAUI series is the first series of the boot.

Later on the same boot, no new emulator process:

| Series | What changed before the ten | TotalTime p50 | min | max | runtime p50 | widget p50 | first frame p50 | Log |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| NativeAOT explicit, reinstalled | install, then the ten | 1093.5 | 893 | 1253 | 245.5 | 63.5 | 743.5 | `evidence/startup-nativeaot-rev-kvm.log` |
| MAUI, already installed | nothing | 1127.0 | 1000 | 1381 |  |  |  | `evidence/startup-maui-rev-kvm.log` |
| NativeAOT explicit, no reinstall | nothing | 973.5 | 819 | 1284 | 268.5 | 46.0 | 634.5 | `evidence/startup-nativeaot-steady-kvm.log` |
| MAUI, no reinstall | nothing | 1102.5 | 1046 | 1276 |  |  |  | `evidence/startup-maui-steady-kvm.log` |
| Profiled, already installed | ten cold starts, none dropped | 1157.0 | 1001 | 1585 | 370.5 | 69.5 | 720.5 | `evidence/startup-profiled-steady-kvm.log` |
| Full AOT, already installed | ten cold starts, none dropped | 1100.0 | 934 | 1202 | 341.5 | 40.0 | 677.0 | `evidence/startup-fullaot-steady-kvm.log` |
| NativeAOT default publish | install of the new APK, functional start not in the ten | 968.5 | 865 | 1153 | 243.0 | 35.0 | 663.0 | `evidence/startup-default-kvm.log` |
| MAUI immediately after that | nothing | 1066.5 | 961 | 1276 |  |  |  | `evidence/startup-maui-final-kvm.log` |

The steady MAUI p50, 1102.5, matches the earlier KVM MAUI median of 1101. The default NativeAOT p50 of 968.5 was measured immediately before the MAUI p50 of 1066.5. The ranges overlap: NativeAOT max 1153, MAUI min 961. The median is lower. The same is true of the explicit NativeAOT steady pair (973.5 vs 1102.5) and the reversed pair (1093.5 vs 1127.0). Profiled steady, 1157.0, is slower than the MAUI series beside it. Full AOT steady, 1100.0, is 2.5 ms under that MAUI median and the ranges overlap; that is not a result to ship on. Runtime init is where NativeAOT is ahead (default 243.0 ms, profiled steady 370.5 ms, full AOT steady 341.5 ms). Widget build stays under 70 ms. First frame stays the largest slice on this SwiftShader emulator.

NativeAOT is the Release default because it is the fastest of these four that runs the screen and reports both runtime flags false.

### Functional checks

Each of these launches is outside the tens above.

| Config | Pid | Flags | What was checked | Functional TotalTime | Evidence |
| --- | --- | --- | --- | --- | --- |
| MAUI |  |  | `Hello, World!`, tap, button became `Clicked 1 time` | 2093 COLD | `evidence/kvm-maui-functional.log` |
| Profiled AOT | 6594 | True / True | four widgets, one `EditText` handle 14790 across `Ada`, Save, clipboard, rotation, same pid | 1467 COLD | `evidence/kvm-profiled-functional.log` |
| NativeAOT explicit | 7398 | False / False | same checks, handle 12006 | 1136 COLD | `evidence/kvm-nativeaot-functional.log` |
| Full AOT | 8121 | True / True | same checks, handle 14790 | 1226 COLD | `evidence/kvm-fullaot-functional.log` |
| NativeAOT default | 12191 | False / False | same checks, handle 12006 | 1488 COLD | `evidence/kvm-default-functional.log` |

A separate rotation of the explicit NativeAOT APK kept pid 11906 through landscape and back. After both rotations the log still says `rendered count=1 name-length=3 title=Counter 1` and the flags are False / False. Field handles changed across the new activities (12006, then 14054, then 14758), which is a new activity, not a new process. `evidence/kvm-nativeaot-rotation.log`.

### Hot reload on this KVM boot, n=20

The listen line in these logs is `hotreload-listen localabstract:npoc-hotreload`. That is this boot's loader, before the handshake used a separate bearer token. The files are `evidence/hot-reload-a-pre-handshake.log`, `evidence/hot-reload-b-pre-handshake.log`, and `evidence/hot-reload-c-pre-handshake.log`.

Debug only, after `-t:Run`. The host is `tools/NativePoc.HotReload.Host`. Cold is sample 0. Warm is samples 1–19. No sample was dropped. Every device sample stayed on one pid, kept `Ada`, `Hello Ada`, and count 1, and the reply started with the expected title plus `collectible=True`.

| Mode | Clock | Samples ms | Cold | p50 all | p50 warm | min | max | Pid |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| (a) end to end | file write, Roslyn compile, abstract-socket send, post-render reply | 849.1, 44.6, 26.8, 16.8, 16.9, 15.4, 16.8, 15.4, 13.8, 15.5, 13.1, 15.4, 13.7, 14.2, 12.9, 16.0, 19.1, 12.7, 12.0, 14.0 | 849.1 | 15.4 | 15.4 | 12.0 | 849.1 | 14365 |
| (b) precompiled DLL | send through the post-render reply; 20 DLLs of 7680 bytes compiled first | 63.8, 87.6, 8.3, 7.3, 7.0, 5.6, 7.0, 7.8, 5.7, 6.2, 5.5, 5.3, 5.1, 7.1, 6.2, 8.2, 5.9, 6.3, 5.8, 9.6 | 63.8 | 6.65 | 6.3 | 5.1 | 87.6 | 15006 |
| (c) compile only | in-memory Roslyn emit, no file write, no socket | 456.8, 14.2, 8.9, 7.5, 8.1, 8.2, 8.5, 7.4, 7.0, 7.6, 7.4, 7.5, 7.2, 6.9, 7.5, 7.5, 7.3, 7.3, 7.2, 7.7 | 456.8 | 7.5 | 7.5 | 6.9 | 456.8 |  |

(a) sorted center pair is 15.4 and 15.4. (b) sorted center pair is 6.3 and 7.0, so the p50 is 6.65; the tool prints `p50-all-ms=6.7`. Sample 1 of (b) is 87.6, which is above the cold sample. (c) sorted center pair is 7.5 and 7.5. Logs: `evidence/hot-reload-a-pre-handshake.log`, `evidence/hot-reload-b-pre-handshake.log`, `evidence/hot-reload-c-pre-handshake.log`. Logcat: `evidence/logcat-hotreload-a-kvm.log` (20 `hotreload-applied`, one listen line, pid 14365), `evidence/logcat-hotreload-b-kvm.log` (20 applies, pid 15006). `Screen.cs` was restored to `TitlePrefix = "Counter "` after (a) and (b). (c) does not write the file. These clocks were not repeated as the current series. The run after the socket rename is the later section.

### Screenshots from this boot

- `evidence/kvm-maui-empty.png` — MAUI template, `Hello, World!`, `Click me`
- `evidence/kvm-maui-clicked.png` — same package, button `Clicked 1 time`
- `evidence/kvm-profiled-empty.png`, `evidence/kvm-profiled-saved.png`, `evidence/kvm-profiled-rotated.png` — profiled AOT, empty, after Save, after rotation
- `evidence/kvm-nativeaot-empty.png`, `evidence/kvm-nativeaot-saved.png`, `evidence/kvm-nativeaot-rotated.png` — explicit NativeAOT
- `evidence/kvm-nativeaot-landscape.png` — pid 11906, landscape, `Counter 1`, `Ada`, `Hello Ada`
- `evidence/kvm-fullaot-empty.png`, `evidence/kvm-fullaot-saved.png`, `evidence/kvm-fullaot-rotated.png` — full AOT
- `evidence/kvm-default-empty.png` — default NativeAOT, `Counter 0`, `Name`, `Save`, `Enter a name`
- `evidence/kvm-default-saved.png` — same config, `Counter 1`, `Ada`, `Hello Ada`, clipboard chip `poc-1`
- `evidence/kvm-default-rotated.png` — same process after rotation
- `evidence/hot-reload-a-cold-pre-handshake.png` — pid 14365, `A00 1`, `Ada`, `Save`, `Hello Ada`
- `evidence/hot-reload-a-warm-pre-handshake.png` — same process, `A19 1`, `Ada` still in the field
- `evidence/hot-reload-b-last.png` — after sample 19 of mode (b), not a timed frame, `B19 1`, `Ada`, `Save`, `Hello Ada`

### Still unverified

- arm64 install and any startup number on a phone
- iOS
- a live reorder while typing, a live root swap, and a second `Start` were open at the end of this round. They were run later on this same emulator. See "Re-review blockers".
- `View.Dispose` on a real Android view
- `/proc/net/unix` was not read on this boot. A later shell read found `npoc-hotreload` and did not find the file token.
- bit-identity of the two NativeAOT APKs; they are the same size and not the same hash
- the republished profiled APK (`314346a9…`) was not installed; full AOT was not republished after the NativeAOT default

## Earlier passes

The paragraphs below are the report as it stood before this KVM round. Item 16 in that text is the profiled Mono AOT result from the previous KVM boot, 1517.5 ms against 1101 ms. The TCG section is not a startup claim.

The plan's "fully working" bar is items 1–16 in [runtime-feasibility.md](runtime-feasibility.md). Item 15 was measured after the review fixes: the Release Mono AOT x64 APK is smaller than `dotnet new maui` on the same emulator. Item 16 on that earlier KVM boot was not met for profiled Mono AOT. A later pass retried Release startup on TCG after KVM faulted; those numbers are withdrawn. Debug in-place reload of the screen assembly works. `dotnet watch` still restarts the process. The screen works on Debug and on Release Mono AOT, on an x86_64 emulator. The review NativeAOT x64 APK was installed in that later pass.

Installing the `maui-android` workload on that earlier machine upgraded `android` from 36.1.2 to 36.1.118. `dotnet workload install maui` is rejected on this SDK with `Workload ID maui isn't supported on this platform`. The template comes from `maui-android` 10.0.110. Numbers under "Verification of 464b63e" and the older tables were taken on 36.1.2. Numbers under "Review fixes" were taken on 36.1.118. `evidence/workload-list-review.txt`. This round's workload list is `evidence/workload-list-kvm.txt` and is still 36.1.2.

Clone URL: `github.com/filipef101/c-sharp-native`

## Review fixes

This section is the tree after `c9f454f`, before the KVM round. The loader it describes still takes the token from the launch extra. That is not the current loader.

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

### NativeAOT publish

`dotnet publish -c Release -f net10.0-android -r android-x64 -p:PublishAot=true -p:EnablePreviewFeatures=true -p:TreatWarningsAsErrors=false`. Log `evidence/nativeaot-x64-review.log`, exit 0. One `IL3053` on `Mono.Android.dll`. The ILC notes for `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception` are in that log. `rg -n XA1040 evidence/nativeaot-x64-review.log` exits 1 and prints no line. `evidence/xa1040-review-grep.txt`. The same command on `evidence/nativeaot-arm64.log`, `evidence/nativeaot-x64.log`, and `evidence/nativeaot-x64-rebuild.log` also exits 1. `evidence/nativeaot-arm64-nowarnserror.log` is not in the tree. APK 2318685 bytes, sha256 `76e5acd224e7f29798d26ca659c08493764250e06bf4e136d80837b842d0f021`, uncompressed `lib/x86_64/libNativePoc.Android.so` 5212960. It was installed in the cold-start pass below. The 464b63e NativeAOT run remains the last one that completed Save and rotation.

## Release cold start

These TCG numbers are not a startup result. The emulator in that session had lost KVM. Do not compare them with the KVM round above.

The target is a Release cold start at or under the MAUI KVM median, 1101 ms. This session could not boot that emulator. `dmesg` reports `kernel BUG at arch/x86/kvm/x86.c:689` in `kvm_arch_vcpu_create`. An `-accel on` emulator stays at a few percent CPU with `adb` offline. Every number in this section is from AVD `poc34`, API 34, x86_64, started with `-no-accel -gpu swiftshader_indirect -memory 2048 -cores 2`. `am start -W` is not the metric here. The script force-stops, clears logcat, starts the activity, and records `ActivityTaskManager` `Displayed`. No sample was dropped. p50 is the mean of the 5th and 6th sorted values. Workload `android` is 36.1.118. The KVM 1517.5 ms and 1101 ms figures stay in the review section and are a different run.

Two TCG boots. The first measured the existing profiled-AOT PoC APK and the MAUI APK. The second boot, after the KVM attempt, measured every other variant. Later series on the second boot are faster than earlier ones, so a median from the end of the boot is not directly a win over a median from the start. The system dialog "Process system isn't responding" was on screen during the second boot. The app still logged `rendered count=0 name-length=0 title=Counter 0`, and `Displayed` still fired.

### Where the time goes

On the second boot, the default profiled-AOT APK (the 1517.5 ms binary) has ten cold starts with `Start proc`, `activity-start`, `rendered`, and `Displayed` in `evidence/startup-profiled-sameboot-tcg.log`. Median slices, same ten runs:

| Slice | p50 |
| --- | --- |
| `Start proc` to `activity-start` | 9431.5 ms |
| `activity-start` to `rendered` | 3971.0 ms |
| `rendered` to `Displayed` | 3585.5 ms |

The `Displayed` duration for those runs, which includes the gap before `Start proc`, has p50 18335.5 ms. Runtime init is the largest slice. Building the four widgets is next. The draw after `SetContentView` is the same order as the widget build. The NativeAOT remeasure on the same boot (`evidence/startup-nativeaot-sameboot-tcg.log`) splits as 4292.5 ms, 1068.5 ms, and 3059.0 ms. `IsDynamicCodeSupported=False` and `IsDynamicCodeCompiled=False` on each of those ten processes. NativeAOT shortens runtime init and widget build. The draw after `rendered` stays a few seconds.

### Ten-run medians

Default Release is profiled AOT. The SDK sets `AndroidEnableProfiledAot` true when `RunAOTCompilation` is true, and includes `dotnet.aotprofile`. That APK's `libaot-System.Private.CoreLib.dll.so` is 1202888 bytes uncompressed. Full AOT (`AndroidEnableProfiledAot=false`) grows it to 2668160. The startup profile (`tools/startup.aotprofile`, `AndroidUseDefaultAotProfile=false`, selected with `-p:NativePocAotProfile=...`) shrinks it to 532320. `InvariantGlobalization=true` shrinks it to 1100600. `libmono-component-marshal-ilgen.so` (37848 bytes) is in every Mono APK below, including the one built with `AndroidUseInterpreter=false`. That build's `environment.x86_64.ll` says `mono_aot_mode_name` is `normal` and `mono_components_mask` is 0.

First TCG boot:

| Attempt | APK bytes | Displayed samples, ms | Sorted | p50 |
| --- | --- | --- | --- | --- |
| Profiled AOT, existing review APK | 3795146 | 25638, 20747, 16906, 27778, 37571, 26899, 20533, 21472, 27889, 36033 | 16906, 20533, 20747, 21472, 25638, 26899, 27778, 27889, 36033, 37571 | **26268.5** |
| MAUI template, existing APK | 19074917 | 87919, 95694, 78335, 70788, 79432, 154164, 86534, 69110, 91885, 75336 | 69110, 70788, 75336, 78335, 79432, 86534, 87919, 91885, 95694, 154164 | **82983.0** |

`evidence/startup-baseline-tcg.log`, `evidence/startup-maui-tcg.log`. On this boot the PoC median is lower than the MAUI median. Both are tens of seconds. They do not replace the KVM comparison, where MAUI was the lower one.

Second TCG boot, in the order they were measured:

| Attempt | APK bytes | sha256 | Displayed samples, ms | p50 |
| --- | --- | --- | --- | --- |
| NativeAOT, review APK, first install | 2318685 | `76e5acd2…d0f021` | 28115, 21739, 15007, 15989, 17071, 19685, 23279, 11820, 12701, 17384 | **17227.5** |
| No AOT, `RunAOTCompilation=false` | 3167299 | `e0f42ed9…b862a2` | 37176, 35761, 41405, 56353, 59603, 31548, 32872, 32771, 35569, 31921 | **35665.0** |
| Full AOT, interpreter left at the SDK default | 4561098 | `03fda237…656345` | 23569, 23028, 18021, 23640, 81074, 38770, 17474, 15072, 29918, 28284 | **23604.5** |
| `startup.aotprofile` only | 3479754 | `fc5d1601…a167b5` | 44671, 35233, 30947, 27474, 32050, 36758, 36040, 32137, 33427, 37610 | **34330.0** |
| `TrimMode=full`, `AndroidLinkMode=Full` | 3786954 | `29db35f6…c5ef58` | 30958, 23196, 23096, 20380, 20592, 20777, 20209, 24454, 20124, 17371 | **20684.5** |
| `InvariantGlobalization=true`, profiled AOT | 3709130 | `c54f3df8…5f17e3` | 27118, 20238, 17689, 16691, 19343, 16212, 18001, 16936, 17477, 20334 | **17845.0** |
| Profiled AOT again, the review APK | 3795146 | `0ae55fce…fd93c9` | 31059, 24205, 16749, 22236, 15391, 17640, 17360, 21873, 18047, 18624 | **18335.5** |
| NativeAOT again | 2318685 | `76e5acd2…d0f021` | 18470, 11340, 10885, 12529, 9310, 12047, 9017, 7971, 8984, 8717 | **10097.5** |
| Full AOT, `AndroidUseInterpreter=false` | 4561098 | `21d3910b…579942` | 26553, 13894, 13453, 11577, 11767, 10590, 12376, 10016, 10255, 12040 | **11903.5** |
| Profiled AOT plus a white `windowBackground` theme | 3795146 | `50fc00f4…335144` | 23467, 10807, 12629, 10502, 13772, 10667, 10579, 11199, 9859, 11904 | **11003.0** |

Sorted lists are in the logs: `evidence/startup-nativeaot-tcg.log`, `startup-noaot-tcg.log`, `startup-fullaot-tcg.log`, `startup-startup-profile-tcg.log`, `startup-trim-full-tcg.log`, `startup-invariant-tcg.log`, `startup-profiled-sameboot-tcg.log`, `startup-nativeaot-sameboot-tcg.log`, `startup-interp-off-tcg.log`, `startup-splash-tcg.log`. Publish logs are `evidence/startup-publish-*.log`, each `EXIT:0`. Full trim logged `rendered` with `Counter 0` on all ten runs.

The last three rows ran back to back. NativeAOT p50 10097.5 ms is the lowest median in the table. Full AOT with the interpreter property off is 11903.5 ms. The white-window theme, `Theme.Material.Light` with `android:windowBackground` set to white, is 11003.0 ms. Its `rendered` line is before `Displayed` on every run, so the metric did not fire on an empty window. The theme was removed after the measurement. The screenshot of that build shows `Counter 0`, `Name`, `SAVE`, `Enter a name`, with the system dialog on top: `evidence/startup-splash.png`. The NativeAOT shot from the first series on this boot shows `Counter 0`, `Name`, `Save`, `Enter a name`, with the same dialog: `evidence/startup-nativeaot.png`.

`InvariantGlobalization` (17845.0 ms) and the profiled remeasure just after it (18335.5 ms) are close. The shipping project was left on the SDK profiled-AOT defaults. `NativePocAotProfile` is an optional item so a later publish can pass a profile; the default publish does not set it.

Beating 1101 ms is **UNVERIFIED**. KVM did not start. The lowest measured median on the emulator that did start is 10097.5 ms.

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

**(a) file save, Roslyn compile, transfer, load, render.** Clock starts before the write and stops when the post-render reply is read. Pid 15262. Milliseconds: 758.4, 52.8, 24.2, 23.6, 22.0, 23.7, 18.2, 16.7, 17.0, 17.1, 15.2, 17.4, 18.5, 15.4, 14.5, 17.6, 16.1, 16.7, 24.7, 16.4. Cold 758.4. Sorted center pair 17.4 and 17.6, so p50 of all 20 is **17.5 ms**. p50 of the 19 warm samples is **17.4 ms**. Min 14.5, max 758.4. Those samples are the TCP listener. The file that held them is now `evidence/hot-reload-a-tcp.log`. `evidence/logcat-hotreload-a.log`.

**(b) send, load, and render of a DLL compiled before the clock.** The 20 DLLs were compiled, then the process was restarted, then each sample timed only `Send` through the post-render reply. The file write in this mode sits before the clock. Pid 15856. Milliseconds: 99.6, 47.8, 8.3, 8.4, 10.2, 5.6, 6.7, 5.8, 5.8, 5.6, 5.5, 5.4, 5.5, 5.8, 5.7, 5.8, 5.6, 5.9, 13.5, 6.3. Cold 99.6. p50 of all 20 is **5.8 ms**. p50 of the warm 19 is **5.8 ms**. Min 5.4, max 99.6. Those samples are the TCP listener. The file that held them is now `evidence/hot-reload-b-tcp.log`. `evidence/logcat-hotreload-b.log`.

**(c) compile only.** In-memory Roslyn emit, no file write, no socket, first compilation in that host process. Milliseconds: 406.5, 10.5, 8.4, 7.4, 7.0, 6.1, 6.8, 6.6, 6.9, 7.0, 7.4, 7.5, 6.7, 6.1, 6.5, 6.6, 6.4, 6.6, 6.7, 6.7. Cold 406.5. Sorted center pair 6.7 and 6.8, so p50 of all 20 is **6.75 ms**. The tool's one-decimal line prints `p50-all-ms=6.7`. p50 of the warm 19 is **6.7 ms**. Min 6.1, max 406.5. Those samples are the TCP-listener host. The file that held them is now `evidence/hot-reload-c-tcp.log`.

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

This subsection describes the tree that still used `TcpListener`. It is not the current loader. The current loader is the cnative section at the top: fixed abstract name `npoc-hotreload`, private file token, handshake check.

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
  AndroidManifest.xml  application label only; the SDK still adds INTERNET to Debug
  MainActivity.cs      holds the render delegate, logs RuntimeFeature
  AndroidHost.cs       LinearLayout, TextView, Button, EditText, ClipboardManager
  HotReload.cs         DEBUG MetadataUpdateHandler, calls RequestRender
  HotReloadServer.cs   DEBUG localabstract:npoc-hotreload, file token, handshake, collectible AssemblyLoadContext
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

Debug in-place reload, after that deploy. The process generates the token itself. There is no launch extra. The socket is `localabstract:npoc-hotreload`. The handshake checks the token. The token is a per-process bearer value, Debug only. The app writes it to the private file `files/nativepoc-hotreload.token`. The host keeps the same value in its temp file, `nativepoc-hotreload.token` under `Path.GetTempPath()`, and forwards `tcp:18765` to `localabstract:npoc-hotreload`. It compiles `src/NativePoc.Screen/Screen.cs` with the SDK Roslyn compiler and sends the DLL. `AppSession` stays in the already-loaded `NativePoc.Core` assembly. Release does not compile the listener. The supported edit path for acceptance items 7–10 is `cnative run android`. `dotnet watch` restarts the app on this workload. That is a known limitation.

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
| 7–10. `cnative run android`, n=20, p50 ≤ 1500 ms, same pid, `Ada` kept | **Pass** on the merged smoke, the `73dc6e2` series, and persona B. **UNVERIFIED:** frame time to pixels. A redeploy drops the typed name (**RUN**, pid 20726) | n=20 on pid 11096, edit-to-screen p50 59.8246 ms / p90 74.8963 ms, `Ada` kept. [docs/devx/comparison.md](../docs/devx/comparison.md), [docs/devx/evidence/fixed-socket-percentiles.txt](../docs/devx/evidence/fixed-socket-percentiles.txt). Merged-main smoke: pid 19193. Persona B: pid 20410 kept `Rio`, dirty relaunch pid 20904. `evidence/cnative-smoke-main.log`, `evidence/persona-b-clean.log`, `evidence/persona-b-dirty.log`. `dotnet watch` is a known restart, not these items. |
| 11. Rude edit | **Watch restarts. The in-place loader refuses a shape change and keeps the process.** | `evidence/dotnet-watch-rude.log` logs `Stopping: com.scratch.nativepoc` and the `dotnet` host process ids 11554, 11652, 11724, 11829, 11920, and 12024. It does not contain app pids 9931 or 10056. The screen-DLL shape change on pid 15856 returned `MissingMethodException` for `AppState.get_Extra()` and left `Rebound 1` / `Ada` on screen. That trial did not type into the field after the exception. The later trial that does is in the review section. |
| 12. Release Mono AOT installs and items 1–6 pass | **Pass on android-x64** for the 464b63e rebuild. The 20-handle count was not repeated on this process | Pid 13736: widgets, `Ada` with handle 14758 across three keystrokes, Save, rotation, clipboard. Ten cold starts, p50 1398 ms. **Not run** on arm64. |
| 13. `libaot-*.so` in the arm64 APK | **Pass** | `lib/arm64-v8a/libaot-NativePoc.Core.dll.so`, `libaot-NativePoc.Android.dll.so`, `libmonosgen-2.0.so`, `libmonodroid.so`. No `libNativePoc.Android.so`. `evidence/apk-listings.txt`. APK 3606628 bytes. |
| 14. No trim/AOT warnings in this repo's source | **Pass** | Debug build `0 Warning(s)`. Release Mono logs have no `warning` lines. NativeAOT's only warning is IL3053 on `Mono.Android.dll` (workload). |
| 15. APK smaller than `dotnet new maui` | **Pass** on android-x64, workload 36.1.118 | PoC Release Mono AOT 3795146 bytes. MAUI template Release 19074917 bytes. Same emulator. See the review section. |
| 16. Startup median ≤ MAUI, 10 runs | **Not met** on KVM. **UNVERIFIED** against 1101 ms in the TCG pass | KVM: PoC p50 1517.5 ms, MAUI p50 1101 ms. Ten `COLD` runs each, none dropped. The PoC median is higher. TCG could boot after KVM faulted. Lowest TCG median is NativeAOT 10097.5 ms. See "Release cold start". |
| 17. NativeAOT publish, XA1040 present | **Partial** | Older publishes and the review publish exit 0. `rg -n XA1040` exits 1 on `evidence/nativeaot-arm64.log`, `evidence/nativeaot-x64.log`, `evidence/nativeaot-x64-rebuild.log`, and `evidence/nativeaot-x64-review.log`. IL3053 does appear. The review APK was installed later: `IsDynamicCodeSupported=False`, `IsDynamicCodeCompiled=False`, ten TCG cold starts, p50 10097.5 ms on the second series. |
| 18. NativeAOT runs items 1–4 | **Pass on android-x64** for the 464b63e rebuild | Pid 14459: `Supported=False Compiled=False`, widgets, `Ada` with handle 12006 across three keystrokes, Save `clipboard-ok poc-1`, rotation kept `Counter 1` / `Ada`. Ten cold starts, p50 1298 ms. The earlier pid 12212 empty-save and the 20-handle series were the pre-split publish. **Not run** on arm64. |
| 19. NativeAOT failed | **Does not apply** to the x64 run | The arm64 link failed once, before the NDK was installed. After `ndk;26.3.11579264` the link succeeded. |

## In-place hot reload

Items 7–10 are `cnative run android`. They pass on the series in [docs/devx/comparison.md](../docs/devx/comparison.md), the merged-main smoke in `evidence/cnative-smoke-main.log`, and persona B in `evidence/persona-b-clean.log`. Frame time from `RenderNow` to the next scanned framebuffer stays unverified. A redeploy drops the typed name; that was run on pid 20726. `dotnet watch` restarts the app on this workload. That is a known limitation, and it is not the gate.

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

- `dotnet watch` restarts the app on this workload. That is a known limitation. Items 7–10 are `cnative run android` and pass. The framebuffer lag stays unverified. A redeploy drops the typed name; persona B ran that drop. The Debug loader is a separate measurement: `localabstract:npoc-hotreload`, a per-process bearer token in the host temp file, and a forward of `tcp:18765`. An `AppState` shape change fails in place with `MissingMethodException`, the previous delegate is restored, and a later keystroke renders the previous screen. The new Release APK does not declare `INTERNET`. The Debug manifest still does, because the SDK adds it.
- Item 15 passed on this emulator. Item 16, on the earlier KVM boot, did not for profiled Mono AOT: p50 1517.5 ms against the MAUI template p50 1101 ms. That KVM then faulted in `kvm_arch_vcpu_create`. The TCG pass is withdrawn. On the KVM boot in the section above, the NativeAOT default's 10-run p50 is under the MAUI p50 measured immediately after it.
- arm64 Mono AOT and arm64 NativeAOT were built and not installed. This host's emulator image is x86_64. The review NativeAOT x64 APK was installed on the TCG emulator.
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
- `evidence/hot-reload-a-tcp-cold.png` — TCP-listener end-to-end sample 0, pid 15262, `A00 1`, `Ada`
- `evidence/hot-reload-a-tcp-warm.png` — TCP-listener end-to-end sample 19, same process, `A19 1`, `Ada`
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
- `evidence/startup-nativeaot.png` — NativeAOT on the TCG emulator, `Counter 0`, `Name`, `Save`, `Enter a name`, with the system "isn't responding" dialog over it
- `evidence/startup-splash.png` — profiled AOT with the temporary white window theme, `Counter 0`, `Name`, `SAVE`, `Enter a name`, same dialog. The theme was removed after the ten runs
