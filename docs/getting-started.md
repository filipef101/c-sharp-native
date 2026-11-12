# Getting started

From an empty folder, through the template, `cnative run android`, one hot-reload edit, `cnative inspect`, and `tools/check.sh`. The commands below were run on this machine. The emulator was `emulator-5554`, API 34, started with `-gpu swiftshader_indirect`. This page did not boot an emulator. It did not run on a phone, on iOS, or on a Mac.

The folder was empty. The machine already had the .NET 10.0.403 SDK, the `android` workload, a JDK, `adb`, and the IL compiler package. The times are wall clock on that machine.

## What the machine had

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
```

## A clean folder

The packages stay in a local folder. Nothing in this run was pushed to nuget.org. The feed path is absolute. A relative `nuget.config` entry would not resolve from the new directory.

```sh
bash tools/pack/pack-local.sh /tmp/p41/packages
dotnet new install /tmp/p41/packages/NativePoc.CnativeApp.0.3.0.nupkg
dotnet new cnative-app -o /tmp/p41/clean/hello --feed /tmp/p41/packages
dotnet tool install NativePoc.Devx.Cli --tool-path /tmp/p41/tools --add-source /tmp/p41/packages
```

This run, seconds: pack 10.39, template install 0.356, `dotnet new` 0.53, tool install 0.59.

`/tmp/p41/clean/hello` has `cnative-app.csproj` and `screens/HomeScreen.cs`. The package id is `com.scratch.nativepoc`. Uninstall that package first when a repo build is already installed. This run ran `adb -s emulator-5554 uninstall com.scratch.nativepoc` before launch.

## Doctor

From the new directory:

```sh
/tmp/p41/tools/cnative doctor
```

That took 0.306 s and printed:

```text
OK dotnet: 10.0.403
OK android workload: installed.
OK adb: adb
OK device: emulator-5554
OK NativeAOT: /home/ubuntu/.nuget/packages/microsoft.dotnet.ilcompiler
```

`doctor` does not build or install the app.

## Run

A directory with `cnative-app.csproj` is a template app. `cnative run android` publishes the Debug NativeAOT listener for the device ABI, installs it, and watches `screens/`.

```sh
/tmp/p41/tools/cnative run android --device emulator-5554
```

Wall clock from that command to `cnative: watching` was 25.085 s. The log shows a restore (280 ms), `Generating native code`, `ilc-stamp`, the launch, and the token read. The activity was `com.scratch.nativepoc/crc64e3b30822abfd5217.MainActivity`, pid 19670. The log also printed `warm lower 34.5 ms` and `watcher-wait 44 ms`.

## Hot reload

In `screens/HomeScreen.cs`, change the `Title` initializer from `"Hello"` to `"Hiya"` and save. This run logged, still pid 19670:

```text
reloaded in 105.0 ms (lower 1.1 ms, apply 86.7 ms)
Hiya program=1 present=1
```

The watcher printed no `state kept` line. The script that wrote the file saw those two lines 1.032 s later. That wall clock includes a 1 s poll. The reload time is the `reloaded in` line.

## Inspect

```sh
/tmp/p41/tools/cnative inspect screens/HomeScreen.cs
```

Exit 0 in 0.105 s. `lint` was empty. The heading row was `home/title`, role `text`, text `Hiya`. The title style is `FontSize` 22, which is the shared heading floor.

## tools/check.sh

Stop the watcher before another publish. This run stopped it, then started the check with the working directory `/tmp/p41/clean`:

```sh
bash tools/check.sh
```

The script changes to the repo from its own path. Exit 0 in 22.307 s. core 87 passed, devx 152 passed, packing 14 passed, iOS managed-host 6 passed, inspect clean, and the mac-bench, scroll-bench, cold-start, guest-env, and portable self-tests passed. `passed=11 failed=0`, `check: pass`. Those self-tests ran on Linux. `mac-bench.sh --self-test` and `scroll-bench.sh --self-test` from `d3c8487` passed on macOS `/bin/bash` 3.2. This page has no new Mac timing.
