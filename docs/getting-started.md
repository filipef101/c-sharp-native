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

`/tmp/p44/clean/hello` has `cnative-app.csproj` and `screens/HomeScreen.cs`. The package id is `com.scratch.nativepoc`. This tree's template writes `com.scratch.` plus the output folder name, and `cnative run` reads that `ApplicationId`. Uninstall that package first when a repo build is already installed. This run ran `adb -s emulator-5554 uninstall com.scratch.nativepoc` before launch. That generated project had `NativePocNav` false and `NativePocPersist` false. This tree's `dotnet new cnative-app` defaults `NativePocNav` to true, because the screen calls `Push`, and leaves `NativePocPersist` false. `NativePocRecycler` stays false.

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

`Theme.Mode` takes `dark`, `light`, or `system`. `system` follows `uiMode`. The call stores the mode and repaints. It does not recreate the activity. Text is `sp`, so the system font scale grows a line instead of clipping it. `cnative inspect` flags `fixed-height` when a vertical text container uses `Grow` or when the measured height does not grow at font scale 2, `overflow` when a child extends outside its parent, `system-bar` when a child sits in the top 24 dp, and `button-label` when a button is taller than 56 dp.

The notes and visit stills for light, dark, and font scale 2.0 are artifacts, not files in this repo: `/opt/cursor/artifacts/progress45/notes-light.png`, `notes-dark.png`, `notes-font2.png`, `visit-light.png`, `visit-dark.png`, and `visit-font2.png`. Northroom, the showcase, uses the same nav, transition, theme, and font scale. Its stills and clip are `/opt/cursor/artifacts/progress49/showcase/`. The hot-reload clip, taken while Harbor bowl stayed kept, is `/opt/cursor/artifacts/progress50/showcase/northroom-reload.mp4`, and the edited home is `/opt/cursor/artifacts/progress50/showcase/northroom-after.mp4`. A later clip at screenrecord frame rate, with the title change on that kept piece and the edited home, is `/opt/cursor/artifacts/progress51/showcase/northroom-reload.mp4`. The README records the emulator-run sizes and the cold start. This page does not add a timing.

## Showcase floor

Northroom's cold-start delta uses one floor. It is an empty native column, package `com.scratch.nativepoc.widgetfloor`, screen id `floor`. The publish is the showcase Release with the same flags plus `-p:NativePocWidgetFloor=true`, which keeps the Northroom screens in the binary and launches the empty column instead of home. The Java list floor is a different app and is not this comparison.

The emulator is the one already running in this workspace: serial `emulator-5554`, AVD `poc34`, API 34, `x86_64`, 1080x2400, density 420, SwiftShader. It was started as `emulator -avd poc34 -accel on -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-save -memory 2048 -cores 2`.

```sh
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-x64 \
  -p:NativePocShowcase=true -p:NativePocNav=true -p:NativePocPersist=true \
  -p:NativePocTransition=true -p:NativePocStartupMarks=true
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-arm64 \
  -p:NativePocShowcase=true -p:NativePocNav=true -p:NativePocPersist=true \
  -p:NativePocTransition=true -p:NativePocStartupMarks=true
dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r android-x64 \
  -p:NativePocShowcase=true -p:NativePocNav=true -p:NativePocPersist=true \
  -p:NativePocTransition=true -p:NativePocStartupMarks=true -p:NativePocWidgetFloor=true
tools/bench/mac-bench.sh --device emulator-5554 --n 12 --repeat 4 --quiesce warn --out /tmp/showcase-floor \
  --run showcase:showcase.apk:com.scratch.nativepoc.showcase/crc64ee53024f7ed0aa60.MainActivity \
  --run floor:widgetfloor.apk:com.scratch.nativepoc.widgetfloor/crc64ee53024f7ed0aa60.MainActivity
```

`tools/check.sh` publishes that x64 showcase and then the arm64 showcase, prints each signed apk's size, and does not install them.

