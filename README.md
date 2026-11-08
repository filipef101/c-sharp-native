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

Debug in-place reload, after that deploy. The app generates a 16-byte token, writes it to the private file `files/hotreload.token`, and listens on `localabstract:npoc-hotreload`. A `nativepoc_hotreload` launch extra is ignored. The token is not logged. The host reads the file with `adb shell run-as` and forwards `tcp:18765` to that abstract socket. State stays in `NativePoc.Core`, which is already loaded.

```bash
dotnet build tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
dotnet tools/NativePoc.HotReload.Host/bin/Debug/net10.0/NativePoc.HotReload.Host.dll a
```

`a` is file write plus compile plus transfer plus render, `b` is send plus render of a DLL compiled beforehand, and `c` is compile only. Each mode records 20 samples.

`dotnet watch` still does not apply edits in place on this workload. See the report.

## Release

Release publish is NativeAOT. `PublishAot` and `EnablePreviewFeatures` are set when `Configuration` is `Release`.

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64
```

The workload's `Mono.Android` assembly reports `IL3053`. That warning is listed in `WarningsNotAsErrors`, so the publish still treats the repo's own warnings as errors. `XA1040` is on the same list and was not emitted on the x64 publish. The Android NDK (26.3.11579264 on this SDK) has to be installed or the native link fails looking for `libc++_static.a`.

Mono AOT is the opt-out. `-p:PublishAot=false` turns on `RunAOTCompilation` and the SDK's profiled AOT profile. Full AOT is that plus `-p:AndroidEnableProfiledAot=false`. To compile against a different profile, pass `-p:AndroidUseDefaultAotProfile=false -p:NativePocAotProfile=/path/to/file.aotprofile`. The signed Mono APK contains `libmonosgen-2.0.so` and `libaot-*.dll.so`.
