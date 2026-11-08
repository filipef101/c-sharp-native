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

Debug in-place reload, after that deploy. A launcher start does not open the loader. The host generates a 16-byte token, starts the activity with the `nativepoc_hotreload` intent extra, and forwards `tcp:18765` to the abstract socket `localabstract:npoc-<hex>`. The app checks that token before it loads a screen DLL. State stays in `NativePoc.Core`, which is already loaded.

```bash
dotnet build tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
dotnet tools/NativePoc.HotReload.Host/bin/Debug/net10.0/NativePoc.HotReload.Host.dll a
```

`a` is file write plus compile plus transfer plus render, `b` is send plus render of a DLL compiled beforehand, and `c` is compile only. Each mode records 20 samples.

`dotnet watch` still does not apply edits in place on this workload. See the report.

## Release Mono AOT

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64
```

`RunAOTCompilation` is set for Release when `PublishAot` is not set. The signed APK contains `libmonosgen-2.0.so` and `libaot-*.dll.so`. The Android SDK then turns on profiled AOT with its `dotnet.aotprofile`. To compile against a different profile, pass `-p:AndroidUseDefaultAotProfile=false -p:NativePocAotProfile=/path/to/file.aotprofile`.

## NativeAOT (experimental)

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64 \
  -p:PublishAot=true -p:EnablePreviewFeatures=true \
  -p:TreatWarningsAsErrors=false
```

`TreatWarningsAsErrors` stays on for normal builds. The NativeAOT publish needs it off because the workload's `Mono.Android` assembly reports IL3053. The Android NDK (26.3.11579264 on this SDK) has to be installed or the native link fails looking for `libc++_static.a`.
