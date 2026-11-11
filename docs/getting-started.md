# Getting started

A clean clone, a new screen, a Debug build on an emulator that is already online, and a hot reload of that screen in the same process.

## What you need

- The .NET SDK named in `global.json` (10.0.403)
- `dotnet workload install android`
- A JDK (this machine uses `/usr/lib/jvm/java-21-openjdk-amd64`)
- An Android SDK at `ANDROID_HOME` with platform 36 and build-tools 36.0.0
- An emulator that `adb devices` already lists. This page does not boot one.

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
```

## A screen

From the repo root:

```sh
tools/devx/cnative new hello
```

That writes `samples/hello/HelloScreen.cs`. The name is lowercase letters and digits and starts with a letter. A second `cnative new hello` refuses because the file is already there.

```sh
tools/devx/cnative run android
```

That builds Debug (`dotnet build -c Debug -f net10.0-android -r <rid> -t:Run`) and installs on the online emulator. It does not publish NativeAOT. Leave it running.

Change the title string in `samples/hello/HelloScreen.cs` and save. The watcher lowers that file and applies it in the same process. The log line is `cnative: reload samples/hello/HelloScreen.cs`.

`samples/NativePoc.Packing/PackingScreen.cs` and `src/NativePoc.Screen/Screen.cs` reload the same way. Only `src/NativePoc.Screen/Screen.cs` reloads in place through the resident compiler, and that compiler's output is lowered to a program before it is applied. A generated screen and the packing screen are lowered as programs. Other files need a rebuild.

## Tests and the Android build

These do not start an emulator. The Android build needs `ANDROID_HOME` and the workload. It does not need a device.

```sh
dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj -c Release
dotnet test tests/NativePoc.Packing.Tests/NativePoc.Packing.Tests.csproj -c Release
dotnet test tools/devx/NativePoc.Devx.Cli.Tests/NativePoc.Devx.Cli.Tests.csproj -c Release
dotnet build src/NativePoc.Android/NativePoc.Android.csproj -c Debug -f net10.0-android
```

A GitHub Actions workflow for those commands is pending. It is not in this tree yet.
