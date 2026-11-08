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

Debug in-place reload, after that deploy. The app listens on `127.0.0.1:18765`. The host forwards that port, compiles `src/NativePoc.Screen/Screen.cs`, and sends the DLL. State stays in `NativePoc.Core`, which is already loaded.

```bash
dotnet run --project tools/NativePoc.HotReload.Host/NativePoc.HotReload.Host.csproj
```

`dotnet watch` still does not apply edits in place on this workload. See the report.

## Release Mono AOT

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64
```

`RunAOTCompilation` is set for Release when `PublishAot` is not set. The signed APK contains `libmonosgen-2.0.so` and `libaot-*.dll.so`.

## NativeAOT (experimental)

```bash
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj \
  -c Release -f net10.0-android -r android-arm64 \
  -p:PublishAot=true -p:EnablePreviewFeatures=true \
  -p:TreatWarningsAsErrors=false
```

`TreatWarningsAsErrors` stays on for normal builds. The NativeAOT publish needs it off because the workload's `Mono.Android` assembly reports IL3053. The Android NDK (26.3.11579264 on this SDK) has to be installed or the native link fails looking for `libc++_static.a`.
