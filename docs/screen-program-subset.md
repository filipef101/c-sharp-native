# Screen program subset

`cnative lower <file>` lowers any screen class that stays in the subset below. It does not look at the file name. `Screen.cs`, `PackingScreen.cs`, and `SettingsScreen.cs` all lower through this subset. The running activity applies the record with `UseProgram`. The screen whose id is `counter` publishes `Count`, `Name`, `GreetingFirst`, and `PlainRoot` into `AppState`. The screen whose id is `packing` publishes `Items`, `Editing`, and the draft fields into `PackingState`. Any other screen keeps those values in its own bag. There is no activity per screen and no per-screen lowerer.

```sh
cnative lower samples/NativePoc.Settings/SettingsScreen.cs --json
cnative lower samples/NativePoc.Settings/SettingsScreen.cs --apply
```

`--json` prints one object. A program that lowers is `{"ok":true,"bytes":N,"hash":"..."}`. The hash is the first 16 bytes of SHA-256 of the source, hex. A refusal is `{"ok":false,"diagnostics":[...]}`. Each diagnostic has `id`, `message`, `start`, `end`, `line`, and `column`. `start` and `end` are UTF-16 offsets into the file. The process exits 1.

## What is accepted

A file is one namespace. It holds records or classes of state, and one static class with `public static Element Render(State state, IPlatform platform)`. The state type may live in the same file or be inferred from the `Render` parameter. `public const string` and `public static readonly string` on the screen class are values.

State properties are `string`, `bool`, `int`, or `List<T>` / `IReadOnlyList<T>` where `T` is a record whose fields are strings or bools. A get-only `int` is a flag count of the first bool field. A get-only `string` is the list text. Initializers are literals, a list of object initializers, or an empty `new List<T>()`. A reload keeps a value when the property name and type are still there. Methods on the state class are kept and are not seeded as properties.

The render body is one of:

- an expression, or a single `return`
- a chain of `if (condition) return element;` ending in `return element;`
- `var rows = new List<Element>();` with `Add` calls before and after one `foreach`, then `return` of a `ScrollView`, `ListView`, or column of that list

Elements are `Text`, `Button`, `TextField`, `Switch`, `Column`, `ScrollView`, `ListView`, and `Image`. A column's children are arguments or a collection expression. `Key` is a string literal, or an item string field (`Key = item.Id`). Keys in one parent are all set or all absent. `new ListView(rows) { Key = "..." }` is the recycled list. `new Image("marker")` names a drawable resource.

An element may set `Style = new Style { ... }`. The fields are `Padding`, `Margin`, `Gap`, `Grow`, `Align` (`Align.Start`, `Align.Center`, `Align.End`, `Align.Stretch`), `FontSize`, `Weight` (`FontWeight.Regular`, `FontWeight.Bold`), `Radius`, `Color`, and `Background`. Numbers are dp except `FontSize`, which is sp. `Color` and `Background` are `0xAARRGGBB`. Zero means unset, so a styled child does not clear the root padding. A style-only edit is a `SetStyle` patch on the same view.

A `TextField` may also set `Keyboard` (`Keyboard.Text`, `Keyboard.Email`, `Keyboard.Number`, `Keyboard.Phone`, `Keyboard.Password`), `Lines` (above 1 is a multiline field), `Submit` (`Submit.None`, `Submit.Next`, `Submit.Send`, `Submit.Go`, `Submit.Search`), and `OnSubmit`. `OnSubmit` is a lambda with the same actions as a button, plus `state.Name = state.Other.Length == 0 ? "missing" : ""`, `state.Flag = state.Other.Length != 0`, and the platform modules in [platform-modules.md](platform-modules.md). The host keeps the caret for a field whose key and text stay put across a reload. `cnative agent submit <id>` runs `OnSubmit`.

The render body may start with `platform.Effect("Property", "resource")` and `platform.Every("Property")`. `Effect` names a string property and a local asset (`resource` + `.txt`). The host starts one task for that pair. A reload while the task is pending does not start a second task, and the task writes the property once. `Every` names an int property. It does not run a timer. `cnative agent clock` adds one to each armed property.

Expressions are string literals, `+`, `$"..."` with holes of the same expressions, `condition ? a : b` of texts or of elements, and parentheses. A condition is a bool property, `true` / `false`, `property.Length == 0`, string equality, or any bool expression the action VM can evaluate, including `state.Count > 0`. An `if` / `else` whose branches each return an element is the same jump. An `if` without an `else` may return early. An int property interpolates as a decimal. `foreach` reads item string fields and item bools. A method in the same file that returns an `Element` can be the element a row adds, as in `rows.Add(Row(item))`.