The activity class is the same on both packages. The CRC is of the C# namespace. `IlcDehydrate` stays at the Release default, true. The block delta is showcase TotalTime p50 minus floor TotalTime p50 for each of the four blocks. Hyndman-Fan type 7, bootstrap B=1000, seed 8946. The command above is the emulator config. The README records the canonical n=48 floor run on that config, TotalTime p50/p90 showcase 946.0/1031.7 and floor 647.5/748.3, block-delta median 333.2, interval 216.0–386.5, and the later attribution of that gap. In the attribution session the block-delta median was 353.2 ms, interval 305.5–400.5, and 347.2 ms of it was the wait from the first pre-draw to the launch idle handler. Creating the gradient, shadow, and outline on the callback two frames after that handler brought the same-session interval to 41.5–134.5, median 91.0.

`ReportFullyDrawn` is the first frame that already has that chrome. The log line is `phase=fully-styled`. `-p:NativePocImmediateChrome=true` is the same screens with the chrome on the first frame, package `com.scratch.nativepoc.immediate`, so the bench can install it beside the deferred showcase. On the n=48 run in the README, deferred fully-styled p50 is 930.0 ms and immediate fully-styled p50 is 1048.5 ms. The deferred launch idle is earlier than that styled frame, and the styled frame is still sooner than the immediate build. A screenrecord of the deferred build shows one unstyled frame. Setting elevation to 0 and drawing the fills on the first frame did not bring fully-styled under the deferred build, so elevation stays and the chrome stays deferred. A solid `ColorDrawable` clipped to the corner radius, with the gradient and the shadow still on that callback, also left fully-styled behind the unpainted first frame. That clipped fill is not in this tree. The first frame now paints a rectangular `ColorDrawable` with no clip and no outline, and the corner radius, gradient, elevation, and stroke still arrive on the callback. `-p:NativePocBlankChrome=true` builds the unpainted first frame as `com.scratch.nativepoc.blank`. SwiftShader magnifies clip and shadow draw cost. A host-GPU or device run is still pending. The README has the n=48 numbers.

Keep scales with `Animated.Spring` and a shelf tile scales with `Animated.Timing`. The duration follows the smaller animator and transition scale, and scale 0 applies the end value on the same call. The shelf tiles are decoded with `BitmapFactory` and `InSampleSize`, then the corner radius is baked into one view-sized bitmap. The eight glaze JPEGs are written into the intermediate output at build time and are not files in the repository. Each is a radial glaze with a highlight and two soft runs that taper. The shelf is a two-column `ListView` on `KeyedList`, with `setHasFixedSize`, item prefetch, and the baked bitmap reused when a holder comes back. The keys stay the piece ids. An edge drag tracks the current screen and reveals the one under it. The commit and the duration come from `MotionContract`, and scale 0 is instant. The back button still pops with TalkBack on. The piece screen has a Note field stored by the persist module, with autofill off, and the Keep label is centred in a 48 to 56 dp button. `samples/showcase/StressScreen.cs` is a 1,000-row keyed list with a generated thumbnail. The emulator is the one above. The README records the APK size, the scroll-bench of that grid against the Java list floor, and the three-fling jank and PSS. SwiftShader inflates the shelf draw. The earlier clip and stills stay in `/opt/cursor/artifacts/progress60/showcase/`. This run is `/opt/cursor/artifacts/progress61/`.

`NativePocStress` defaults to false. It compiles `samples/showcase/StressScreen.cs`, forces the showcase on, and sets the application id to `com.scratch.nativepoc.stress`. The measured showcase omits that screen and is 3,522,515 bytes. The stress package is 3,526,970 bytes, 8,432 bytes more native code. The growth from the 3,352,003 byte baked shelf is the recycler dex: `classes.dex` zip 23,043 to 135,242, with `androidx.recyclerview` carrying 86,814 bytes of method bytecode in the grid dex. A radius bake runs off the UI thread, and the UI thread presents one baked bitmap per frame. On the same scroll bench, round 0 guest_load 1.17, frame p50/p90/p99 were 107.8/122.7/188.4, against the previous grid p99 of 285.1. A Debug listener scrolled to the second row, edited the cedar title, and swapped the rain and field keys. The applies were 24.3 ms and 19.0 ms. Cedar stayed at [42,579][540,1119], the log was `list-first=2 saved=2 restored=0`, and `holder-new` did not run again. TalkBack on that emulator got cell labels `Harbor bowl, Mist, 1 of 8` through `Moss tile, Copper, 8 of 8`, in row-major order. `cnative inspect` adds `grid-order` and `grid-label` for a list with `Style.Across` of at least 2. The emulator config, separate from those times, is emulator-5554, AVD poc34, API 34, x86_64, 1080x2400, density 420, SwiftShader. SwiftShader inflates the shelf draw. This run is `/opt/cursor/artifacts/progress62/`.

