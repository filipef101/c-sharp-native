# Screen program subset

`cnative lower <file>` lowers any screen class that stays in the subset below. It does not look at the file name. `Screen.cs` and `PackingScreen.cs` stay on their own lowerers: those screens talk to the checkpointed counter and packing state, which are not a record bag. A new screen uses this subset, and the running activity applies it with `UseRecord`. There is no activity per screen.

```sh
cnative lower samples/NativePoc.Settings/SettingsScreen.cs --json
cnative lower samples/NativePoc.Settings/SettingsScreen.cs --apply
```

`--json` prints one object. A program that lowers is `{"ok":true,"bytes":N,"hash":"..."}`. The hash is the first 16 bytes of SHA-256 of the source, hex. A refusal is `{"ok":false,"diagnostics":[...]}`. Each diagnostic has `id`, `message`, `start`, `end`, `line`, and `column`. `start` and `end` are UTF-16 offsets into the file. The process exits 1.

## What is accepted

A file is one namespace. It holds records or classes of state, and one static class with `public static Element Render(State state, IPlatform platform)`.

State properties are `string`, `bool`, `int`, or `List<T>` / `IReadOnlyList<T>` where `T` is a record declared earlier in the file whose fields are strings. Initializers are literals, or a list of object initializers. A reload keeps a value when the property name and type are still there.

The render body is one of:

- an expression, or a single `return`
- `if (condition) return element; return element;`
- `var rows = new List<Element>();` then one `foreach (var item in state.Items) rows.Add(element);` then `return` of that list

Elements are `Text`, `Button`, `TextField`, `Switch`, `Column`, and `ScrollView`. A column's children are arguments or a collection expression. `Key` is a string literal, or, on a `Text` inside `foreach`, an item string field. Keys in one parent are all set or all absent.

Expressions are string literals, `+`, `$"..."` with holes of the same expressions, `condition ? a : b`, and parentheses. A condition is a bool property or `true` / `false`. An int property interpolates as a decimal. `foreach` reads item string fields.

An action is a lambda. It may assign a property from the lambda argument or a literal, use `property++` on an int, call `platform.RequestRender()`, or call `platform.CopyNonce` with a string literal or a string property.

## Refusal ids

| id | when |
| --- | --- |
| `subset.not-a-screen` | the file is not one `Render(state, IPlatform)` over a record in the file, or the source does not parse |
| `subset.unknown-member` | a member, property, or initializer the subset does not name |
| `subset.unknown-type` | a property that is not a string, bool, int, or list of string records |
| `subset.unknown-element` | a constructor that is not one of the six elements |
| `subset.unknown-expression` | an expression outside the list above |
| `subset.bad-lambda` | an action that is not an assign, `RequestRender`, or `CopyNonce` |
| `subset.bad-foreach` | a loop that is not one `Add` of an element from a list property |
| `subset.duplicate-key` | two children with the same literal key |
| `subset.mixed-keys` | a parent mixes keyed and unkeyed children |

## Scroll

`ScrollView` is one keyed child. The Android host maps it to `android.widget.ScrollView` (`FillViewport`) and reconciles that child by key, the same way a column reuses a row. The packing list is a `ScrollView` of key `scroll` around the `list` column, in `PackingScreen.cs` and in the lowered program.

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

`cnative agent a11y` prints `class`, `desc`, `text`, and `bounds` from `uiautomator dump`. A `tests/device` step `a11y <text>` asserts that dump.

## Layout, logs, and the clock

`cnative agent layout` prints `id`, `left`, `top`, `width`, and `height` for each element, from the mounted view. `cnative agent logs` prints `logcat -d --pid=<app> -s NativePoc:I NativePoc:W NativePoc:E` for the running process.

No screen starts a timer or an animation. `cnative agent clock` therefore answers `animations=0` and `clock=0` on every call. A later timer has to advance only when that verb steps it, so a test can pin the frame.
