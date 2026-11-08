# Dev-loop evaluation

Independent pass over the Debug loop at `feat-cnative-kvm-evidence-025b`, commit `c449270` (`c44927002f25958cd91133fae6678933f33e6aac`). This writeup lives on `feat-ux-eval-devflow-82d2`, which is that commit plus `docs/ux-eval/`. `src/`, `tools/`, `research/`, and `evidence/` were not changed. Edits made during the session were reverted before this was written.

Personas used only the repo docs and the CLI help, then the running app:

- **A.** A C# developer new to mobile, starting at [README.md](../../README.md).
- **B.** A React Native / Expo developer, starting at [docs/devx/comparison.md](../devx/comparison.md) and `tools/devx/cnative --help`.

KVM was checked before any app command. It worked. The API 34 x86_64 emulator booted. Details: [kvm.md](kvm.md).

## Status labels for this session

### BUILT

- .NET SDK `10.0.403` (`dotnet --version`).
- Workload `android` `36.1.118/10.0.100` (`dotnet workload list`). Install log timestamps: start `2026-11-08T19:47:27Z`, `WORKLOAD_EXIT 0` at `2026-11-08T19:47:39Z`.
- Android SDK at `$HOME/android-sdk`: platform `android-36`, build-tools `36.0.0`, `system-images;android-34;google_apis;x86_64`, emulator `37.2.13.0` (build_id `16740729`).
- `tools/devx/cnative --help` built the CLI (`Build succeeded`, `0 Error(s)`, about 0.8 s) and printed usage.
- Persona A README command built and deployed Debug `android-x64` (`Time Elapsed 00:00:25.69`, `0 Warning(s)`, `0 Error(s)`).

### TESTED

No unit suite was run in this session. The host tests named in `docs/devx/comparison.md` were not re-executed.

### RUN

The API 34 x86_64 emulator, both persona flows, hot reload, a compile error, a rude edit, and recovery. Step logs are in [persona-a.md](persona-a.md) and [persona-b.md](persona-b.md). Findings are in [findings.md](findings.md). Proposed text is in [proposals.md](proposals.md) and is not applied.

### UNVERIFIED

- Whether `dotnet build -t:Run` finds `adb` from `ANDROID_HOME` alone. `platform-tools` was already on `PATH` from the emulator setup.
- Expo itself. The Expo section is public documentation, labelled as such in [expo.md](expo.md).
- The in-memory token after answering `y` to a rude restart. The CLI printed the new pid and the token line, then no further line. A later signal stopped it before another save. See persona B.
- Any reload percentile. This pass recorded individual wall times, not an n=20 series.

## What a newcomer can actually do

On a **first** launch into an app data directory with **no** leftover `files/nativepoc-hotreload.token`, `cnative run android` builds, installs, launches, and stays up. The committed `Screen.cs` then fails the hot-reload compile (`CS0246` `IReadOnlyList<>`) because the project turns on implicit usings and the hot-reload compiler does not. Adding `using System.Collections.Generic;` and saving `Screen.cs` reloads in place and keeps typed text.

On **every later launch**, including the launch after a rude restart, the CLI reads the previous token file before the new process replaces it. The startup push fails with `EndOfStreamException` and the process **exits** (`COMMAND_EXIT_CODE=1`). The watch never starts.

The README never mentions `cnative`. Its reload command is `NativePoc.HotReload.Host a`, a 20-sample benchmark. On the committed file that command restarts the app, types its own name, and exits 1 on the same `CS0246`.

## Top 5 small fixes

Exact diffs are in [proposals.md](proposals.md). None of them are applied.

1. **Before `am start`, delete `files/nativepoc-hotreload.token`.** Stops the second launch from forwarding to the previous abstract socket. Finding F1. Blocker.
2. **Add `using System.Collections.Generic;` to `src/NativePoc.Screen/Screen.cs`.** The stock screen then compiles in the hot-reload host. Finding F2. Blocker.
3. **On a failed startup push, print one sentence and keep watching.** A connection error currently ends the command, so the next save does nothing because nothing is watching. Finding F3. Blocker.
4. **Put a Dev loop section in the README.** Point at `tools/devx/cnative run android`, say the emulator must already be online, say which file reloads, and say that host mode `a` is a benchmark that restarts the app. Findings F4 and F8. Blocker for persona A.
5. **After `Restart the app? [y/N]` is declined, print `pass --restart-on-rude to rebuild and relaunch`.** Replace the stale "What is missing" bullets in `docs/devx/comparison.md` that still say the app does not write a token file. Findings F5 and F6. Major.