Every Release sets `AndroidLinkTool` to `r8`. The widget floor sets `NativePocRecycler` and `NativePocImage` false, and a build without the recycler flag does not import RecyclerView. The showcase dex stayed 309,468 bytes uncompressed and 135,242 compressed, APK 3,522,515 bytes, sha256 `54d2a8897342e0d54de034a662c7ede728cc862745114fea11f5b5f5136da8b7`. The widget floor APK stayed 3,228,533 bytes, sha256 `4ca751f9a50f9ee71fb7210e6d5a7910ad51ccc68f0ac7eebeb64acf3dbb6864`, and its dex went from 60,404 / 21,565 to 59,572 / 21,223, with no `androidx.recyclerview`. The arm64 showcase was built and not installed: 3,346,390 bytes, sha256 `d29613ea1d6f95d538ee753f640b863af33c7d88cac57cddfae7ba1252525d48`. On the emulator above, the showcase opened the shelf, scrolled, and with TalkBack on still exposed `n of 8` cell labels. TalkBack was turned off before the timed runs. `tools/bench/mac-bench.sh --device emulator-5554 --n 12 --repeat 4 --quiesce warn`, n=48, all COLD, Hyndman-Fan 7, B=1000 seed 8946. guest_load_p50 is 5.8, so the home totals are same-run relatives. TotalTime p50/p90 showcase 566.0/598.1 and floor 485.5/548.5. fully-styled p50 496.0 and 340.5. Block deltas 70.0, 68.5, 80.5, 88.5, median 75.2, interval 68.5–88.5, inside the earlier 37.5–106.0 interval whose median was 58.2. The shelf deep link is the same command with `--es nativepoc_root shelf` on the showcase run. guest_load_p50 is 6.8. TotalTime p50/p90 shelf 586.5/640.7 and floor 500.5/555.5. fully-styled p50 435.5 and 347.5. Block-delta median 78.5, interval 57.5–93.5. SwiftShader magnifies the shelf draw. A host-GPU or device run is still pending. This run is `/opt/cursor/artifacts/progress63/`.

