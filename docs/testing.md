# Testing a screen without a device

`ScreenSession` runs a lowered record through `RecordProgram` on the machine that executes the test. It does not open a socket, install an APK, or read a device. The same interpreter is what the activity runs after `cnative lower`.

```csharp
var lowered = SubsetLowerer.TryLower(File.ReadAllText(path));
var session = ScreenSession.Open(lowered.Program!);
session.Enter("name", "Ada");
session.Tap("save");
session.Tree();
session.State();
```

`Open` binds a fresh bag, renders once, and keeps that session as the `IPlatform`. `Tree` is `ScreenSnapshot.FormatTree`: one line per element, with `role`, `id`, and the value or title. A `ListView` stays one node. Its rows are not children of that snapshot, which is the same tree the agent prints for a windowed list. `State` is the bag: strings are lowercase hex, flags are `true` or `false`, ints are decimal. It does not print list rows. Read those with `session.Bag.Rows`.

`Enter`, `Tap`, `Submit`, `Swipe`, `LongPress`, and `Refresh` call `ElementIds` on the current tree and render again when the action asks for a render. `Tap` presses a button, toggles a switch, and refuses a text node. `Copies` records `CopyNonce`. `Effects` records `Effect` as `property=resource` and does not start the Android asset task. `Stack` records `Push` and `Pop`.

`Step` adds one to every int the screen armed with `Every`, advances each `Motion` by the given milliseconds (100 when omitted), and renders. `Opacity(id)` is `MotionMath.Lerp` at that elapsed time. The step does not wait on a display clock.

Two device scripts are covered by `ScreenSessionTests` and stay in `tests/device` for an emulator run:

| Device script | Headless assertion |
| --- | --- |
| `tests/device/counter.app.test` types `Ada`, taps save, and expects `count=1`, the name hex `416461`, and `id="save"` | `Enter` + `Tap` on the lowered `Screen.cs`. The bag lines are `Count=1` and `Name=416461`. The tree contains `id="save"` and `value="Ada"`. |
| `tests/device/packing.app.test` taps `scroll/list/tent/packed` and expects that id and the tent row packed | `Tap` on the lowered `PackingScreen.cs`. The tree shows the switch `value=true`. `Swipe` on `scroll/list/tent` removes that row from the bag. |

A third test lowers a screen that calls `Every("Tick")` and sets a 200 ms opacity motion. One `Step` makes `Tick=1` and opacity 50. The second step finishes the motion.

`cnative agent a11y --check` still reads the device (`uiautomator dump`, `wm density`, `font_scale`). `AccessibilityDump.Check` is the part that decides violations, and `AccessibilityCheckTests` feeds it XML with no device. `RowMetrics` and `RowSlotState` cover variable-height rows and keyed rebinding with no device. The scroll frame stats in the README are an emulator run, not this API.
