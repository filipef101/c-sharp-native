# C# native widgets

Experimental, MAUI-free C# UI framework. A small element tree is reconciled onto real platform widgets. The Android proof is the part that has been built and run. This repository is that proof, plus two branches that are not on `main`: a Spark AAR binding probe and an iOS UIKit spike.

Elements in `src/NativePoc.Core` are `Column`, `Text`, `Button`, `TextField`, and `Switch`. `src/NativePoc.Android` maps them onto `android.view.View`. The app id is `com.scratch.nativepoc`. Release publish is NativeAOT. Debug hot reload is `tools/devx/cnative run android`.

The longer record is [research/android-poc-report.md](research/android-poc-report.md). The plan is [research/runtime-feasibility.md](research/runtime-feasibility.md).

## Verified

These ran on a device or an emulator, and the numbers below are copied from the files named here.

**Release is NativeAOT.** `PublishAot` and `EnablePreviewFeatures` are set when `Configuration` is `Release`. An `android-x64` NativeAOT publish completed, and the signed APK was installed on a KVM emulator. The listener strings are absent from that Release image. See `evidence/nativeaot-release-scan-publish.log` and `evidence/nativeaot-release-listener-scan.log`.

**`cnative run android` reloads the screen in place.** On the fixed socket `localabstract:npoc-hotreload`, the n=20 series in [docs/devx/evidence/fixed-socket-percentiles.txt](docs/devx/evidence/fixed-socket-percentiles.txt) stayed on pid 11096. The clock is from the `Screen.cs` write until the reply line.

| Clock | p50 | p90 |
| --- | --- | --- |
| edit-to-screen | 59.8246 ms | 74.8963 ms |
| CLI edit-to-reply | 60.0 ms | 72.0 ms |

`Ada` stayed for all 20 samples. A compile error stayed on that process. The rude-edit redeploy was a new pid and the field was empty. Rows are in [docs/devx/evidence/fixed-socket-samples.tsv](docs/devx/evidence/fixed-socket-samples.tsv).

**Mac arm64 emulator cold start.** Source file: [evidence/mac-arm64-startup-run2.txt](evidence/mac-arm64-startup-run2.txt). Device `pixel_8`, API 36, n=20. Each sample is `TotalTime` from `adb shell am start -W -S`. p50 is the mean of the 10th and 11th sorted samples. p90 is the 18th sorted sample plus 0.1 times the gap to the 19th. The same file records the percentiles:

| Config | p50 (ms) | p90 (ms) |
| --- | --- | --- |
| MAUI empty baseline | 1604 | 1729 |
| Mono profiled AOT | 1533.5 | 1804 |
| Mono full AOT | 1428.5 | 1682.9 |
| NativeAOT | 1300 | 1711.3 |

