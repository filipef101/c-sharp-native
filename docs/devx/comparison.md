# cnative

`tools/devx/cnative run android` is the Debug loop for this proof of concept. It builds `net10.0-android`, installs onto the emulator or device that is already online, launches the app without a hot-reload intent extra, reads the token with `adb shell run-as`, and then watches sources. A change to `src/NativePoc.Screen/Screen.cs` is compiled by a Roslyn compilation that stays inside the `cnative` process and pushed through `HotReloadConnection`. The socket name and the 16-byte token framing live in that type. `tools/NativePoc.HotReload.Host` is unchanged and is no longer spawned for a reload. `src/` is not modified by the tool.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export ANDROID_HOME="$HOME/android-sdk"   # platform-tools on PATH
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
tools/devx/cnative run android
tools/devx/cnative run android --restart-on-rude
tools/devx/cnative run android --device emulator-5554 --once
```

`--debounce` defaults to 40 ms. `--abi` is `android-x64` or `android-arm64` (otherwise the device ABI). `--once` pushes the current screen once and exits. `--restart-on-rude` rebuilds and relaunches instead of waiting for an answer. On a terminal, a rude edit asks `Restart the app? [y/N]`.

The Debug app has to publish the token itself. Before `am start`, `cnative` deletes the previous token files so a stale `files/nativepoc-hotreload.token` cannot be forwarded at the next process. The file it then reads is `files/nativepoc-hotreload.token` (`adb shell run-as com.scratch.nativepoc cat files/nativepoc-hotreload.token`): 32 hex characters. The process listens on the fixed name `localabstract:npoc-hotreload`. `HotReloadConnection` forwards that name to `tcp:18765` once per session. The first 16 handshake bytes are the file token. The device still closes each accepted socket after one exchange, so each push opens a new TCP connection on the forward that is already up. The CLI also accepts `files/hotreload.token`, `cache/nativepoc-hotreload.token`, any other `*.token` name in those two directories, and an `npoc-<hex>` name if `run-as` can read `/proc/net/unix`. It never passes `--es nativepoc_hotreload`.

The screen compile references the net10.0 reference pack and `NativePoc.Core.dll` read into memory, plus a generated syntax tree with the SDK implicit global usings (`System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`). The screen at `4147a5b` has no `using` of its own.

## Resident reload

### BUILT

`ScreenCompiler` keeps one `CSharpCompilation` for the watch and replaces the screen tree on each edit. `HotReloadConnection` / `HotReloadHandshake` own the abstract socket name, the adb forward, and the token-plus-length frame. `cnative` no longer references `NativePoc.HotReload.Host`. The default quiet period is 40 ms. Launch removes leftover token files before `am start`.

`dotnet test tools/devx/NativePoc.Devx.Cli.Tests` built `cnative` and the test assembly. Log: [evidence/unit-tests-resident.log](evidence/unit-tests-resident.log).

The measured series below used `tools/devx/cnative run android --device emulator-5554 --restart-on-rude`. The CLI build in that transcript is `Time Elapsed 00:00:01.09` (`0 Warning(s)`, `0 Error(s)`). The Debug `android-x64` deploy is `Time Elapsed 00:00:03.05`. Transcript: [evidence/cnative-resident.log](evidence/cnative-resident.log).

### TESTED

CLI suite from that log: `Passed: 49, Failed: 0, Skipped: 0`, duration `569 ms`. These tests do not talk to a device. They cover the committed `Screen.cs` compiling with the implicit-usings tree, a syntax error staying on `src/NativePoc.Screen/Screen.cs` and the same compiler recovering, the fixed `npoc-hotreload` name with the token still in the first 16 frame bytes, and the 40 ms default.

### RUN

Profile of the previous host `once` path, before this change, on this same emulator. The screen file was given a temporary `using System.Collections.Generic;` for the successful `once` and mode `c` only; `git checkout` restored it before the resident series. Numbers are the raw samples, not a subtraction of one sample from another. Record: [evidence/reload-profile.txt](evidence/reload-profile.txt).

- `dotnet exec` of the host with no mode (process start, no compile): `24.5`, `21.0`, `23.8` ms, then a later sample `44.8` ms.
- Host mode `c`, one fresh process, references loaded before the clock: process wall `740.7` ms. Compile sample 0 `497.9` ms. The host's own warm p50 was `8.5` ms (`min 7.9`, `max` of the 20 is the cold `497.9`).
- Host `once` on that temporary file: process wall `2770.4` ms, `once-ms=66.4`, reply `Counter 0 collectible=True`, pid `9290`.
- Host `once` on the committed file, which has no `using`: `433.4` ms and `397.1` ms, exit 1, `CS0246` for `IReadOnlyList<>`. No UI dump on that failure path.
- `adb forward --remove` plus `forward` (tcp `18766`, not the live reload port): `4.9`, `7.9`, `3.6`, `3.8`, `3.3` ms.
- `uiautomator dump`: `2100.0`, `2005.1` ms, then `2032.0`, `2011.8`, `2011.1` ms. The host calls this from `UiTexts()` after `once-ms`.
- The old `reloaded in` clock did not include the 250 ms debounce constant or the 40 ms watch poll. Those were source constants. The dump was inside the host process, so it was inside `reloaded in`.

An earlier resident launch on this boot read the previous process's token file, forwarded `localabstract:npoc-<old>`, and printed `reload failed` / `Hot reload reply failed: The hot reload connection closed early.` That launch is not in the 20. The series below deletes the token files before `am start`.

The series ran on the committed `Screen.cs`. The only edits were `TitlePrefix`, set to `R00 ` through `R19 `, and those edits were restored afterward. `git diff -- src` is empty. Before the 20, the harness tapped the name field and ran `adb shell input text Ada`. The dump then contained `Ada` and `Hello Ada` ([evidence/resident-name.png](evidence/resident-name.png), [evidence/resident-before-name.txt](evidence/resident-before-name.txt)).

Startup push, outside the 20: `reloaded in 683.3 ms (apply 105.3 ms)`, reply `Counter 0 collectible=True`, pid `9865`.

Each sample's edit-to-screen clock is `time.perf_counter` immediately before `Screen.cs` `write_text`, until the new `reply Rxx ` line is read from the `cnative` log. The harness polls that log every 2 ms. The reply is written after `RenderNow` returns. After the clock stopped, a UI dump had to contain the new title (`Rxx 0`), `Ada`, and `Hello Ada`, and `pidof` had to stay `9865`. All 20 rows passed. Rows: [evidence/resident-samples.tsv](evidence/resident-samples.tsv). Frames: [evidence/resident-reload-00.png](evidence/resident-reload-00.png) (`R00 0`, `Ada`, `Hello Ada`), [evidence/resident-reload-19.png](evidence/resident-reload-19.png) (`R19 0`, `Ada`, `Hello Ada`). From [evidence/resident-percentiles.txt](evidence/resident-percentiles.txt), `statistics.median` and `statistics.quantiles(n=10, method=inclusive)[8]`:

- edit-to-screen p50 `61.4000` ms, p90 `68.3700` ms
- `reloaded in` p50 `19.4000` ms, p90 `27.9400` ms
- apply p50 `9.3500` ms, p90 `16.4000` ms
- CLI `edit-to-reply` p50 `60.0000` ms, p90 `65.2000` ms

Sorted edit-to-screen times: `55.5, 55.7, 55.8, 56.1, 56.4, 58.6, 58.9, 58.9, 60.1, 60.9, 61.9, 62.2, 62.6, 63.1, 64.3, 64.6, 66.8, 67.0, 80.7, 88.0`.

The next edit made `TitlePrefix` syntactically invalid. The CLI printed:

```
src/NativePoc.Screen/Screen.cs:5:36: error CS1525: Invalid expression term ';'
```

`pidof` was still `9865`. A dump after `cnative` had stopped was `Native PoC | R19 0 | Ada | Save | Hello Ada`. Frame: [evidence/resident-compile-error.png](evidence/resident-compile-error.png).

The rude-edit check is a second `cnative run android --device emulator-5554 --restart-on-rude`, not the pid `9865` process. It launched pid `10585`, and adding `RudeGeneration` to `AppState` printed:

```
rude edit -> restart required
src/NativePoc.Core/Elements.cs
restarting
```

The redeploy build was `Time Elapsed 00:00:03.60`. The new process was pid `10743`, and `pidof` was `10743`. Transcript: [evidence/cnative-rude.log](evidence/cnative-rude.log). The frame taken while that pid was still up, after the activity had drawn, shows the committed screen `Counter 0`: [evidence/resident-after-restart.png](evidence/resident-after-restart.png). An earlier capture in that same pid was still the launcher splash and was replaced.

### UNVERIFIED

- The 20, the compile error, and the rude restart inside one `cnative` process. The rude restart is the second launch above.
- Time from `RenderNow` to the next scanned framebuffer. The edit-to-screen clock ends when the reply line is observed.
- The fixed socket `localabstract:npoc-hotreload` on a device run. This section's n=20 was measured against `npoc-<token>`.
- Any percentile other than the clocks in [evidence/resident-percentiles.txt](evidence/resident-percentiles.txt). The startup `683.3 / 105.3` is not in the 20.

## Fixed socket `npoc-hotreload`

Rebased onto `4147a5b` (`Separate the hot reload socket name from the token`). `HotReloadHandshake.AbstractSocketName` is `npoc-hotreload`. The first 16 frame bytes are still the file token. `src/` was not edited by this change. `origin/main` at the fetch was `e280ddc`, one commit after `4147a5b` that does not change the listener; this branch is based on `4147a5b`.

### BUILT

`dotnet test tools/devx/NativePoc.Devx.Cli.Tests` after rebuilding `NativePoc.Core` (the checked-out `AppState.PlainRoot` was not in the previous DLL): `Passed: 49, Failed: 0, Skipped: 0`, duration `458 ms`. Log: [evidence/unit-tests-fixed-socket.log](evidence/unit-tests-fixed-socket.log).

The device command was `tools/devx/cnative run android --device emulator-5554 --restart-on-rude`. CLI build `Time Elapsed 00:00:00.78` (`0 Warning(s)`, `0 Error(s)`). Debug `android-x64` deploy `Time Elapsed 00:00:08.06`. Transcript: [evidence/cnative-fixed-socket.log](evidence/cnative-fixed-socket.log).

### TESTED

The 49 tests do not talk to a device. The handshake test expects the socket name `npoc-hotreload` and a frame that still begins with the 16 token bytes.

### RUN

The same AVD `poc34` was still up. qemu pid `5338` still had `-accel on`. `sys.boot_completed` was `1`, `ro.build.version.sdk` was `34`, `ro.product.cpu.abi` was `x86_64`.

After launch, `adb forward --list` was `emulator-5554 tcp:18765 localabstract:npoc-hotreload`. The token file was 32 hex characters and that hex was not in the forward line. Startup push, outside the 20: `reloaded in 651.0 ms (apply 196.2 ms)`, reply `Counter 0 collectible=True`, pid `11096`.

The harness then typed `Ada`. The dump was `Native PoC | Counter 0 | Ada | Save | Hello Ada`. Frame: [evidence/fixed-socket-name.png](evidence/fixed-socket-name.png).

The 20 edits changed `TitlePrefix` to `S00 ` through `S19 ` on the screen from `4147a5b` and were restored afterward. `git diff -- src` is empty. Each edit-to-screen clock is `time.perf_counter` immediately before `write_text`, until the new `reply Sxx ` line is observed (log poll 2 ms). The reply is written after `RenderNow`. After the clock, the dump had to contain `Sxx 0`, `Ada`, and `Hello Ada`, and `pidof` had to stay `11096`. All 20 passed. The forward was still `localabstract:npoc-hotreload`. Rows: [evidence/fixed-socket-samples.tsv](evidence/fixed-socket-samples.tsv). Frames: [evidence/fixed-socket-reload-00.png](evidence/fixed-socket-reload-00.png), [evidence/fixed-socket-reload-19.png](evidence/fixed-socket-reload-19.png) (`S19 0`, `Ada`, `Hello Ada`).

From [evidence/fixed-socket-percentiles.txt](evidence/fixed-socket-percentiles.txt):

- edit-to-screen p50 `59.8246` ms, p90 `74.8963` ms
- `reloaded in` p50 `18.0000` ms, p90 `31.1500` ms
- apply p50 `8.6000` ms, p90 `18.1600` ms
- CLI `edit-to-reply` p50 `60.0000` ms, p90 `72.0000` ms

Sorted edit-to-screen times, displayed to 0.1 ms: `56.2, 56.2, 56.3, 57.0, 58.0, 58.3, 58.3, 58.6, 59.1, 59.8, 59.9, 59.9, 62.1, 62.4, 62.7, 63.9, 70.9, 74.9, 75.1, 96.2`. The p50 is the median of the unrounded samples.

The next edit made `TitlePrefix` syntactically invalid. The CLI printed:

```
src/NativePoc.Screen/Screen.cs:5:36: error CS1525: Invalid expression term ';'
```

`pidof` was still `11096`. The dump was `Native PoC | S19 0 | Ada | Save | Hello Ada`. Frame: [evidence/fixed-socket-compile-error.png](evidence/fixed-socket-compile-error.png).

Restoring `S19 ` printed `reloaded in 21.6 ms (apply 13.8 ms)` on pid `11096`, and the dump still had `Ada` and `Hello Ada`. That recovery is outside the 20.

Adding `RudeGeneration` to `AppState` in the same `cnative` process printed:

```
rude edit -> restart required
src/NativePoc.Core/Elements.cs
restarting
```

The redeploy build was `Time Elapsed 00:00:03.42`. The new process was pid `11820`, and `pidof` was `11820`. The forward was again `emulator-5554 tcp:18765 localabstract:npoc-hotreload`. The new process showed `S19 0` with an empty name (`Enter a name`): [evidence/fixed-socket-after-restart.png](evidence/fixed-socket-after-restart.png). `Screen.cs` and `Elements.cs` were restored after `cnative` stopped.

### UNVERIFIED

- Time from `RenderNow` to the next scanned framebuffer.
- `origin/main` `e280ddc` as the base. This run is on `4147a5b`.
- The typed name across the redeploy. Pid `11820` started with an empty `AppState`.

## E2E after the once-ms parser fix

This section records the earlier loop, which spawned `NativePoc.HotReload.Host once` for every reload. `--debounce` in that build was 250 ms.

`origin/main` `0a45484` is merged. `dotnet workload install android@36.1.118` installed android `36.1.118/10.0.100` (workload version `10.0.403`). Record: [evidence/workload-e2e.txt](evidence/workload-e2e.txt). `src/` and `tools/NativePoc.HotReload.Host` were not changed by the parser fix. The Screen and Core edits used to drive the loop were reverted; `git diff -- src` is empty.

### BUILT

`HostOutput` accepts `once-ms=<ms> bytes=<n> reply=<text>` and the older line that is only `once-ms=<ms>`. A Roslyn diagnostic with no path is printed as `src/NativePoc.Screen/Screen.cs:<line>:<col>`.

`dotnet test tools/devx/NativePoc.Devx.Cli.Tests` built the host, `cnative`, and the test assembly. Log: [evidence/unit-tests-parser.log](evidence/unit-tests-parser.log).

`tools/devx/cnative run android --device emulator-5554 --restart-on-rude` built the CLI in `00:00:01.01` (`0 Warning(s)`, `0 Error(s)`) and Debug `android-x64` in `00:00:01.57` (`0 Warning(s)`, `0 Error(s)`). The rude-edit redeploy built in `00:00:03.65`. Transcript: [evidence/cnative-e2e.log](evidence/cnative-e2e.log).

### TESTED

Host suite from that log: `Passed: 41, Failed: 0, Skipped: 0`, duration `326 ms`. The new cases use the device line `once-ms=220.1 bytes=7680 reply=Counter 0 collectible=True` and the pathless diagnostic `(5,36): error CS1525: Invalid expression term ';'`. These tests do not talk to a device.

### RUN

The same AVD `poc34` was still up. At `2026-11-08T19:34:23Z`, after the session below, `sys.boot_completed` was `1`, `ro.build.version.sdk` was `34`, `ro.product.cpu.abi` was `x86_64`, `tainted` was `0`, and qemu pid `5338` still had `-accel on`.

The command was `tools/devx/cnative run android --device emulator-5554 --restart-on-rude`. It launched `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`, printed `cnative: pid 8318`, read the token, and printed `reloaded in 3557.9 ms (apply 279.4 ms)` with `reply Counter 0 collectible=True`. That startup push is outside the 20.

For the measured edits, `Screen.cs` on disk began with `using System.Collections.Generic;` so the host could compile `IReadOnlyList<>`, which the merged screen names and the host compiles as one file without implicit usings. Each of the 20 edits changed `TitlePrefix` to `N00 ` through `N19 `. The UI dump after each edit contained that label, and `adb shell pidof com.scratch.nativepoc` stayed `8318`. Rows: [evidence/e2e-samples.tsv](evidence/e2e-samples.tsv). First and last frames: [evidence/e2e-reload-00.png](evidence/e2e-reload-00.png), [evidence/e2e-reload-19.png](evidence/e2e-reload-19.png) (`N19 0`).

The 20 `reloaded in` wall times and the 20 `apply` times, from [evidence/e2e-percentiles.txt](evidence/e2e-percentiles.txt):

- wall `reloaded in`, `statistics.median` p50 `2541.0000` ms, `statistics.quantiles(n=10, method=inclusive)[8]` p90 `2609.3100` ms
- host `apply` (`once-ms`), p50 `14.0000` ms, p90 `17.3200` ms

Sorted wall times: `2495.4, 2504.2, 2504.6, 2509.4, 2513.4, 2520.9, 2522.9, 2525.8, 2535.1, 2536.6, 2545.4, 2545.7, 2546.8, 2556.1, 2557.5, 2559.9, 2569.1, 2609.0, 2612.1, 2612.8`.

Sorted apply times: `12.7, 13.4, 13.4, 13.4, 13.5, 13.6, 13.6, 13.6, 13.8, 13.9, 14.1, 14.3, 14.4, 14.5, 15.2, 15.3, 16.1, 16.8, 22.0, 31.6`.

The next edit made `TitlePrefix` syntactically invalid. The CLI printed:

```
src/NativePoc.Screen/Screen.cs:7:36: error CS1525: Invalid expression term ';'
src/NativePoc.Screen/Screen.cs:7:37: error CS1519: Invalid token ';' in a member declaration
```

`pidof` was still `8318`. The framebuffer still showed `N19 0`: [evidence/e2e-compile-error.png](evidence/e2e-compile-error.png).

Restoring `TitlePrefix` to `N19 ` printed `reloaded in 2543.4 ms (apply 20.5 ms)` on pid `8318`. That recovery is outside the 20.

Adding `RudeGeneration` to `AppState` then printed:

```
rude edit -> restart required
src/NativePoc.Core/Elements.cs
restarting
```

The redeploy printed `cnative: pid 9290`. `pidof` was `9290`. The new frame still shows `N19 0`: [evidence/e2e-after-restart.png](evidence/e2e-after-restart.png).

### UNVERIFIED

- A 20-edit series of the committed `Screen.cs`, which has no `using System.Collections.Generic`. The series above used that using, and the using was reverted.
- A typed name kept across the 20 reloads. The greeting stayed `Enter a name`.
- Any percentile other than the two clocks on those 20 lines. The startup `3557.9 / 279.4` and the recovery `2543.4 / 20.5` are not in the 20.

## This boot of 86d70ae

Product sources are the tree at `86d70ae`. `src/` was not edited.

### BUILT

SDK and workload on this machine: [evidence/sdk-this-boot.txt](evidence/sdk-this-boot.txt). `dotnet --version` printed `10.0.403`. `dotnet workload list` printed android `36.1.2/10.0.100`.

`dotnet test tools/devx/NativePoc.Devx.Cli.Tests` built `NativePoc.HotReload.Host`, `cnative`, and the test assembly. Log: [evidence/unit-tests-this-boot.log](evidence/unit-tests-this-boot.log).

`tools/devx/cnative run android --device emulator-5554 --restart-on-rude` then built the CLI (`Time Elapsed 00:00:00.98`, `0 Warning(s)`, `0 Error(s)`) and Debug `android-x64` (`Time Elapsed 00:00:21.69`, `0 Warning(s)`, `0 Error(s)`). Transcript: [evidence/cnative-android-this-boot.log](evidence/cnative-android-this-boot.log).

### TESTED

The same host suite, from the log above: `Passed: 39, Failed: 0, Skipped: 0`, duration `361 ms`. These tests do not talk to a device. Fixture timing strings inside the suite were not remeasured on the emulator.

### RUN

KVM on this boot, kernel `6.12.113+`, `/proc/sys/kernel/tainted` was `0` before `emulator -accel-check` and still `0` after the guest reached `sys.boot_completed=1`. `dmesg` contained `0` lines matching `kernel BUG`.

`/dev/kvm` was `crw-rw----` root group `103`, and that gid is not listed in `/etc/group`. Opening it as the user returned `Permission denied`. After `chmod 666 /dev/kvm`, the same open returned a file descriptor. `emulator -accel-check` then printed:

```
accel:
0
KVM (version 12) is installed and usable.
accel
```

Exit code 0. Copy: [evidence/accel-check-this-boot.txt](evidence/accel-check-this-boot.txt).

AVD `poc34`, `system-images;android-34;google_apis;x86_64`, `hw.lcd.width=1080`, `hw.lcd.height=2400`, `hw.cpu.arch=x86_64`. Command:

```
emulator -avd poc34 -accel on -gpu swiftshader_indirect -memory 2048 -cores 2 -no-snapshot -no-window -no-audio -no-boot-anim -port 5554
```

The emulator log says `Android emulator version 37.2.13.0 (build_id 16740729)` and `Increasing RAM size to 2560MB`. The process cmdline recorded in [evidence/kvm-boot-this-boot.txt](evidence/kvm-boot-this-boot.txt) is that `-accel on` command, `qemu_pid=5338`.

The poll in [evidence/boot-poll-this-boot.txt](evidence/boot-poll-this-boot.txt) reached `sys.boot_completed=1` at its `t=36s` line, with `adb devices` showing `emulator-5554`. `ro.build.version.sdk` was `34`, `ro.product.cpu.abi` was `x86_64`, `ro.build.version.release` was `14`. During the poll, qemu `%CPU` was `70.3` and then higher (`171` on the last line). After the boot marker, `dmesg` has:

```
[  265.691581] kvm: SMP vm created on host with unstable TSC; guest TSC will not be reliable
[  267.589464] kvm: kvm [6889]: vcpu0, guest rIP: 0xffffffffab003a51 Unhandled WRMSR(0xc2) = 0xffff
```

`sysfs` at the start of the boot: `kvm.enable_virt_at_load` was `N`, `kvm_intel.nested` was `Y`.

`cnative run android --device emulator-5554 --restart-on-rude` then:

- launched `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`
- printed `cnative: pid 6379`
- printed `cnative: token read via adb run-as (files/nativepoc-hotreload.token)`
- `adb shell run-as com.scratch.nativepoc wc -c files/nativepoc-hotreload.token` printed `32`. The 32 bytes match `^[0-9a-fA-F]{32}$`. The hex value is not copied into this record.
- printed `reload failed`, then the host lines `once-ms=220.1 bytes=7680 reply=Counter 0 collectible=True`, `ui=Native PoC | Counter 0 | Name | Save | Enter a name`, and `pid=6379`
- exited `1`

The transcript has no `cnative: watching` line, so the file watch never started. `HostOutput` treats a successful apply as a line that is only `once-ms=<number>` plus a line that starts with `reply=`. The host's `once` mode prints both fields on one line (`once-ms=220.1 bytes=7680 reply=Counter 0 collectible=True`). That line matches neither pattern, `Classify` returns failed, and `Execute` returns 1 before `Watch`.

The device did apply that one startup push. Logcat on pid `6379`: `IsDynamicCodeSupported=True`, `IsDynamicCodeCompiled=False`, `rendered count=0 name-length=0 title=Counter 0`, `hotreload-listen`, `hotreload-applied Counter 0 collectible=True`. Copy: [evidence/logcat-token-launch.txt](evidence/logcat-token-launch.txt). The same log also contains three `monodroid-assembly` warnings about bundled `NativePoc.*.dll` opens.

At `2026-11-08T19:13:07Z`, after the CLI had exited, `adb shell pidof com.scratch.nativepoc` was still `6379`, `sys.boot_completed` was still `1`, and `adb forward --list` was empty. The framebuffer is [evidence/screen-after-token.png](evidence/screen-after-token.png) (1080×2400). The UI dump texts are `Native PoC`, `Counter 0`, `Name`, `Save`, `Enter a name` ([evidence/screen-after-token.xml](evidence/screen-after-token.xml)).

`once-ms=220.1` is the host send clock for that single startup push. The CLI did not print `reloaded in`.

### UNVERIFIED

The process exited before the watch, so these were not run:

- n=20 `Screen.cs` edits through `cnative run android`
- a p50 or a p90 of CLI reload times, and a pid compared across those edits
- a compile error printed as `file:line:col` with the app still alive
- `rude edit -> restart required`, and a `--restart-on-rude` redeploy whose new pid differs from `6379`

No percentile is reported. `src/` was left as it is in `86d70ae`.

## Token contract retry

The branch is rebased onto `e2d588d` (`Publish the Debug hot reload token where cnative reads it`). `git diff e2d588d -- src tools/NativePoc.HotReload.Host` is empty. `src/` was not edited here.

### BUILT

`dotnet test tools/devx/NativePoc.Devx.Cli.Tests` after that rebase: build succeeded and the tests ran. Log: [evidence/unit-tests-after-rebase.log](evidence/unit-tests-after-rebase.log).

### TESTED

Same suite: `Passed: 39, Failed: 0, Skipped: 0`, duration 297 ms. That log is the one above. These tests still do not talk to a device.

### RUN

KVM was checked before any `cnative` command. `emulator -accel-check` printed `KVM (version 12) is installed and usable`. A separate probe, `KVM_GET_API_VERSION` then `KVM_CREATE_VM` then `KVM_CREATE_VCPU`, segfaulted. Exit 139. `dmesg` shows `kernel BUG at arch/x86/kvm/x86.c:689!` in `kvm_arch_vcpu_create` for `Comm: kvmprobe` and for `Comm: qemu-system-x86`. The kernel is `6.12.113+`, tainted `D` after an earlier oops in this boot. Excerpt: [evidence/kvm-vcpu-bug.txt](evidence/kvm-vcpu-bug.txt).

AVD `poc34` was then started with `-accel on -gpu swiftshader_indirect -memory 2048 -cores 2 -no-snapshot -port 5554`. `adb devices` stayed empty for twelve polls, five seconds apart. At elapsed `01:26`, `ps` showed that qemu pid at `%CPU 0.6`. The same BUG line names `qemu-system-x86` at dmesg timestamp `2042.422682`. The process was killed. No `cnative` process was started.

### UNVERIFIED

Because the API 34 emulator never reached `adb`, these were not run:

- n=20 Screen edits through `cnative run android`, with p50, p90, and a stable pid
- a compile error printed as `file:line:col` with the app still alive
- `rude edit -> restart required` followed by `--restart-on-rude` and a new pid

No reload milliseconds are reported for this attempt. There is no new screenshot.

The earlier launch transcript and `evidence/launched.png` are from before this rebase, on the app that did not write the token file. They are not a run of the token-contract tree.

## Earlier launch, before the token file

This section is the run on `c9f5bb1`, before the rebase onto `e2d588d`. The Debug app in that tree did not write `files/nativepoc-hotreload.token`.

### BUILT

On this machine, SDK 10.0.403, workload `android` 36.1.118.

- `dotnet test tools/devx/NativePoc.Devx.Cli.Tests` built `NativePoc.HotReload.Host`, `cnative`, and the test assembly. The test log is [evidence/unit-tests.log](evidence/unit-tests.log).
- `tools/devx/cnative run android --device emulator-5554 --once` built Debug `android-x64` with `-t:Run`. The saved transcript says `Build succeeded`, `0 Warning(s)`, `0 Error(s)`, `Time Elapsed 00:00:01.53`. That run was incremental: the same command had already produced the assemblies. Transcript: [evidence/cnative-once.log](evidence/cnative-once.log).

### TESTED

Host unit tests for the CLI, not the emulator. `Passed: 39, Failed: 0, Skipped: 0`, duration 278 ms in [evidence/unit-tests.log](evidence/unit-tests.log).

- Argument parsing: `run android` defaults, `--device`, `--abi`, `--debounce`, `--once`, `--restart-on-rude`, `--help`, and rejected commands, ABIs, and debounce values.
- Debounce: events inside the window flush as one batch, a later event starts a new window, and three rapid writes of one file through `FileSystemWatcher` collapse to `Screen.cs`.
- Errors: a real Roslyn diagnostic is formatted as `Screen.cs:<line>:<col>: error CS<number>: …`. Two `CS` lines stay two lines. A host `once-ms` / `reply` pair formats as `reloaded in 24.8 ms (apply 19.3 ms)`. `MissingMethodException` is classified as a rude edit and the line is `rude edit -> restart required`. A connection failure stays `reload failed`.
- Launch arguments are `am start -n <component>` with no `--es` and no `nativepoc_hotreload`. Restart follows `--restart-on-rude` or a `y` answer, and does not prompt when the flag is set.

The 24.8 ms and 19.3 ms figures are fixture strings in the unit test, not a device measurement.

### RUN

KVM accepted a VM and a vCPU (`KVM_CREATE_VM`, `KVM_CREATE_VCPU`). That was earlier in this boot, before `kernel BUG at arch/x86/kvm/x86.c:689`. `emulator -accel-check` printed `KVM (version 12) is installed and usable` ([evidence/accel-check.txt](evidence/accel-check.txt)). AVD `poc34`, API 34, `google_apis` / `x86_64`, 1080×2400, started with `-accel on -gpu swiftshader_indirect -memory 2048 -no-window`. `sys.boot_completed=1`. `adb devices` showed `emulator-5554`. `ro.build.version.sdk` was `34`. `ro.product.cpu.abi` was `x86_64`.

`cnative run android --device emulator-5554 --once` then:

- resolved the activity as `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`
- launched it with `am start -n` and no extras
- printed `cnative: pid 8181`
- the process logged `IsDynamicCodeSupported=True IsDynamicCodeCompiled=False` and `rendered count=0 name-length=0 title=Counter 0` ([evidence/logcat-launch.txt](evidence/logcat-launch.txt))
- the framebuffer shows `Native PoC`, `Counter 0`, hint `Name`, `Save`, `Enter a name` ([evidence/launched.png](evidence/launched.png))

The same process did not publish a token. After the wait, the command printed:

```
cnative: The app did not publish a hot reload token for adb run-as. Launch extras are not used.
files/nativepoc-hotreload.token exit=1 cat: files/nativepoc-hotreload.token: No such file or directory
files/hotreload.token exit=1 cat: files/hotreload.token: No such file or directory
cache/nativepoc-hotreload.token exit=1 cat: cache/nativepoc-hotreload.token: No such file or directory
files ls=(empty)
cache ls=(empty)
unix exit=1 cat: /proc/net/unix: Permission denied
```

Exit code 1. `run-as` on this image cannot read `/proc/net/unix`, so the socket name is not available that way. The app sources on this branch are the tree at `c9f5bb1`: `HotReloadServer.Start` runs only when the `nativepoc_hotreload` intent extra is set, and nothing writes a token file. This command refuses that extra.

### UNVERIFIED

Not observed on the emulator, because the command stopped at the token:

- a Screen DLL pushed through the host
- `reloaded in … ms` from a device apply
- a compile error printed from a failed device push
- `rude edit -> restart required` from a device reply, and the automatic redeploy
- the file watch after a successful token read
- state kept across a reload (pid, typed text)

`dotnet watch` and Visual Studio Hot Reload were not run on this branch. The notes below about `dotnet watch` are the existing report, not a new measurement.

## How the loops compare

### This command

One process builds, installs, launches, and stays up to push `Screen.cs`. It compiles that file in-process with the SDK implicit usings, forwards `tcp:18765` to `localabstract:npoc-hotreload`, sends the 16 token bytes, then the DLL. On success it prints `reloaded in` for the whole push and `apply` for the device exchange. `src/NativePoc.Core` and `src/NativePoc.Android` edits are rude: the loader only swaps the screen assembly. A device `MissingMethodException`, `MissingFieldException`, `MissingMemberException`, or `TypeLoadException` is the same message. Other `.cs` files under `src/NativePoc.Screen` are not in the swapped assembly; the CLI says so and does not pretend they were included.

### Expo

Not run here. The documented loop is `npx expo start` (Metro, then `a` or `--android` to open Android) and `npx expo run:android` when the native app has to be compiled and installed. Fast Refresh applies JS and TS component edits on save and usually keeps component state. A syntax error is printed in the terminal and shown in a red box on the device. Changing a native module or the native project needs a new native build. There is no number from this machine for how long that refresh takes.

### NativeScript

Not run here. The documented one-shot is `ns run android`: build, install, launch, and watch. JS, TS, and XML edits are pushed with webpack HMR. A change that touches the native project rebuilds and restarts the app. Again, no timing from this machine.

### MAUI (`dotnet watch` / Visual Studio Hot Reload)

The report already measured `dotnet watch` on this app: the log says the project does not support Hot Reload, then `Stopping: com.scratch.nativepoc`, and the process restarts. See `research/android-poc-report.md` and `evidence/dotnet-watch.log`. That was not repeated here.

Visual Studio's Hot Reload matrix, from Microsoft's documentation, lists MAUI Android as working with the debugger attached. Rude edits (signature changes, and similar) are documented as requiring a restart. Visual Studio is not on this machine, and that path was not run. Treat it as documentation, not a result.

The in-place mechanism this CLI drives is the Debug screen-assembly swap already in the app, not `MetadataUpdater` deltas. The report's n=20 series for that swap (file-to-render p50 17.5 ms on one Debug process) is the earlier measurement of the host, not a `cnative` session.

## What is still true

- The command does not boot an emulator. Expo and `ns run android` can. An online `adb` device is required.
- Only `src/NativePoc.Screen/Screen.cs` is lowered into the in-place edit. The packing screen ships in the app as the same kind of program, rows included. A new `Element` type or an `AppState` shape change is still not an in-place edit.
- A rude restart is a full Debug `-t:Run`, so it is a new process. `cnative run android --restart-on-rude` reads the counter and the packing list before that launch and writes them back, then pushes the screen program again. Measured on the emulator: the typed name, the count, and the packing rows were still there. A force-stop outside that command still starts from the seed.
- There is no on-device error surface, no debugger, no IDE gutter, and no iOS path.
- `run-as cat /proc/net/unix` is `Permission denied` on this API 34 image. The socket name is the fixed `localabstract:npoc-hotreload`. The token file is the per-process bearer, not the socket name.
- Release and NativeAOT are outside this command. The listener is compiled only in Debug.
