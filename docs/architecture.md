# Architecture

This page describes the path in this tree, from a C# screen function to an Android view. The UIKit host is authored and is not compiled here. See [ios-status.md](ios-status.md).

## Element tree

A screen is a function that returns an `Element`. The types live in [`src/NativePoc.Core/Elements.cs`](../src/NativePoc.Core/Elements.cs): column, text, button, text field, switch, scroll, list, image, and `HostView`. A `Key` is optional until a sibling has one. [`Diff.cs`](../src/NativePoc.Core/Diff.cs) walks the previous tree and the next. When the previous node and the next node are the same reference, that walk returns before the children. [`ViewReconciler`](../src/NativePoc.Core/ViewReconciler.cs) applies that diff through `IElementHost`. The host is the only type that constructs a platform view.

## Subset lowering

[`SubsetLowerer`](../tools/devx/NativePoc.Devx.Cli/SubsetLowerer.cs) reads one screen file in the subset documented in [screen-program-subset.md](screen-program-subset.md). `cnative lower` prints the record as JSON or as a C# byte array. A construct outside the subset is a diagnostic. It is not rewritten into a nearby construct.

## NPC records

`RecordProgram.Encode` writes a payload whose first four bytes are `N`, `P`, `C`, `1`. The next byte is `ScreenProgram.Version` (1). The byte after that is the constant 2. `Decode` checks the magic and the version, then reads from the following byte. It does not branch on that constant. After the header:

- a string table
- props (`TypeString`, `TypeBool`, `TypeInt`, `TypeList`, `TypeScreen`)
- the opcode body
- 16 bytes of the SHA-256 of the source, or zeros when there is no source

`TypeScreen` holds a screen id. `ScreenNav.Stage` stores those bytes and does not decode them. `Push` and `Top` call `Ensure`, which decodes the staged screen. The counter is the launcher. The other catalogs stay bytes until a navigation asks for them.

## Interpreter

`RecordProgram.Bind` returns a `Bound` whose `Render` function evaluates the opcode body. The evaluator is a stack of slots. Lists of more than `EagerRowLimit` (24) push one `ListView` of a `WindowList`. Indexing a `WindowList` evaluates that row. Lists of 24 rows or fewer still expand, so the four-row packing screen keeps its switch, edit button, and swipe. `RowWindow` on Android keeps 20 slots (`RowWindow.Window`). Each slot is a title, a subtitle when the row has a second text, and an image when the row has a drawable. Slot offsets come from `RowPrefix`, which stores those heights and answers the row at a scroll offset without walking the rows above it on every rebind. `RowWindowPlan` leaves a slot that already shows a still-visible row in place, so a one-row scroll rebinds one slot. The text-only window's packing1000 first frame built 20 of 1000 rows and mounted 32 views. That count is in the README, from the x86_64 emulator series on sha256 `f7e2a6411480cc6337c30d995463ae654fc7171f11d92433d6e68acae03ccab9`. The variable-height slots, measured on one later COLD launch, were `views=92` with the same `rows=1000 built=20`.

## Action VM

An action is still a list of the `Act*` opcodes in [`RecordProgram`](../src/NativePoc.Core/RecordProgram.cs). Opcodes 1 through 33 stay the encoding for the forms they already name: a render, a simple store, `++` and `+=` / `-=` of an int, string `+`, `if` / `else` on the conditions those opcodes compare, a local of int, string, or bool, list add / clear / `RemoveAt`, and the platform calls. Nothing renumbers them.

A form those opcodes cannot name is one `ActVm` (34). The bytes after it are a stack program. Each value is a struct, and an int local is one of eight slots on the handler frame. The integer literal, the int local load, the int `<` compare, the jumps, and a post-increment of an int local are written in the switch. `RunVm` is marked `AggressiveOptimization`. A nested script runs on the same byte array. Each instruction is a byte the AOT compiler turns into a switch arm. The arms are arithmetic (`+`, `-`, `*`, `/`, `%`, and unary minus) on int and double, comparisons and `!`, `&&`, `||`, string `+`, `?.`, `??`, `++` and `--` used as values, jumps for `if` / `while` / `for` / `foreach`, and a call. Integer `/` and `%` match C#. A divide by zero stops the action. `&&` and `||` jump over the side that must not run. `foreach` walks a state list by index. A list's fields are strings and bools, so a sum parses a string field. `?.` leaves null when the receiver is null and does not call. `??` replaces that null.

Calls are a second switch on a ushort id, also compiled in. The ids are `Trim`, `ToUpper`, `ToLower`, `Contains`, `StartsWith`, `Substring`, `Length`, `Math.Min`, `Math.Max`, `Math.Abs`, `Math.Round`, `int.Parse`, and `int.TryParse`, plus `Remove`, `Contains`, `Count`, and `IndexOf` on a state list. `ToUpper` and `ToLower` use the invariant culture. `Math.Round` uses midpoint-to-even and returns a double. `int.TryParse` is the one `out` this program accepts: it writes an int local. There is no reflection and no `Assembly.Load`. A private static method, a private instance method on the state type, or a method that returns an `Element`, when it is declared in the same file, is lowered into the program and called by `VmUser` or, for an element, by the render. Those methods may call each other. Recursion stops after 64 calls with `action stopped after 64 calls` and still counts toward the step budget.

