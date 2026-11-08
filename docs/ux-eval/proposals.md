# Proposals

Not applied. Written against `c449270`. Diffs use the file's tab indentation.

## 1. F1 — drop the previous token file before launch

`tools/devx/NativePoc.Devx.Cli/AndroidLoop.cs`, in `DeployAndLaunch`, after `force-stop` and before `am start`:

```diff
 		Console.WriteLine("cnative: launching " + component);
 		Adb(serial, "shell", "am", "force-stop", LaunchCommand.Package);
+		Adb(serial, "shell", "run-as", LaunchCommand.Package, "rm", "-f", "files/nativepoc-hotreload.token");
 		Thread.Sleep(300);
 		var started = Adb(serial, startArgs);
```

`WaitToken` already polls for 30 s. After the delete, the first 32-hex file it sees is the one the new process writes. `rm -f` is a no-op when the file is absent, which is the first-install case that already worked.

## 2. F2 — let the stock screen compile

`src/NativePoc.Screen/Screen.cs`:

```diff
+using System.Collections.Generic;
+
 namespace NativePoc;
```

`IReadOnlyList<Element>` on the committed line 31 is what `CS0246` names. The csproj already enables implicit usings; the hot-reload compiler does not. This line is the change that made `reloaded in 2682.9 ms (apply 47.3 ms)` succeed in persona B.

## 3. F3 — keep watching when the first push fails

`tools/devx/NativePoc.Devx.Cli/AndroidLoop.cs`, in `Execute`:

```diff
 			if (options.Once)
 				return initial.Status == ReloadStatus.Applied || initial.Restarting ? 0 : 1;
 			if (initial.Status == ReloadStatus.Failed)
-				return 1;
+				Console.Error.WriteLine("cnative: the first push failed. Still watching. Save src/NativePoc.Screen/Screen.cs again.");
 			return Watch(repo, rid, serial, token, options, cancel.Token);
```

A compile error already falls through to `Watch`. A connection error is `Failed` and is what exited `1` in [logs/b-stale-token-relaunch.txt](logs/b-stale-token-relaunch.txt). Staying up does not repair a stale token; item 1 does that. Staying up means a failed first push does not abandon the terminal.

## 4. F4 and F8 — README dev loop

Insert this section in `README.md` after the Debug `-t:Run` block and before the paragraph that begins "Debug in-place reload":

```markdown
## Dev loop

`tools/devx/cnative run android` builds Debug, installs it on the emulator or device that is already online, launches it, and reloads saves of `src/NativePoc.Screen/Screen.cs`. It does not boot an emulator. `adb` from `platform-tools` has to be on `PATH`. With no device online the command prints `cnative: No emulator or device is online.` (that sentence is `cnative: ` plus the exception from `DeviceList.Require`; this session did not execute the empty-device path).

The running screen has a name field and Save. Save increments the counter, greets the name, and copies `poc-{count}`.

Only `src/NativePoc.Screen/Screen.cs` reloads in place. A save under `src/NativePoc.Core` or `src/NativePoc.Android` prints `rude edit -> restart required` and asks `Restart the app? [y/N]`. `--restart-on-rude` rebuilds and relaunches instead of asking. A restart is a new process, so the counter and the name go back to `Counter 0` and `Enter a name`.

`reloaded in 2500 ms (apply 20 ms)` means the whole push took 2.5 s and the device apply took 20 ms. The first number includes starting the compiler process.

The `NativePoc.HotReload.Host` modes `a`, `b`, and `c` below are 20-sample benchmarks. `a` restarts the app, types its own name, and rewrites `evidence/hot-reload-a.log`. It is not the edit loop.
```

And in `tools/devx/cnative`, skip the rebuild when the user asked for help and the DLL is already there:

```diff
 here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
 root=$(cd "$here/../.." && pwd)
 project="$here/NativePoc.Devx.Cli/NativePoc.Devx.Cli.csproj"
+dll="$here/NativePoc.Devx.Cli/bin/Debug/net10.0/cnative.dll"
+if [[ ${1:-} == "--help" || ${1:-} == "-h" || ${1:-} == "help" ]]; then
+	if [[ -f "$dll" ]]; then
+		exec dotnet exec "$dll" "$@"
+	fi
+fi
 dotnet build "$project" -c Debug --nologo -v q
-exec dotnet exec "$here/NativePoc.Devx.Cli/bin/Debug/net10.0/cnative.dll" "$@"
+exec dotnet exec "$dll" "$@"
```

Help text in `tools/devx/NativePoc.Devx.Cli/ArgParser.cs`, inside the `Usage` string, after the opening paragraph:

```diff
 		  Build Debug, install it on the running emulator or device, launch it,
 		  read the hot reload token with adb run-as, and push Screen.cs edits.
+		  The emulator or device must already be online. This command does not start one.
+		  Only src/NativePoc.Screen/Screen.cs reloads in place.
+		  Saves under src/NativePoc.Core or src/NativePoc.Android ask to restart the app.
+		  "reloaded in" includes starting the compiler. "apply" is the device time.
```

## 5. F5 and F6 — stale "What is missing", and the declined restart

`docs/devx/comparison.md`, replace the whole "What is missing" list with:

```markdown
## What is still true

- The command does not boot an emulator. An online `adb` device is required. With none, the source prints `cnative: No emulator or device is online.` This session did not execute that path.
- Only `src/NativePoc.Screen/Screen.cs` is compiled into the swapped DLL. A second file under `src/NativePoc.Screen` is reported and not included. `src/NativePoc.Core` and `src/NativePoc.Android` print `rude edit -> restart required`.
- A rude restart is a full Debug `-t:Run`. The new process starts `AppState` over.
- There is no on-device error surface. A compile error is a terminal line, `src/NativePoc.Screen/Screen.cs:<line>:<col>: error CS…`. The last good frame stays on screen.
- `run-as cat /proc/net/unix` is `Permission denied` on this API 34 image. The token file is the socket name.
- Release and NativeAOT are outside this command.
```

Delete the bullets that say the app does not write the token file, that the watch never starts, and that no device reload time was recorded. Those describe older trees. This commit writes the file, and a clean-data session in `docs/ux-eval/persona-b.md` recorded `2682.9` ms and `2517.0` ms.

`tools/devx/NativePoc.Devx.Cli/AndroidLoop.cs`, both copies of the `FollowUp` call (the watch loop and `Push`):

```diff
-			var follow = ReloadConsole.FollowUp(restarting, !restarting && !interactive && !options.RestartOnRude);
+			var follow = ReloadConsole.FollowUp(restarting, !restarting && !options.RestartOnRude);
```

The watch-loop copy uses the same expression with a tab-aligned `var follow`. After `n`, the terminal then prints:

```
pass --restart-on-rude to rebuild and relaunch
```

That string already exists in `ReloadConsole.FollowUp`. The change is to show it on a terminal too.