`cnative new` and `dotnet new cnative-app` write one screen: a heading, a count, a text field, the items, an Open button that calls `Push`, and an Add button. `NativePocNav` defaults to true. `NativePocPersist`, `NativePocRecycler`, `NativePocImage`, and `NativePocHttp` stay false. The verbatim commands on this machine, from an empty `/tmp/p44`, were pack 11.485 s, template install 0.374 s, `dotnet new` 0.485 s, tool install 0.539 s, and doctor 0.307 s. The run section of this page is the Debug listener. A Release NativeAOT x64 publish is `dotnet publish cnative-app.csproj -c Release -f net10.0-android -r android-x64` in the generated directory, then `adb install` of `bin/Release/net10.0-android/android-x64/com.scratch.nativepoc-Signed.apk` and `am start -W -n com.scratch.nativepoc/crc64e3b30822abfd5217.MainActivity`. The first publish of that folder failed in 2.384 s with CS0122: the host sources compiled into assembly `CnativeApp` could not see internal Core types. This tree friends `CnativeApp`. After a repack the same publish succeeded in 21.565 s. That binary returned a `ListView`. With `NativePocRecycler` false the host paints each row as text, so the field and the buttons were `TextView`s and Add did not run. The screen now returns a `Column`. Republishing that screen ran ILC in 18.007 s. The signed apk under `publish/` kept the previous native library. The OutDir apk had the new library, 2,994,729 bytes, sha256 `b8b6a228ea6f881aa0ccef875f5f62d4528ff5b86fd89cb3bc05226f16a66e07`. A later publish of 4.171 s skipped ILC and refreshed that OutDir apk. This commit copies the OutDir apk into `publish/` after the bundle is built. Installing it and starting the activity was COLD, TotalTime 713, WaitTime 753. The dump showed Hello, Count 0, an `EditText` whose hint is Item, and the Open and Add buttons. Tapping Add showed Count 1. Adding the measured commands and leaving out the failed publish and the idle between them, `dotnet new` through that cold start is 0.485 + 0.539 + 0.307 + 18.007 + 1.279 s. The emulator config, separate from those times, is emulator-5554, AVD poc34, API 34, x86_64, 1080x2400, density 420, SwiftShader (`ro.hardware.egl=emulation`). Font scale, animator scale, and transition scale were 1. TalkBack was off. SwiftShader magnifies draw cost. A host-GPU or device run is still pending. `samples/tasks` is a second app on that public API: a list, add, complete, delete, `PersistRows`, a detail `Push`, and `Theme.Mode`. `Column` takes its children as arguments. A Debug listener, `cnative run android --listener --root tasks`, pid 30677, applied an added row field in `reloaded in 25.9 ms (apply 23.1 ms)`, a heading `FontSize` of 28 in `reloaded in 20.0 ms (apply 18.3 ms)`, and a renamed key in `reloaded in 15.5 ms (apply 14.1 ms)`. The row stayed Done and the log was `state kept Saved="ok" (initializer "")`. A duplicate key logged `samples/tasks/TasksScreen.cs:66:12: subset.duplicate-key: Duplicate element key "heading". Give this child a different Key. Inside a list, use Key = item.Id.` The overlay body was that same sentence. The first `state.Draft.Normalize()` log was `subset.unknown-expression: The screen subset does not include this expression.` The message now names the call: `Not in the subset: state.Draft.Normalize(). Use a property, string concat, a comparison, or list Add/Remove.` The overlay showed that text. Declining the restart prompt printed `Fix the screen and save again. A relaunch loads this same file.` An empty button applied in `reloaded in 20.1 ms (apply 18.5 ms)` and cleared the overlay. `cnative inspect` exited 1 with `button tasks/blank has no accessibility label. Pass a label as the third argument of Button, or set AccessibilityLabel.` Those three edits were reverted. Arm64 was not built. `tools/check.sh` this run: core 113, devx 171, packing 14, iOS managed-host 7, `passed=11 failed=0`. This run is `/opt/cursor/artifacts/progress64/`.