NativeAOT has the lowest p50 in that file. Full Mono AOT has the lowest p90 (1682.9, against NativeAOT's 1711.3). Those APKs were built on Linux and timed on a Mac arm64 emulator. They are a different series from the x64 KVM `TotalTime` numbers in the research report. TCG startup numbers in that report are withdrawn.

**Packing sample.** `samples/NativePoc.Packing` is an offline checklist of fictional kit. There is no account and no network call. State lives in `PackingSession.Shared` for the process. [review/packing-fix/report.md](review/packing-fix/report.md) records Debug and Release NativeAOT on the existing KVM emulator: the switch changes packed state, system back leaves the editor, rotation keeps the process, the in-process clipboard round-trips, and the first row sits below the action bar.

## Unverified

- Time from render to the next scanned framebuffer.
- `View.Dispose` observed on an `android.view.View`.
- The packing sample on arm64. The landscape list does not scroll, so a later row can sit below the fold. Force-stop drops process state and restores the seed list. `haptic-ok` is the vibrator API; physical vibration was not measured. The emulator image has no shell clipboard reader, so the copy was checked inside the process.
- The Spark probe on arm64, and any general AAR-binding feature. The probe is one library on one branch.
- The iOS UIKit project. It was authored and was not compiled or run.
- `dotnet watch` as a reload path. On this workload it restarts the process.

## Spark AAR binding probe

Branch `feat-sparkline-aar-binding-0a93`. It is not in `main`.

`compatibility/Sparkline.Probe` is an application binding of the Apache-2.0 Spark 1.2.0 AAR (`com.robinhood.spark`), through `compatibility/Sparkline.Binding` and a typed adapter. The activity does not name a Java type. One `Metadata.xml` edit was required (`FillType` and the support-annotations package). Debug and Release NativeAOT `android-x64` both delivered a scrub callback on a KVM emulator. `src/` was not edited. The decision note on that branch, `docs/compatibility/binding-decision.md`, keeps binding in the app project. arm64 was not run.

## iOS spike

Branch `feat-ios-feasibility-8497`. It is not in `main`.

`spikes/ios/NativePoc.iOS` is a throwaway UIKit host for the same element tree (`UIStackView`, `UILabel`, `UIButton`, `UITextField`). It is not in `NativePoc.slnx`. It was authored and was not compiled or run. The Linux build in `docs/ios/linux-ios-build.txt` exits 1 before the iOS SDK loads (`Microsoft.iOS.Sdk.net10.0_26.0` is missing). A host `dotnet test` of the spike's `net10.0` checks, noted on that branch, does not compile or launch UIKit.

## Build

- .NET SDK 10.0.403 (`global.json`)
- `dotnet workload install android`
- Android SDK, a JDK, and `ANDROID_HOME`. The NDK is required for the NativeAOT link.

Host tests:

```bash
dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj
dotnet test tests/NativePoc.Packing.Tests/NativePoc.Packing.Tests.csproj
```

Debug, on an emulator or device that is already online. `android-x64` is the emulator ABI. Use `android-arm64` for a phone. Debug uses fast deployment, so a plain `adb install` of that APK aborts.

```bash
dotnet build src/NativePoc.Android/NativePoc.Android.csproj \
  -c Debug -f net10.0-android -r android-x64 -t:Run -p:AdbTarget=-e
```

Hot reload:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
tools/devx/cnative run android
```

Saving `src/NativePoc.Screen/Screen.cs` reloads that screen in the same process. Saving `src/NativePoc.Core` or `src/NativePoc.Android` prints `rude edit -> restart required`. The Debug token is a per-process bearer and is not stored in this tree. Release does not compile the listener.

Release NativeAOT:

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64
```

`NoWarn` includes `IL3053` for the whole project because three `IL3050` sites are inside `Mono.Android` (`MakeGenericType`, `MakeArrayType`, `DynamicMethod`). `WarningsNotAsErrors` is `XA1040` only. Mono AOT is the same publish with `-p:PublishAot=false`. Full Mono AOT adds `-p:AndroidEnableProfiledAot=false`.

`tools/measure-arm64-startup.sh` is the script that produced the percentile definition used for [evidence/mac-arm64-startup-run2.txt](evidence/mac-arm64-startup-run2.txt).

## Branch map

`main` is the Android proof: NativeAOT Release, `cnative`, the packing sample, and the Mac startup record. This commit adds `LICENSE` and this README on top of `feat-packing-runtime-0a93`.

| Branch | What it holds |
| --- | --- |
| `feat-android-poc-independent-review-4bad` | Acceptance note for the packing checklist |
| `feat-arm64-startup-apks-695a` | android-arm64 Release APK records and the Mac startup script, before the measured percentiles |
| `feat-cnative-android-devloop-0321` | `cnative`, then the KVM vCPU failure note |
| `feat-cnative-kvm-evidence-025b` | n=20 `cnative` reload series on the KVM emulator |
| `feat-cnative-merge-695a` | Items 7–10 on the merged `cnative` smoke |
| `feat-cnative-resident-reload-025b` | `cnative` pointed at the fixed `npoc-hotreload` socket |
| `feat-cnative-token-contract-695a` | Debug token file published where `cnative` reads it |
| `feat-ios-feasibility-8497` | UIKit spike and notes; authored, not compiled, not run |
| `feat-loader-clocks-695a` | Loader clocks retargeted at `cnative` |
| `feat-nativeaot-validate-695a` | Mac arm64 startup percentiles and the NativeAOT validation |
| `feat-packing-checklist-695a` | Element-host explainer for the packing checklist |
| `feat-packing-runtime-0a93` | Packing checklist on Debug and Release NativeAOT; parent of this README commit |
| `feat-rereview-blockers-695a` | Hot reload socket name separated from the token |
| `feat-sparkline-aar-binding-0a93` | Spark 1.2.0 app-level AAR binding probe |
| `feat-ux-eval-devflow-82d2` | Independent dev-loop evaluation |
| `feat-ux-eval-fixes-695a` | Fixes from that evaluation that still reproduced |

Sixteen feature branches, plus `main`.
