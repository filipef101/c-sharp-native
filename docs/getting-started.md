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
bash tools/pack/pack-local.sh /tmp/p44/packages
dotnet new install /tmp/p44/packages/NativePoc.CnativeApp.0.3.0.nupkg
dotnet new cnative-app -o /tmp/p44/clean/hello --feed /tmp/p44/packages
dotnet tool install NativePoc.Devx.Cli --tool-path /tmp/p44/tools --add-source /tmp/p44/packages
```

This run, seconds: pack 10.574, template install 0.399, `dotnet new` 0.566, tool install 0.597.

`/tmp/p44/clean/hello` has `cnative-app.csproj` and `screens/HomeScreen.cs`. The package id is `com.scratch.nativepoc`. Uninstall that package first when a repo build is already installed. This run ran `adb -s emulator-5554 uninstall com.scratch.nativepoc` before launch. The generated project has `NativePocNav` false and `NativePocPersist` false.

`--nav` and `--persist` are bool flags. A second empty folder, `dotnet new cnative-app -o /tmp/p44/flags/navpersist --feed /tmp/p44/packages --nav --persist`, took 0.521 s and wrote both properties as `true`. A third folder without the flags, `/tmp/p44/flags/plain`, took 0.492 s and wrote both as `false`. A Debug build of the flagged project compiled `PersistStore.cs` and `NavFocus.cs` and did not compile `FeedJson.cs`. The defines were `NATIVEPOC_NAV` and `NATIVEPOC_PERSIST`. The default listener `.so` from the run below has neither UTF-16 `persist-put` nor `nav-focus`. It is a Debug listener, so UTF-16 `dev-overlay` is present there. A Release `.so` does not have that string.

## Doctor

From the new directory:

```sh
/tmp/p44/tools/cnative doctor
```

That took 0.294 s and printed:

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
/tmp/p44/tools/cnative run android --device emulator-5554
```

Wall clock from that command to `cnative: watching` was 23.813 s. The log shows a restore (222 ms), `Generating native code`, `ilc-stamp`, the launch, and the token read. The activity was `com.scratch.nativepoc/crc64e3b30822abfd5217.MainActivity`, pid 8287. The log also printed `warm lower 35.3 ms` and, on the edit, `watcher-wait 48 ms`.

## Hot reload

In `screens/HomeScreen.cs`, change the `Title` initializer from `"Hello"` to `"Howdy"` and save. This run logged, still pid 8287:

```text
reloaded in 31.9 ms (lower 0.8 ms, apply 21.9 ms)
Hello program=1
state kept Title="Hello" (initializer "Howdy"). cnative reset-state present=1
```

The process had already written the title, so the bag kept Hello. `cnative reset-state --device emulator-5554` printed `state reset`. The uiautomator dump then showed Howdy and Count 0. The reload time is the `reloaded in` line.

## Inspect

```sh
/tmp/p44/tools/cnative inspect screens/HomeScreen.cs
```

Exit 0 in 0.096 s. `lint` was empty. The heading row was `home/title`, role `text`, text `Howdy`. The generated title style is `FontSize` 22, which is the shared heading floor.

## Nav, transitions, theme, and font scale

This section is not another timed run of the empty folder above. The template flags are the ones already measured there. `dotnet new cnative-app --nav` sets `NativePocNav` and compiles `NavFocus.cs`, which logs `nav-focus`. `Push` stacks a screen and `Pop` removes it. Android back calls the same pop. A cold launch of one screen is depth 1.

`NativePocTransition` compiles `NavTransition`, an Android `Slide`. The duration passed in is 180 ms, and the system animator scale shortens it. `animator_duration_scale` or `transition_animation_scale` at 0 skips it. That measurement is the motion package in the README, not a new launch on this page.

`Theme.Mode` takes `dark`, `light`, or `system`. `system` follows `uiMode`. The call stores the mode and repaints. It does not recreate the activity. Text is `sp`, so the system font scale grows a line instead of clipping it. `cnative inspect` flags `fixed-height` when a vertical text container uses `Grow` or when the measured height does not grow at font scale 2, `overflow` when a child extends outside its parent, and `system-bar` when a child sits in the top 24 dp.

The notes and visit stills for light, dark, and font scale 2.0 are artifacts, not files in this repo: `/opt/cursor/artifacts/progress45/notes-light.png`, `notes-dark.png`, `notes-font2.png`, `visit-light.png`, `visit-dark.png`, and `visit-font2.png`. Northroom, the showcase, uses the same nav, transition, theme, and font scale. Its stills and clip are `/opt/cursor/artifacts/progress49/showcase/`. The hot-reload clip, taken while Harbor bowl stayed kept, is `/opt/cursor/artifacts/progress50/showcase/northroom-reload.mp4`, and the edited home is `/opt/cursor/artifacts/progress50/showcase/northroom-after.mp4`. The README records the emulator-run sizes and the cold start. This page does not add a timing.

## tools/check.sh

Stop the watcher before another publish. This run stopped it, then started the check with the working directory `/tmp/p44/clean`:

```sh
bash tools/check.sh
```

The script changes to the repo from its own path. Exit 0 in 20.384 s. core 88 passed, devx 152 passed, packing 14 passed, iOS managed-host 6 passed, inspect clean, and the mac-bench, scroll-bench, cold-start, guest-env, and portable self-tests passed. `passed=11 failed=0`, `check: pass`. Those self-tests ran on Linux. `mac-bench.sh --self-test` and `scroll-bench.sh --self-test` passed on macOS `/bin/bash` 3.2. The Mac emulator is still off. This page has no new Mac timing.