`ThemeRuntime.Stamp` writes `Dark` and `Large` onto every screen id `ScreenNav` already knows, then `CompiledScreens.ApplyTheme` does the same for compiled state. On emulator-5554, SwiftShader, the Debug listener package `com.scratch.nativepoc`, `--root tasks`, pid 31302, the dump was Tasks, Buy milk, Add, Complete, Delete, Open, and Theme. There was no Restore button. Adding Sweep stored the list. Theme logged `storage-set key=theme chars=4` and `theme mode=dark dark=1 scale=1.0`, and the page was dark. The heading bounds were `[42,170][1038,247]`, height 77, and Add was height 126. `settings put system font_scale 2` and the app preference `font-scale` `2`, then a cold start, pid 31652, logged `storage-load key=font-scale`, `theme mode=dark dark=1 scale=2.0`, and `persist-get key=tasks chars=55`. Buy milk and Sweep were both on screen. The heading bounds were `[42,170][1038,293]`, height 123, and Add was height 252. `font_scale` was put back to 1.0. TalkBack stayed off. `platform.OnOpen(ScreenId, Open)` is that load. It runs the first time this platform renders the screen id. A later `RequestRender` and a hot reload do not run it again. `PersistRows.Encode` and `Fill` take the field list as a lambda, so the trimmer does not reflect over the record. `Persist.PutFields` stores one record of any width. `Persist.PutRecord` is still id, title, and note. Autofill is off unless a field sets `Autofill = true`. `Push` of a row uses the string property named `Id`, or the first string property when there is no `Id`. The verbatim commands, from an empty `/tmp/p65` because `/tmp/p44` already held the previous template, were pack 10.226 s, template install 0.409 s, `dotnet new` 0.485 s, tool install 0.547 s, and doctor 0.292 s. `dotnet new uninstall NativePoc.CnativeApp` was an extra 0.359 s, because that template was already installed, and the global `nativepoc.*` 0.3.0 cache was removed so restore took the new packages. The generated `ApplicationId` was `com.scratch.hello`, and the screen did not set `Autofill`. The CLI packed in that install still launched `com.scratch.nativepoc`. It now reads `ApplicationId`. After that CLI was packed again, a clean Debug listener publish of `/tmp/p65/clean/hello` reached the activity at 19.006 s: `com.scratch.hello/crc64e3b30822abfd5217.MainActivity`, `Generating native code`, `field-autofill key=draft off=1`, and the dump Hello, Count 0, Item, Open, Add. That process logged `hotreload-error IOException Address already in use` because `com.scratch.nativepoc` still held the socket, and the CLI exited 1 at 49.505 s. After `am force-stop` of that package, the same apk skipped publish and reached `cnative: watching` in 1.405 s, warm lower 37.0 ms. A Release publish was 20.915 s. `publish/com.scratch.hello-Signed.apk` and the OutDir apk were the same bytes, sha256 `96b82af4f9fa97ace2af2bad7c61a8047ca2da88d82e0e24bb2a8e414b2e51a0`, 3,007,017 bytes. The `lib/x86_64/libCnativeApp.so` inside both was sha256 `59935a73c0e4e6f71cf6e2745d6f07d44b1813b451f983d2f23ad77aa2050b0c`. Changing the title to Hello there and publishing again took 17.344 s and logged `ilc-stamp: compile`. Both apks were sha256 `babef3cc8d46aebb027a32ba21eaef0c279579de4619d7211f3c1b407a8671b2`. The library inside both was sha256 `79bfee28cf94b78482f7cec78131c5e58aea8ea0432c36c75bba07c7acea72b8`, 6,986,792 bytes. Installing that `publish/` apk was COLD, TotalTime 695, WaitTime 740, and the dump showed Hello there, Count 0, Item, Open, and Add. SwiftShader magnifies draw cost. A host-GPU or device run is still pending. Arm64 was not built. No n=48 bench. `tools/check.sh` this run: core 116, devx 173, packing 14, iOS managed-host 7, `passed=11 failed=0`. This run is `/opt/cursor/artifacts/progress65/`.

`cnative run android` in this repo, without `--listener`, exited 1 in 2.239 s and printed `cnative: This repo run needs --listener. A fast Debug build has no assemblies and the process aborts.` It did not install. Two Debug listeners then watched at once on emulator-5554. Tasks, package `com.scratch.nativepoc`, pid 1559, logged `hotreload-listen socket=npoc-com.scratch.nativepoc`, and the CLI logged `cnative: hotreload socket=npoc-com.scratch.nativepoc port=21639`. Hello, package `com.scratch.hello`, pid 1869, activity `crc64e3b30822abfd5217.MainActivity`, logged `hotreload-listen socket=npoc-com.scratch.hello`. Saving an unkeyed `Text` onto the keyed hello column logged `cnative: hotreload socket=npoc-com.scratch.hello port=22268` and `reloaded in 35.0 ms (lower 1.3 ms, apply 26.2 ms)`, `Hello program=1`. The dump was Hello, Count 0, Item, Open, Add, and unkeyed hello. In the same minute Tasks applied `samples/tasks/TasksScreen.cs` in `reloaded in 21.1 ms (apply 17.9 ms)`, edit-to-reply 64 ms, `hotreload-applied Tasks program=1`, and kept `Draft="GT "`, the saved Buy milk and Sweep list, and `Dark=true`. The dump added unkeyed tasks beside the keyed rows. `adb forward --list` had `tcp:21639 localabstract:npoc-com.scratch.nativepoc` and `tcp:22268 localabstract:npoc-com.scratch.hello`. Both pids were still 1559 and 1869 after each apply. Those two edits were reverted. `OnOpen` runs when the screen id differs from the last one this platform rendered, including the way back. An action `foreach` walks a snapshot, so `Remove` during the loop stays in the reload VM. A compiled `List<T>` foreach still needs `.ToArray()` or the enumerator throws. `PersistRows.Encode` and `Fill` follow the lambda field order. `Theme.Mode` takes `ThemeMode`. `cnative inspect` fails a button whose action or label argument is empty. A heading is `FontSize` at least `ThemeTokens.HeadingSize`, 22. Keyed children match by key. Unkeyed children match by position among the unkeyed children. A parent may mix them. The emulator config, separate from those times, is emulator-5554, AVD poc34, API 34, x86_64, 1080x2400, density 420, SwiftShader (`ro.hardware.egl=emulation`). Font scale was 1.0. TalkBack was off. SwiftShader magnifies draw cost. A host-GPU or device run is still pending. Arm64 was not built. No n=48 bench. `tools/check.sh` this run: core 117, devx 176, packing 14, iOS managed-host 7, `passed=11 failed=0`. This run is `/opt/cursor/artifacts/progress66/`.