A public method marked `[ReloadCallable]`, or a public method of a type inside a namespace named by an assembly-level `[ReloadCallable("Namespace")]`, is a third switch. [`NativePoc.ReloadGen`](../src/NativePoc.ReloadGen/ReloadDispatchGenerator.cs) emits `ReloadDispatch.Call`. `VmApp` (48) reads the type name, the method name, and up to eight arguments, and calls that switch. The arguments and the return are int, bool, or string. `CounterService.Next` in the screen project and `LibraryFormat.Label` in `samples/NativePoc.Library` are in the table. The body that runs is the body compiled into the binary. Saving the file prints that the body was not swapped. The running handler keeps the previous body until `cnative run android --restart-on-rude` relaunches. Each string argument is stored in a local before the call. The trimmer inlined a constant method and left the `??` argument's IL unbalanced when that argument was still an expression at the call. A new top-level type, a generic constructed only by the edit, or a method the table does not list, has no arm. The edit is refused. `await` is in that group: an action does not start a task.

A public static method in another file of the same `samples/<name>` folder, when it returns an `Element` and its parameter has the same name as the screen state, is inlined at the call. Saving that file reloads every `*Screen.cs` in the folder.

One tap shares a budget of 100000 steps, counted on every action opcode and every VM instruction. The old cap of 128 steps per slice is gone. Past the budget the action stops. The log line is `action stopped after 100000 steps`. Where the same C# would throw, the action stops with that exception's message: integer divide by zero, overflow of `int.MinValue / -1` or `Math.Abs(int.MinValue)`, `Substring` out of range, `int.Parse` of a bad or overflowing string, a null receiver, and modifying a list during `foreach`. Double `/` and `%`, and a cast of a double to int, use the C# results, including infinity, NaN, and saturation. Integer `+`, `-`, and `*` stay unchecked and wrap. The writes so far stay in the bag, the host asks for a render, and the process stays up. `ActionLog` records the microseconds and the step count for that tap (`action-us=` in logcat).

## Swapping a compiled method body

This is a design. It is not implemented. `IsDynamicCodeSupported` stays false, so a running NativeAOT process cannot compile a new method body. The call table in `ReloadDispatch` is a switch that was compiled into the binary. Saving a `[ReloadCallable]` body today leaves the previous body in that process.

The subset already lowers an action, and a same-file helper, into `ActVm` bytes. A reloadable method can use that path. The mark stays `[ReloadCallable]`. The generated arm keeps the compiled call as the default. In front of it, the arm reads a slot for that method id. When the slot is empty, it calls the compiled body. When a reload has stored bytes there, it calls `RunVm` on those bytes and returns that result. The edit is lowered on the host, the same way a screen file is lowered, and the bytes are sent on the listener socket. The slot stores them. A later call in the same process runs the new body. A restart loads the slot from the checkpoint, so the replacement survives the Debug state file. The next Release build compiles the body that is on disk and the slot starts empty.

The costs are the ones that should decide whether to build it. Every call through the table pays a null check that the compiled body does not pay today. The replacement runs on the interpreter, so a hot method is slower than the body the AOT compiler produced. The arguments stay int, bool, and string, because that is what `RunVm` and the table already pass. A new type, a new generic, a `ref` or `out` other than `int.TryParse`, and `await` still have no arm: the slot cannot invent a signature that was not compiled. A method the table does not list cannot grow one while the process is up. The host has to refuse an edit whose lowered body uses an opcode the running binary does not have. Two versions of one method can disagree about a bag write, and the checkpoint then holds whichever one ran last. The compiled default and the slot have to be checked on the UI thread, because that is where the action runs, so the check itself has to stay a load and a branch.

## Host

`AndroidHost` implements `IElementHost`. `NativeViews` constructs an `Android.Webkit.WebView` for `HostView` with a direct call. There is no reflection on that path. `ThemeRuntime.Paint` sets the page color and the text color. A later frame with the same root and the same dark bit skips that walk. The startup census runs on the first frame. `LocaleRuntime` selects a `StringTable` and sets layout direction. Invariant globalization stays on. The Release configuration sets `PublishAot`. The listener is absent from a normal Release build. `NativePocDevListener` on a Debug publish turns `PublishAot` on and keeps the program-only listener. That flag refuses a Release build.

The UIKit project maps the same element kinds, including `Host` to `UIView`. It is not in the solution that this tree builds.

## Reload and checkpoint

```mermaid
flowchart TD
  screen["Screen.cs or a sample screen"] --> lower["SubsetLowerer"]
  lower --> npc["NPC1 record"]
  npc --> nav["ScreenNav Stage or Ensure"]
  nav --> eval["RecordProgram.Eval"]
  eval --> tree["Element tree"]
  tree --> diff["Diff"]
  diff --> host["IElementHost"]
  host --> android["Android widgets"]
  host --> uikit["UIKit mapping, authored"]
  edit["cnative run android"] --> socket["localabstract:npoc-hotreload"]
  socket --> apply["program bytes on the UI thread"]
  apply --> eval
  debug["Debug render"] --> file["files/nativepoc-state.bin"]
  file --> restore["read before the first frame"]
```

Debug `cnative run android` builds for the device ABI, installs, and watches the screen files. A save that stays inside the subset is lowered again and sent on `localabstract:npoc-hotreload`. The token is a per-process file read with `run-as`. It is not a launch extra. The program-only listener applies the record and does not load an assembly. A change under `src/NativePoc.Core`, `src/NativePoc.Android`, `src/NativePoc.Screen/CounterService.cs`, or `samples/NativePoc.Library` is a restart. A shared component under `samples/<name>` reloads every screen in that folder.

A Debug process writes `files/nativepoc-state.bin` on each render and reads it before the first frame. The UI thread only queues that save. A worker copies the checkpoint and writes the latest bytes, and logs `state-save-us=` for the file write. An unchanged epoch does not queue another copy. `cnative run android` also captures the running checkpoint before a deploy and writes it back after launch. Release does not write that file. A Release force-stop returns to the seed state. The phase timings are in [devx/evidence/render-phases.txt](devx/evidence/render-phases.txt). The call table and the shared-component reload are in [devx/evidence/call-table.txt](devx/evidence/call-table.txt).