An action is a lambda. The running process interprets it with a switch in `RecordProgram.RunSteps`. That switch is compiled into the NativeAOT binary. An action does not call `Assembly.Load` and does not generate code. The forms opcodes 1–33 already name stay in that encoding. Anything else is one `ActVm` program, a stack machine in [architecture.md](architecture.md). One tap stops after 100000 steps. The log line is `action stopped after 100000 steps`. Where the same C# would throw, the action stops with that exception's message. The writes so far stay, and the host asks for a render. The process stays up.

An action may:

- assign a property from the lambda argument, a literal, a property of the same kind, or an item field
- use `+`, `-`, `*`, `/`, `%`, and unary minus on int and double. Integer `/` and `%` match C#, including the overflow of `int.MinValue / -1`. Integer divide by zero stops with `Attempted to divide by zero.` Double `/` and `%` do not stop: `1.0 / 0.0` is infinity
- use `<`, `>`, `<=`, `>=`, `==`, `!=`, `!`, `&&`, and `||`. `&&` and `||` skip the side that must not run
- use `property++`, `++property`, `property--`, and `--property` on an int, as a statement or as a value (`var n = ++state.Count`)
- use `+=`, `-=`, `*=`, `/=`, and `%=` on an int, and `+=` on a string
- use `flag = !flag`, `flag = !other`, and `flag = other` on bools
- concatenate strings with `+`, `+=`, and `$"..."`. A hole may be any expression the VM can evaluate, including arithmetic
- use `value = condition ? a : b`, and `if` / `else`. An `if` that the older condition opcodes can name stays those opcodes and nests at most eight deep. Any other condition is a jump in the VM. A bare `return;` stops the rest of that action. Statements after it do not run. A `return` with a value uses the helper return path
- use `for`, `while`, and `foreach` over a state list. `foreach` walks by index. The body may read `item.Field`. Removing or adding during the walk stops the action with `Collection was modified; enumeration operation may not execute.` The removal itself has already happened, as it does in C#
- use `?.` and `??`. `?.` does not call when the receiver is null. `??` replaces that null
- declare one local at a time, `var` or a typed name, of int, double, string, or bool. An action has eight slots. A local stays visible for the rest of that action. A second `var` of the same name is refused. `string label = null` stores null
- call `list.Add(new T { Field = cell })`, `list.Clear()`, `list.RemoveAt(index)`, and, on the row a `foreach` is visiting, `list.Remove(item)`, `list.Contains(item)`, and `list.IndexOf(item)`. `list.Count` is the row count. A cell is a string literal, a string property, a string local, or a bool literal. A missing cell is `""` or `false`. `RemoveAt` past the end does nothing
- call `Trim`, `ToUpper`, `ToLower`, `Contains`, `StartsWith`, `Substring`, and `Length` on a string. `ToUpper` and `ToLower` use the invariant culture. `Substring` past the end stops with the `ArgumentOutOfRangeException` message. A null receiver stops with the `NullReferenceException` message
- call `Math.Min`, `Math.Max`, `Math.Abs`, and `Math.Round`. `Round` uses midpoint-to-even and returns a double. Storing a double in an int needs `(int)`
- call `int.Parse` and `int.TryParse(text, out var name)`. A bad or overflowing parse stops with the `FormatException` or `OverflowException` message. `TryParse` is the one `out` the VM accepts. `(int)` of a double matches the C# cast, including NaN and values past `int.MaxValue`
- call `platform.RequestRender()`, `platform.Pulse()`, `platform.CopyNonce` with a string literal, a string property, or `"poc-" + state.Count`, `platform.CopyText` with a literal label and the list text, `platform.Push("screen-id")`, `platform.Push("screen-id", item)` from the row a render `foreach` is visiting, `platform.WriteBack()`, `platform.Pop()`, and `platform.Effect`. `Push` of a row copies that row's fields onto the destination screen's properties of the same name and stores the route in the destination bag. `WriteBack` writes those properties back onto the source row. The route strings are part of the nav checkpoint, so a reload keeps them
- call `state.Commit()` and, inside a foreach, `state.Drop(item)`
- call a private static method or an instance method declared in the same file, with int, double, bool, or string parameters. The methods may call each other, and a method may call itself. Recursion stops after 64 calls with `action stopped after 64 calls`. A method that returns an `Element` is a render helper, not an action call
- call `Haptics.Confirm`, `Share.Text`, `Clipboard.Copy`, `Storage.Set`, and `Theme.Mode` as in [platform-modules.md](platform-modules.md)

