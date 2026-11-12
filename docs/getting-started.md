# Getting started

From a new screen, to a hot reload in the running process, to a Release APK. The commands below were run from this repo on the x86_64 API 34 emulator `emulator-5554`. The emulator process was started with `-gpu swiftshader_indirect`. This page did not boot an emulator, and it did not run on a phone or on iOS.

## What the machine had

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
```

```sh
tools/devx/cnative doctor
```

That printed:

```text
OK dotnet: 10.0.403
OK android workload: installed.
OK adb: adb
OK device: emulator-5554
OK NativeAOT: /home/ubuntu/.nuget/packages/microsoft.dotnet.ilcompiler
```

`doctor` does not build or install the app.

## A new screen

```sh
tools/devx/cnative new hello
```

That wrote `samples/hello/HelloScreen.cs` and printed that path. A second `tools/devx/cnative new hello` exited 2 and printed that the file already exists. The generated file is not in this tree. Run the command to create it.

## Hot reload

```sh
tools/devx/cnative run android --listener --device emulator-5554 --root notes
```

That publishes the Debug NativeAOT listener (`-c Debug -f net10.0-android -r android-x64 -p:NativePocDevListener=true -p:NativePocHttp=true -p:NativePocRecycler=true`) and leaves the watcher running. On this run the process was pid 23382. Creating `hello` while that watcher was up reloaded the new file in that pid: `reloaded in 32.2 ms (apply 29.0 ms)`, `edit-to-reply 72 ms`, reply `Hello`.

Change the `"Count "` literal in `samples/hello/HelloScreen.cs` to `"Taps "` and save. This run logged `reloaded in 16.5 ms (apply 14.7 ms)` and `edit-to-reply 56 ms`. The tree line was `value="Taps 0"`.

```sh
tools/devx/cnative agent press hello/add --running --device emulator-5554
```

That printed `pressed hello/add`. The count line became `value="Taps 1"`. The pid was still 23382.

A later start of the same installed listener, pid 23544, logged `runtime IsDynamicCodeSupported=False`. The logcat buffer no longer had that line for pid 23382.

## Release APK

Stop the watcher first. A Debug listener must not be publishing while this runs. The counter Release does not pass `NativePocHttp` or `NativePocRecycler`.

```sh
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-x64
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-arm64
```

The signed APK is `src/NativePoc.Android/bin/Release/net10.0-android/<rid>/publish/com.scratch.nativepoc-Signed.apk`. The arm64 publish was built and not run. Sizes are in [devx/evidence/list-input.txt](devx/evidence/list-input.txt).

A feed Release, which includes HTTP and the RecyclerView list, adds the two properties:

```sh
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-x64 -p:NativePocHttp=true -p:NativePocRecycler=true
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-arm64 -p:NativePocHttp=true -p:NativePocRecycler=true
```

The feed arm64 publish was built and not run. The x86_64 feed APK was not installed over the listener in this unit, so it was not run either. The list measurements are the Debug listener on the emulator, in the evidence file.

The JNI list, images, scroll bench, and accessibility pass are in [devx/evidence/jni-list.txt](devx/evidence/jni-list.txt). That run did install the Release feed APK on the same emulator. The Java list floor, lazy Release rows, the R8 dex, and view animations are in [devx/evidence/scroll-floor.txt](devx/evidence/scroll-floor.txt).
