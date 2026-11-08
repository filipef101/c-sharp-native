# Native PoC

A MAUI-free .NET 10 Android app. A small C# element tree (`Column`, `Text`, `Button`, `TextField`) is reconciled onto real Android views. It is a proof of concept, not a framework.

The plan is [research/runtime-feasibility.md](research/runtime-feasibility.md). What was built, measured, and left unverified is [research/android-poc-report.md](research/android-poc-report.md).

## Requirements

- .NET SDK 10.0.403 (`global.json`)
- The `android` workload (`dotnet workload install android`)
- Android SDK 36, build-tools 36.0.0, and an `x86_64` system image if you want the emulator
- `ANDROID_HOME` pointing at that SDK, and a JDK (this machine used OpenJDK 21)

## Test

```bash
dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj
```

These tests run on the host. They cover the diff and the in-memory reconciler, including state kept across a new host.

## Debug, on an emulator or a device

```bash
dotnet build src/NativePoc.Android/NativePoc.Android.csproj \
  -c Debug -f net10.0-android -r android-x64 -t:Run -p:AdbTarget=-e
```

Use `android-arm64` for a phone. A plain `adb install` of the Debug APK aborts: Debug uses fast deployment, and the assemblies are pushed by `-t:Run`.

## Dev loop

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"
export JAVA_HOME="$(dirname "$(dirname "$(readlink -f "$(command -v javac)")")")"
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
tools/devx/cnative run android
```

The emulator or device has to be online already. This command does not boot one. It builds Debug, installs, launches, and watches `src`. Saving `src/NativePoc.Screen/Screen.cs` reloads that screen in place and keeps the process. Saving `src/NativePoc.Core` or `src/NativePoc.Android` prints `rude edit -> restart required` and asks before a rebuild. `reloaded in` is the whole push. `apply` is the device exchange. A compile error stays in the terminal as `file:line:col` and the app keeps the previous screen. Pass `--restart-on-rude` to skip the question.

The `NativePoc.HotReload.Host` modes below are 20-sample benchmarks. Mode `a` restarts the app, types its own name, and rewrites `evidence/hot-reload-a.log`. It is not the edit loop.

Debug in-place reload, after that deploy. The process starts the listener with no launch extra. It listens on `localabstract:npoc-hotreload` and checks the token in the handshake. The token is a per-process bearer value, Debug only. The app writes it to the private file `files/nativepoc-hotreload.token`. The host keeps that value in its temp file, `nativepoc-hotreload.token` under the temp directory, and forwards `tcp:18765` to `localabstract:npoc-hotreload`. The token is not logged. A new process mints a new token. State stays in `NativePoc.Core`, which is already loaded. Release does not compile the listener. Acceptance items 7–10 are `cnative run android`. They pass on the series in `docs/devx/comparison.md`, the merged-main smoke, and persona B. Frame time to pixels stays unverified. A redeploy drops the typed name.

```bash
tools/devx/cnative run android --restart-on-rude
```

```bash
dotnet build tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
dotnet tools/NativePoc.HotReload.Host/bin/Debug/net10.0/NativePoc.HotReload.Host.dll a
```

`a` is a benchmark that restarts the app: file write plus compile plus transfer plus render. `b` is send plus render of a DLL compiled beforehand, and `c` is compile only. Each mode records 20 samples. `a` is not the edit loop.

`dotnet watch` restarts the app on this workload. That is a known limitation. It is not the hot reload path.

## Release

Release publish is NativeAOT. `PublishAot` and `EnablePreviewFeatures` are set when `Configuration` is `Release`.

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64
```

`IL3053` comes from three `IL3050` sites inside `Mono.Android` (`MakeGenericType`, `MakeArrayType`, and `DynamicMethod`). `NoWarn` lists `IL3053` for the whole project. ILC's `nowarn` list is not per assembly, so that suppression is not limited to `Mono.Android.dll`. `WarningsNotAsErrors` is `XA1040` only. That list does not keep warnings from this repo visible: a code there is printed and does not fail the build. An `IL3050` in this repo stays `IL3050` because single-warn is off for the app and its project references, and `IL3050` is not in `NoWarn`, so the publish fails. The Android NDK (26.3.11579264 on this SDK) has to be installed or the native link fails looking for `libc++_static.a`.

Mono AOT is the fallback. `-p:PublishAot=false` turns on `RunAOTCompilation` and the SDK's profiled AOT profile. Full AOT is that plus `-p:AndroidEnableProfiledAot=false`. To compile against a different profile, pass `-p:AndroidUseDefaultAotProfile=false -p:NativePocAotProfile=/path/to/file.aotprofile`. The signed Mono APK contains `libmonosgen-2.0.so` and `libaot-*.dll.so`.

`tools/measure-arm64-startup.sh` measures cold start for the four signed `android-arm64` Release APKs (empty MAUI template, profiled Mono AOT, full Mono AOT, NativeAOT) once they sit in the same directory. Run it with `sh measure.sh` on the Mac. It uses `adb` only. Set `ADB_SERIAL` when more than one device is attached. The copies built on workload `android` 36.1.118 are the files next to that script in the artifact directory. Their package and activity names, and the fact that they have not been timed, are in the report.