These stay refused. The VM has no arm for them, and a reload cannot invent one:

- `await`. An action does not start a task
- a call that is not in the list above, including `state.Title.Normalize()`. The table is a switch, not a lookup
- a new type, or a generic that the edit instantiates for the first time. The binary was compiled without it
- `out` and `ref` other than `int.TryParse`, two names in one declaration, a list-typed local, a nested lambda, and assigning one list to another

`cnative run android --restart-on-rude` relaunches the installed process. The C# compiler then puts that method or type into the next binary. The relaunch keeps the checkpoint.

A `public const string ScreenId` names the screen on the stack. Each id keeps its own record across a reload. A `PackingTelemetry` increment inside an action is a no-op in the program. The C# method still increments.

`cnative agent tree` prints one `stack N id` line per frame, from the bottom of the stack. `Show` updates the program. The stack gains a frame only when that id is not already on it, so a show of the top screen is a no-op and a show of a screen further down does not add a second copy. `Push` of an id that is not on the stack appends it. `Push` of an id already on the stack moves that frame to the top and leaves the other frames in order. It does not add a second copy. `Push` of the current top leaves the stack where it is. The stack is also the `NAV` trailer after the STC1 packing note. The version byte stays 1 or 2. A checkpoint without that trailer still loads. Bytes that are not the trailer are rejected.

## Refusal ids

| id | when |
| --- | --- |
| `subset.not-a-screen` | the file is not one `Render(state, IPlatform)`, or the source does not parse |
| `subset.unknown-member` | a member, property, or initializer the subset does not name |
| `subset.unknown-type` | a property that is not a string, bool, int, or list of string or bool records |
| `subset.unknown-element` | a constructor that is not one of the elements above |
| `subset.unknown-expression` | an expression outside the list above |
| `subset.bad-lambda` | an action outside the list above |
| `subset.bad-foreach` | a loop that is not one `Add` of an element from a list property |
| `subset.duplicate-key` | two children with the same literal key |
| `subset.mixed-keys` | a parent mixes keyed and unkeyed children |

A running `cnative run android` prints `path:line:column: id: message` and `The edit was not applied. The last program stays until relaunch.` The device overlay shows the same id and message, and the third line is `file:line` from the screen source. A runtime exception in a handler uses that same overlay and leaves the last tree up. A compiler error in `Screen.cs` puts `error CSxxxx` in the overlay. The next save that applies clears it. `--restart-on-rude` relaunches the installed process without publishing again, restores the checkpoint, and applies the last program that lowered. A Core or Android edit still rebuilds first. A parse error in a generated screen is `subset.not-a-screen` and uses that same relaunch. A parse error in `Screen.cs` is the compiler's `error CSxxxx` line, and that process stays up. Debug writes `files/nativepoc-state.bin` from `StateStore` on each render, including the nav bags, and the launch does not delete that file. A reload keeps a bag value when the property name and type are still there, because `Seed*` writes only when the key is missing. Changing `"Hello"` to `"Hello again"` leaves the heading on `Hello`. The apply reply then adds one line, `state kept Title="Hello" (initializer "Hello again"). cnative reset-state`. Several kept properties share that line, separated by commas. A first show has no keys, so it prints no line. A string literal in the render does change. `cnative reset-state` zeros the counter `AppState`, restores the packing sample list, deletes `files/nativepoc-state.bin`, replaces every nav bag, and applies the screen on top again so the initializers show. The next render writes a new state file from that reset. Only the `counter` screen reads and writes `AppState`. Only the `packing` screen reads and writes the packing list. A generated `notes` screen with its own `Count` does not inherit the counter.

## Scroll

`ScrollView` is one keyed child. The Android host maps it to `android.widget.ScrollView` (`FillViewport`) and reconciles that child by key, the same way a column reuses a row. The packing list is a `ScrollView` of key `scroll` around the `list` column, in `PackingScreen.cs` and in the lowered program.

`ListView` is a keyed list of rows. The Android host maps it to a `ScrollView` that keeps 20 slots and rebinds them as it scrolls. It is not `AndroidX.RecyclerView`. The agent tree is one list node. The rows are not id-tree children.

