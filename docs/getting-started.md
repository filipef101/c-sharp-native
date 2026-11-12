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

The JNI list, images, scroll bench, and accessibility pass are in [devx/evidence/jni-list.txt](devx/evidence/jni-list.txt). That run did install the Release feed APK on the same emulator. The Java list floor, lazy Release rows, the R8 dex, view animations, the feed first frame, navigation, and location are in [devx/evidence/scroll-floor.txt](devx/evidence/scroll-floor.txt).

## A new app, without publishing

The packages stay in a local folder. Nothing in this run was pushed to nuget.org. The commands were run on this machine. The feed path is absolute. A relative `nuget.config` entry would not resolve from the new directory.

```sh
bash tools/pack/pack-local.sh /tmp/cnative-packages
dotnet new install /tmp/cnative-packages/NativePoc.CnativeApp.0.3.0.nupkg
dotnet new cnative-app -o /tmp/cnative-clean/hello --feed /tmp/cnative-packages
dotnet tool install NativePoc.Devx.Cli --tool-path /tmp/cnative-tools --add-source /tmp/cnative-packages
```

`/tmp/cnative-tools/cnative doctor` printed the same OK lines as the repo `doctor` above.

The new directory is `/tmp/cnative-clean/hello`. It has `cnative-app.csproj` and `screens/HomeScreen.cs`. The package id is `com.scratch.nativepoc`, so uninstall that package first if a repo build is already installed. From that directory:

```sh
/tmp/cnative-tools/cnative run android --listener --device emulator-5554
```

That publishes the Debug NativeAOT listener for the template (`-c Debug -f net10.0-android -r android-x64 -p:NativePocDevListener=true`, without `NativePocHttp` or `NativePocRecycler`) and watches `screens/`. This run launched pid 8012, activity `crc64e3b30822abfd5217.MainActivity`. The first push logged `reloaded in 451.8 ms (apply 409.5769 ms)` and `Hello program=1 present=0`. `cnative agent press home/add --running --device emulator-5554` left `Count=1`. Changing the `Title` initializer from `"Hello"` to `"Hiya"` logged `reloaded in 16.8 ms (apply 14.4416 ms)`, same pid, and `state kept Count=1 (initializer 0), Title="Hello" (initializer "Hiya")`. The tree was still `stack 0 counter` and `stack 1 home`, and the count line was `value="Count 1"`.

Stop the watcher before the Release publish.

```sh
dotnet publish -c Release -f net10.0-android -r android-x64
dotnet publish -c Release -f net10.0-android -r android-arm64
```

The signed APK is `bin/Release/net10.0-android/<rid>/publish/com.scratch.nativepoc-Signed.apk`. The x64 APK is 2,937,385 bytes, sha256 `e9d7b02f2f73dca0bf7553d532b25009fa41d1e845c2d416efa7a75ed8c08bdb`. It was installed on emulator-5554 and cold-started. Logcat showed `nav-back registered depth=2` and `title=Hello`. The manifest has `INTERNET`, `VIBRATE`, and `enableOnBackInvokedCallback`. It does not have a location permission. The arm64 publish was built and not run. APK 2,777,644 bytes, sha256 `05d348ebc767d661900378311a1fe938396082301a5229346bd0703e5b0cccfe`. Dex 47,764 sha256 `62bb0763783492c753733eef2b4c749722bad1ecf7af3482e525f47e4c997a75` on both. x64 `.so` `libCnativeApp.so` 6,780,856 sha256 `6c8c916511e2ef8ea292b3c9abeb0e9501d5560e51e6fd59f08366b06a1f1ec4`. arm64 `.so` 7,003,048 sha256 `5d6e96c4aa5de3f07e3d37e552a6baea53148968493d3f940c5e6beee867660a`.
