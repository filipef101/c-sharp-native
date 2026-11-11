# Getting started

A clean clone, a new screen, the NativeAOT dev listener on an emulator that is already online, and a hot reload of that screen in the same process.

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
tools/devx/cnative run android --listener
```

That publishes the Debug NativeAOT listener (`dotnet publish -c Debug -f net10.0-android -r <rid> -p:NativePocDevListener=true`) and installs it on the online emulator. Leave it running. The process log contains `IsDynamicCodeSupported=False`. After the host screen and the packing screen, it lowers every `samples/<name>/<Name>Screen.cs` from `cnative new` and shows it. The log line is `cnative: show samples/hello/HelloScreen.cs`. When several of those files exist, path order decides which one is on top.

`cnative run android` without `--listener` is the Debug build (`-t:Run` in `AndroidBuild.DebugRun`). It does not publish NativeAOT. The reload below was the listener.

Change the `"Count "` literal in `samples/hello/HelloScreen.cs` to `"Taps "` and save. The watcher lowers that file and applies it in the same process. The log line is `cnative: reload samples/hello/HelloScreen.cs`, then `reloaded in`. The heading is `state.Title`. A reload keeps a bag value when the name and type are still there, so changing the `"Hello"` initializer does not change a heading the screen is already showing.

`samples/NativePoc.Packing/PackingScreen.cs` and `src/NativePoc.Screen/Screen.cs` reload the same way. Only `src/NativePoc.Screen/Screen.cs` goes through the resident compiler, and that compiler's output is lowered before it is applied. A generated screen and the packing screen are lowered as programs. Other files need a rebuild.

An edit the program cannot carry prints `path:line:column: id: message` and `The edit was not applied. The last program stays until relaunch.` `--restart-on-rude` relaunches the installed process, restores state, and applies the last program that lowered. It does not publish again. A Core or Android edit is rebuilt first. The ids are in [screen-program-subset.md](screen-program-subset.md).

The listener run that checked these commands is [docs/devx/evidence/listener-new-screen.txt](devx/evidence/listener-new-screen.txt).

## Tests and the Android build

These do not start an emulator. The Android build needs `ANDROID_HOME` and the workload. It does not need a device. [testing.md](testing.md) is the headless screen API, which also needs no device.

```sh
dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj -c Release
dotnet test tests/NativePoc.Packing.Tests/NativePoc.Packing.Tests.csproj -c Release
dotnet test tools/devx/NativePoc.Devx.Cli.Tests/NativePoc.Devx.Cli.Tests.csproj -c Release
dotnet build src/NativePoc.Android/NativePoc.Android.csproj -c Debug -f net10.0-android
```

A GitHub Actions workflow for those commands is pending. It is not in this tree yet.