A `foreach` of more than 24 rows does not build those rows during the first eval. It pushes one `ListView` whose rows are a window. Indexing a row runs that row's body, so a scroll that asks for the visible slots builds those slots and leaves the rest unbuilt. A list of 24 rows or fewer still expands, which is the packing screen's own four rows (the switch, the edit button, and the swipe stay on those rows). The 1000-row bench is the window. Headers and footers around the `foreach` stay in the column. A window slot shows the row's first text, a second text when that text is not empty, and an image when the source is not empty. Those three make different heights. The height of a row that has not been built yet is one text line, so the first frame does not evaluate every row. When the parent measures the window with an unbounded height, the window takes at most one screen and scrolls itself. The slot's key is the row key. Scrolling a slot onto a different row replaces that slot's title, subtitle, and image from the row it now shows. The switch and the button stay on the expanded list.

`HostView` is a platform view. `new HostView("webview", "url", state.Url)` names the kind, the prop, and the value. `Event` and `OnEvent` are the event name and the script. See [native-view.md](native-view.md). `L.Get`, `L.Format`, and `L.Plural` are the locale helpers in [localization.md](localization.md). `L.Plural` chooses the `one` string when the count is 1 and the `other` string otherwise.

## Accessibility

`ScreenSnapshot.Role` is the role. The host widget is the platform role, and the accessibility label is `contentDescription`.

| element | role | Android class | content description |
| --- | --- | --- | --- |
| Column | column | `android.widget.LinearLayout` | none |
| Text | text | `android.widget.TextView` | the text |
| Button | button | `android.widget.Button` | the label |
| TextField | textfield | `android.widget.EditText` | the label |
| Switch | switch | `android.widget.Switch` | the label |
| ScrollView | scroll | `android.widget.ScrollView` | none |
| ListView | list | `android.widget.ScrollView` (20 slots) | none |
| Image | image | `android.widget.ImageView` | the drawable name |
| HostView | host | the view named by `NativeViews` | the prop value |

`FontSize` is sp. The host calls `SetTextSize` with `ComplexUnitType.Sp`, so a system font scale of 1.3 or 2 grows that text. A `Text` whose `FontSize` is 20 or more is an accessibility heading on API 28 and later. Buttons, switches, and any view with a gesture are at least 48dp on each side.

`cnative agent a11y` prints `class`, `desc`, `text`, and `bounds` from `uiautomator dump`. `cnative agent a11y --check` reads that dump plus `wm density` and `settings get system font_scale`. It exits 1 when a clickable or long-clickable control is under 48dp, a button, switch, image, field, or text control has no text and no content description, a leaf sits above the previous leaf in the same column, or a text line is shorter than 16sp at the current font scale. A control whose bounds stop on the scroll view's bottom edge is partly off screen, and an empty `android.view.View` is the decor inset. Neither is a violation. The report includes `headings=N`. When the dump has no `heading` attribute it also says `heading-attribute=absent`. A `tests/device` step `a11y <text>` asserts that dump. The check itself does not need the hot-reload listener.

## Layout, logs, and the clock

`cnative agent layout` prints `id`, `left`, `top`, `width`, and `height` for each element, from the mounted view. `cnative agent logs` prints `logcat -d --pid=<app> -s NativePoc:I NativePoc:W NativePoc:E` for the running process.

An element may set `OnTap`, `OnLongPress`, `OnSwipe`, or `OnRefresh` to a lambda with the same actions as a button. `OnSwipe` is a horizontal swipe. On a packing row it removes that row. `OnRefresh` belongs on a `ScrollView` or `ListView` and can call `platform.Effect`. A reload that keeps the view keeps the gesture; a reload that removes the view does not dispose it until the gesture ends. `cnative agent swipe <id>` runs `OnSwipe`.

A `Motion` initializer animates opacity, translation, and scale. `FromOpacity`, `FromX`, `FromY`, and `FromScale` are the start. `Opacity`, `X`, `Y`, and `Scale` are the end. `Duration` is milliseconds. A fresh view starts at the from values. A reload that keeps the view continues from the value it has reached. `cnative agent clock` steps those motions by 100 ms and answers `animations=N`. When no screen has called `Every`, it also answers `clock=0`. After a screen arms an int with `Every`, each call adds one to that property and answers `clock=N`, where `N` is how many times the verb has stepped. The clock does not move between calls. Outside that agent clock, the same motion runs on the display's clock.
