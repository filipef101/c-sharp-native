# Findings

Each item has an evidence pointer, a severity, and the smallest fix. Proposed text is in [proposals.md](proposals.md) and was not applied. Severities: **blocker** (the documented step cannot be finished), **major** (the step finishes with a wrong idea of what happened, or the docs contradict the run), **minor** (wording or noise).

## F1 — Second launch reads the previous token file

**Severity:** blocker.

**Evidence:** [persona-b.md](persona-b.md), [logs/b-stale-token-first.txt](logs/b-stale-token-first.txt), [logs/b-stale-token-relaunch.txt](logs/b-stale-token-relaunch.txt).

After a previous process had written `files/nativepoc-hotreload.token`, `cnative run android` printed `token read` and then pushed with that old token. Fingerprints: live socket `4dd3abdd500a` vs forward `dd85ad5a0e43` on the first session; previous file `025c372766bf` vs new socket `b08018f09a8c` on the relaunch. The push printed:

```
reload failed
exception=EndOfStreamException The hot reload connection closed early.
```

`WaitToken` returns on the first 32-hex `cat`. `am start` does not delete the old file, and the new process writes the new file only after `LocalServerSocket` is up. A clean `pm clear` launch matched (`3291494e3b86` on file, temp, and socket) and is the case that can reload.

**Smallest fix:** after `force-stop` and before `am start`, `adb shell run-as com.scratch.nativepoc rm -f files/nativepoc-hotreload.token`. Diff in proposals, item 1.

## F2 — Committed `Screen.cs` does not compile in the hot-reload host

**Severity:** blocker.

**Evidence:** [persona-a.md](persona-a.md) mode `a` exit 1; [persona-b.md](persona-b.md) startup line; [logs/a-host-a-first.txt](logs/a-host-a-first.txt).

`dotnet build` of the Android project prints `0 Error(s)`. The next line from the reload host is:

```
src/NativePoc.Screen/Screen.cs:31:3: error CS0246: The type or namespace name 'IReadOnlyList<>' could not be found (are you missing a using directive or an assembly reference?)
```

`src/NativePoc.Screen/NativePoc.Screen.csproj` sets `<ImplicitUsings>enable</ImplicitUsings>`. `Compile` in `tools/NativePoc.HotReload.Host/Program.cs` parses one file with no implicit usings. Line 31 of the committed screen is `IReadOnlyList<Element>`. The comparison page's UNVERIFIED list already says the measured series used a using that was reverted. A newcomer hits that gap on the first push.

Adding the using, then saving, reloaded: `reloaded in 2682.9 ms (apply 47.3 ms)`, reply `Expo 1`, pid unchanged, typed name kept.

**Smallest fix:** add `using System.Collections.Generic;` at the top of `src/NativePoc.Screen/Screen.cs`. Diff in proposals, item 2. A host that honored implicit usings would also fix the next missing global using; that is a larger change than this file needs.

## F3 — A failed startup push exits, so nothing keeps watching

**Severity:** blocker.

**Evidence:** [logs/b-stale-token-relaunch.txt](logs/b-stale-token-relaunch.txt) ends with `COMMAND_EXIT_CODE="1"` and has no `cnative: watching` line. The compile-error startup in [logs/b-clean-session.txt](logs/b-clean-session.txt) does print `cnative: watching`.

`AndroidLoop.Execute` returns 1 when the first push classifies as `Failed`. `CompileError` does not. So a syntax problem on the first push leaves the watch up, and a dead socket ends the command. The app stays running (`pid 9492` after the exit). The next save hits no watcher.

**Smallest fix:** print `cnative: the first push failed. Still watching. Save src/NativePoc.Screen/Screen.cs again.` and enter `Watch`. Diff in proposals, item 3. This only helps if the in-memory token is the live one (F1). With a stale token, later pushes fail the same way until F1 is fixed.

## F4 — The README reload command is a benchmark that restarts the app

**Severity:** blocker for persona A.

**Evidence:** [persona-a.md](persona-a.md). README "Debug in-place reload" tells the reader to run `NativePoc.HotReload.Host.dll a`.

On the committed file, `a` ran `2026-11-08T19:51:35Z` to `2026-11-08T19:51:55Z`, exit 1, pid `6280` to `6573`, and the UI became `Ada` / `Hello Ada` / `Counter 1`. The name typed in the first-input step was gone before the compile error. After the using was added, `a` succeeded as a 20-sample benchmark (cold `829.9` ms, warm p50 `16.7` ms) and restarted again (`6573` to `6872`). It also rewrote `evidence/hot-reload-a.log` and the two `hot-reload-a-*.png` files.

`once`, which does one push and leaves the pid alone, is absent from the README. It appears only as `Usage: NativePoc.HotReload.Host <a|b|c|once>`.

**Smallest fix:** README section in proposals, item 4. State that `a` restarts the app, types its own sample, records 20 timings, and rewrites `evidence/hot-reload-a.log`. Point the edit loop at `tools/devx/cnative run android`.

## F5 — `docs/devx/comparison.md` ends by denying the loop that just ran

**Severity:** major.

**Evidence:** the page itself, read as a newcomer, against the run in [persona-b.md](persona-b.md).

The top of the page is the command and a successful E2E. Further down, "What is missing" still says:

