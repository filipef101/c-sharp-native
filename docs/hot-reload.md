# What hot reload can and cannot change

This table is generated from the subset rules. A test lowers each row that has a screen and checks this file against that generator, so the table cannot drift from the rules.

| Change | Hot reload | Why |
| --- | --- | --- |
| Text, keys, and button labels | yes | The screen file lowers again and the same process applies the program. |
| A local function after the return | yes | Render hoists every local function before the rest of the body. |
| An expression-bodied local function nested in another | yes | Render hoists every local function, and an expression body lowers as that function. |
| platform.OnOpen after the markup | yes | OnOpen is hoisted and runs when the screen id changes. |
| await into a string local | yes | The result is a string slot, not a state field. |
| A JNI binding call in a button | yes | The call is the jni module through JniRegistry, or a diagnostic on that call. |
| A PackingTelemetry increment | yes | The reload program calls the telemetry module. The C# method increments the same counter. |
| Add and remove list rows | yes | The action updates the record and the screen renders again. |
| state.Title.Normalize() | no | A call the subset does not name is a diagnostic. The program does not skip it. |
| await of a call the subset does not name | no | Await is only the platform calls the subset lists, stored in a string property or a string local. |
| A new type | no | The running binary was compiled without it. Save a rude edit and relaunch. |
| A change under src/NativePoc.Core or src/NativePoc.Android | no | That is a rude edit. The listener prints restart required. |

A force-stop starts a new process and a new token file. The watcher reads that file after a rejected token or a dead socket and applies the next edit on the new process. The watch loop stays up.

A newly added `[JniMethod]` is not callable without a republish. The listener is NativeAOT and logs `IsDynamicCodeSupported=False`, so the process cannot compile the new method. `JniRegistry` is the list `RegisterJni` built at publish, and a reload program names only `Type.Method` through the `jni` module. An unknown name throws `jni-bind miss`. The generated trampoline only emits `J`, `I`, and `Z` arguments and a `V`, `Z`, `J`, or `I` return, and only for a method compiled into that binary. The program does not carry a class or a JNI signature, so a reflection-free dispatcher cannot run a new body such as `DeviceNotify.OpenSettings`. `cnative` keeps the republish line.

## Loaded-host relatives

These numbers are relatives. The host was already loaded. They are not a cold start and they are not a SwiftShader run.

| | |
| --- | --- |
| Date | 2026-11-13 |
| Commit | 0c6a958 |
| Machine | Mac arm64 emulator, API 36, host GLES via the Apple GPU |
| Shelf grid scroll frame p50/p90/p99 | 31.5 / 57.2 / 101.0 ms |
| Java list floor frame p50/p90/p99 | 27.8 / 49.3 / 113.2 ms |
| Janky median | 6.7% on both |
| Startup home-floor delta median | 53 ms |
| Shelf deep link delta | 71 ms |

The scroll gap was the renderer. Scroll was not re-run on SwiftShader.