A `foreach` whose body calls `Add`, `Remove`, `RemoveAt`, `RemoveAll`, `Insert`, or `Clear` on the list it walks is analyzer `NP264`. The fix writes `ToArray()` on that list. A `Remove` inside a button lambda is left alone. The reload VM still snapshots the rows, and the parity compile applies the same fix. Both paths leave Rope. A bare call of a local function, such as `Load();` on the piece screen, lowers that body and runs it while the screen renders. A call the subset cannot lower is a diagnostic on that call. `cnative inspect samples/showcase/PieceScreen.cs` returned the piece tree. The host forwarder and the CLI both call `HotReloadAddress`. On emulator-5554 the drop removed `tcp:18765 localabstract:npoc-hotreload` and left `tcp:21639 localabstract:npoc-com.scratch.nativepoc` and `tcp:22268 localabstract:npoc-com.scratch.hello`. `tools/check.sh` publishes the showcase for `android-x64` and then `android-arm64` and does not install either apk. The x64 signed apk is 3,542,995 bytes, sha256 `b1ffe2862d6a72adf92a3aae4855c3c31ed8fb238d02e77017e9039018f4a1e1`. The arm64 signed apk is 3,366,870 bytes, sha256 `c5f5b51c6ffe092e384e6a3838aa61aa3be843d212c54afbba54dffe32b91447`. A Mac arm64 emulator, API 36, host GLES via the Apple GPU, at `0c6a958`, loaded the host, so the totals are relatives. Shelf grid scroll pooled frame p50/p90/p99 were 31.5/57.2/101.0 ms against the Java list floor 27.8/49.3/113.2, janky median 6.7% on both. Startup home-floor delta median was 53 ms, and the shelf deep link was 71 ms. That gap was the renderer. Scroll was not re-run on SwiftShader. The emulator for the forward check, separate from those GPU relatives, is emulator-5554, AVD poc34, API 34, x86_64, 1080x2400, density 420, SwiftShader (`ro.hardware.egl=emulation`). Font scale was 1.0. Animator scale and transition scale were 1. TalkBack was off. No n=48 bench. `tools/check.sh` this run: core 117, devx 182, packing 14, iOS managed-host 7, `passed=13 failed=0`. This run is `/opt/cursor/artifacts/progress67/`.

## tools/check.sh

Stop the watcher before another publish. This run stopped it, then started the check with the working directory `/tmp/p44/clean`:

```sh
bash tools/check.sh
```

The script changes to the repo from its own path. Exit 0 in 20.384 s. core 88 passed, devx 152 passed, packing 14 passed, iOS managed-host 6 passed, inspect clean, and the mac-bench, scroll-bench, cold-start, guest-env, and portable self-tests passed. `passed=11 failed=0`, `check: pass`. Those self-tests ran on Linux. `mac-bench.sh --self-test` and `scroll-bench.sh --self-test` passed on macOS `/bin/bash` 3.2. The Mac emulator is still off. This page has no new Mac timing.