- "The running Debug app does not write the token file"
- "Until `OnCreate` does that without an intent extra, the watch never starts."
- "A rude restart … was not exercised on the device."
- "No device reload time was recorded."

This session's clean launch read `files/nativepoc-hotreload.token`, watched, reloaded (`2682.9` ms / `47.3` ms and `2517.0` ms / `21.0` ms), and completed `y` on a rude restart (pid `8995` to `9323`). A reader who finishes the page will believe none of that is possible.

**Smallest fix:** replace those bullets. Text in proposals, item 5.

## F6 — Declining the restart prompt gives no next step

**Severity:** major.

**Evidence:** [logs/b-clean-session.txt](logs/b-clean-session.txt) lines with `Restart the app? [y/N] n`. No line after the `n` until the next edit. Pid stayed `8995`.

The comparison page says: "On a terminal, a rude edit asks `Restart the app? [y/N]`." Answering `n` is a valid choice. `ReloadConsole.FollowUp` adds `pass --restart-on-rude to rebuild and relaunch` only when stdin is not a terminal. On a terminal the hint is suppressed.

**Smallest fix:** pass `offerFlag` when the user did not restart, including an interactive `n`. Diff in proposals, item 5.

## F7 — Compile failures from the host DLL have no path, and the device never shows them

**Severity:** major.

**Evidence:** [logs/a-host-once-error.log](logs/a-host-once-error.log) versus the `cnative` line in [persona-b.md](persona-b.md). Screenshots [screenshots/a-compile-error.png](screenshots/a-compile-error.png) and [screenshots/b-compile-error.png](screenshots/b-compile-error.png) still show the last good screen.

Host:

```
exception=InvalidOperationException (7,36): error CS1525: Invalid expression term ';'
```

`cnative`:

```
src/NativePoc.Screen/Screen.cs:7:36: error CS1525: Invalid expression term ';'
```

Roslyn was given the source with no path, so the diagnostic has none. `cnative` fills in `Screen.cs` for a pathless diagnostic. The device has no error surface. An Expo developer expects a red box (documented, not tested here: [expo.md](expo.md)).

**Smallest fix:** in `Compile`, parse with `path:` set to `src/NativePoc.Screen/Screen.cs`, and throw the diagnostic text without the `InvalidOperationException` wrapper. One call-site change. Not in the top 5 because `cnative` already prints a usable line, and F2 removes the startup error a newcomer hits first.

## F8 — Setup and the dev command are not where a newcomer looks

**Severity:** major.

**Evidence:** [README.md](../../README.md) requirements and Debug sections; the emulator command exists only inside a RUN transcript in [comparison.md](../devx/comparison.md); help text quoted in [persona-b.md](persona-b.md).

Missing from the README:

- How to install the Android SDK pieces, create an AVD, and boot it.
- That `platform-tools` must be on `PATH`. UNVERIFIED: this session already had `adb` on `PATH`, so `-t:Run` was not retried with `ANDROID_HOME` only.
- That `tools/devx/cnative run android` is the edit loop.
- That an online device is required. The code throws `No emulator or device is online.` (`DeviceFacts.cs`). That string was not provoked in this session because the emulator was up. The throw is in the source; the session did not execute the empty-device path. Treat the wording as read from code, the empty-device run as UNVERIFIED.
- `--help` rebuilds the CLI first (`Time Elapsed 00:00:00.84` on a warm build).

The activity line `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity` is accurate and unexplained. Minor next to the missing command.

**Smallest fix:** the README section in proposals, item 4, plus skip the build in `tools/devx/cnative` when the argument is `--help`, `-h`, or `help` and the DLL is already built.

## F9 — Each save starts a new compiler process

**Severity:** major for the feel of the loop. The smallest change is a sentence, not a faster compiler.

**Evidence:** persona B, same pid `8995`: `reloaded in 2682.9 ms (apply 47.3 ms)` and `reloaded in 2517.0 ms (apply 21.0 ms)`. Persona A `once` on an already-running host process was `once-ms=20.6` and `once-ms=23.1` with about 2 s of wall clock around the `dotnet` invocation.

`cnative` prints both clocks, which is honest, and a reader coming from Fast Refresh will treat 2.5 s as the reload. The gap is process startup of `NativePoc.HotReload.Host` per save. A resident compiler is a different change, and another agent is already working in `tools/devx`. No diff for that here.

**Smallest fix:** one clause in the help text: "The first number includes starting the compiler. `apply` is the device time." Included in proposals, item 4.

## F10 — First input is undiscoverable from the docs

**Severity:** minor.

**Evidence:** screenshots [screenshots/a-first-input.png](screenshots/a-first-input.png) and [screenshots/b-first-input.png](screenshots/b-first-input.png). Logcat `clipboard-ok poc-1` when Save runs (`NativePoc` tag, persona A session).

The screen is a name field, Save, and `Enter a name` / `Hello {name}`. Save also copies `poc-{count}` and Android shows a clipboard chip. The README lists the element types and not this screen. Both personas had to leave the docs and use `adb shell input`.

**Smallest fix:** one README sentence: "The running screen has a name field and Save. Save increments the counter, greets the name, and copies `poc-{count}`." Included in proposals, item 4.
