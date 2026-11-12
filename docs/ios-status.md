# iOS status

This file is the status of the UIKit host after unit 37. It was written on Linux. Nothing in this file is an iOS run.

## Authored

These files exist and name the current program and record contract. They are not part of `NativePoc.slnx`.

| File | What it is |
| --- | --- |
| `spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj` | `net10.0-ios` project. Debug uses the interpreter. Release leaves `PublishAot` off. |
| `spikes/ios/NativePoc.iOS/UIKitHost.cs` | `IElementHost<UIView>` for Column, Text, Button, TextField, Switch, Scroll, List, Image, and Host. Applies `Style`, `Motion`, and the gesture mask. Host is a `UIView`. This file is still not compiled. |
| `spikes/ios/NativePoc.iOS/ScreenController.cs` | Renders `ScreenNav.Shared.Top()` through the same reconciler, and falls back to `Screen.Render`. |
| `spikes/ios/NativePoc.iOS/PlatformBridge.cs` | `IPlatform` for nav, effects, and the five modules (`clipboard`, `haptics`, `share`, `storage`, `theme`). |
| `spikes/ios/NativePoc.iOS/IosCatalog.cs` | `RecordProgram.ScreenId` plus `ScreenNav.Shared.Register` / `ShowRoot`. |
| `spikes/ios/NativePoc.iOS/AppDelegate.cs` | Window and root controller. |

The gesture mask uses `UITapGestureRecognizer`, `UILongPressGestureRecognizer`, `UISwipeGestureRecognizer`, and `UIPanGestureRecognizer`. A list is a `UIScrollView` of labels. It is not a recycled table. The Android row window's variable-height slots are not in this host.

## Tested on Linux

| What | How |
| --- | --- |
| `IosSurface.ViewClass` for every `ElementKind` | `spikes/ios/NativePoc.iOS.Tests`, `net10.0`, xUnit. |
| A record program still binds to a `Text` whose kind maps to `UILabel` | Same suite, `RecordProgram.Bind`. |
| The authored sources name the view classes, the five modules, `SetStyle`, `SetGesture`, `SetMotion`, `ScreenNav`, and `RecordProgram` | The suite reads the files. |
| The UIKit project is outside the Linux solution | `NativePoc.slnx` has the `net10.0` test project and not `NativePoc.iOS.csproj`. `spikes/ios/scripts/check-authored.sh` checks the same fact. |

`spikes/ios/NativePoc.iOS.Contract` is `net10.0` and is the map those tests call. It does not reference UIKit.

## Never compiled

`spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj` (`net10.0-ios`) has not been compiled on this machine. There is no iOS workload here, no Mac, and no simulator. `dotnet build NativePoc.slnx` does not build it. No binary, no simulator log, and no device log exists for it.

Do not read the authored UIKit files as a successful iOS build. The recognizer constructors, `UIImage.FromBundle`, and the app entry point are written to the current contract and have not been type-checked by the iOS SDK.

## Conformance

`PlatformConformance.Run` is the suite a screen platform has to pass: create, update, remove, text, a list, an accessibility label, a hot-reload `Apply`, a push, a pop, the heading key as the focus target, and a second `Apply` that leaves the stack at depth 2. On Linux it passes on `HeadlessHost`, on `ViewReconciler` with `MemoryHost`, and on `IosLogicSurface`. `IosLogicSurface` records that focus key. It does not construct UIKit. `IosLogicSurface` is the UIKit host's managed decisions (`HostPolicy` and `IosBridge`) on `ViewReconciler<IosView>`. `Android.Views` is not constructed here. `spikes/ios/NativePoc.iOS` (`net10.0-ios`) was not compiled. There is no iOS binary, simulator log, or device log.

`HostPolicy` is the shared floor. `AndroidHost` calls it for hidden text, the 48 dp touch floor, headings (`FontSize` at least `ThemeTokens.HeadingSize`, 22), and multiline fields. `UIKitHost` calls the same methods. `IosLogicHost` is the Linux stand-in and is what the tests construct.

Closed in portable code, and written through in the UIKit sources. The UIKit project was not built, so the `UIView` lines are authored, not run:

- `UIKitHost.Remove` removes a stack child and a scroll child. Scroll insert keeps the index by laying the host's child list out again. `IosLogicHost` does the same, and a test removes the first scroll child.
- `SetText` sets `AccessibilityValue` from `HostPolicy.TextAccessibility` and hides an empty string. `AndroidHost.SetText` uses the same two calls for `ContentDescription` and `Gone`.
- `SetList` labels each row with `HostPolicy.RowText`, including the first text inside a column, and sets `AccessibilityValue` to `HostPolicy.ListSummary` (`count=` plus the row count). `ScreenController` prints `ScreenSnapshot.FormatTree`, which is the dump the suite reads.
- `SetGesture` adds the same recognizers and, when the mask is nonzero, a `nativepoc-touch` constraint of `HostPolicy.TouchFloorDp` (48). `AndroidHost.SetGesture` calls `TouchTarget` for that same value.
- `SetStyle` sets `UIAccessibilityTrait.Header` when `HostPolicy.IsHeading` is true. `AndroidHost` sets `AccessibilityHeading` for the same font size.
- `SetFieldMode` calls `HostPolicy.FieldIsMultiline`. A multiline field shows the `UITextView` inside `FieldBox`. A password stays the `UITextField`. The Linux host records `UITextView` or `UITextField`.
- `ScreenController.Apply` binds a `RecordProgram` and renders that tree on the next paint. `IosLogicSurface.Apply` is the path `PlatformConformance` runs.
- `IosPlatform` implements `CopyText`, `WriteBack`, `Push(string, object)`, and `Continue` by calling `IosBridge`. The bridge dispatches clipboard, haptics, share, storage set and get, theme, animate, http, and location. A Linux `IIosServices` double covers storage, http, location, and those four methods. `IosNet.Send` is `NSUrlSession` and `IosLocation.Read` is `CLLocationManager`. Neither has been compiled or run.
- `IosPlatform.Push` writes `nav-focus heading=` plus the screen id. That line is in `spikes/ios/NativePoc.iOS/PlatformBridge.cs`. The file was not compiled. `IosBridge.Modules` does not dispatch `nav`. The Android log of the same name is `NavFocus`, compiled only when `NativePocNav` is true. On Android, `Show` does not push a catalog, and a cold launch of one screen is depth 1. That Android behavior was emulator-run. The UIKit project was not built, so there is no iOS depth log.

Still not a build:

- The list is not a recycled table. The Android row window is not in this host.
- `NSUrlSession` and `CLLocationManager` are written in `PlatformBridge.cs` and have not been type-checked by the iOS SDK. A denied fix is the string `Location.Parse` already accepts. A granted fix from a simulator was not produced.
- No iOS binary, simulator log, or device log exists. Do not read this section as an iOS run.
- The hot-reload error overlay is Android `DevOverlay`, compiled only for a Debug listener. It was not added to the UIKit host. The startup phase logs (`state-load`, `nav-restore`, `persist-read`) are Android. This wave did not compile `net10.0-ios`.
- Swipe-back and `Animated` decide in `MotionContract` (`Commits`, `SettleMs`, `TransitionMs`, `AnimatedMs`). A commit is progress at least 0.33, or at least 0.12 with a velocity above 900. Either system scale at 0 applies the end value and asks for no frame. `NavMotion` and `NavTransition` call that contract. The authored UIKit host maps it to `UINavigationController` interactive pop and to `UIView` animations ticked by `CADisplayLink` (`UIKitHost.cs`, `MotionMap` on the net10.0 stand-in). Night mode and font scale are still Android. `net10.0-ios` was not compiled. There is no iOS binary, simulator log, or device log.
- A declared Android binding is `[JniType]` and `[JniMethod]` on a partial class. `JniBindingGenerator` emits `JNIEnv` calls. The iOS shape is the same declaration with an `[ObjcType]` / `[ObjcMethod]` generator that would emit `objc_msgSend`. That generator is not written. `net10.0-ios` was not compiled. This iOS shape is unverified.
- Northroom's four screens render on `HeadlessHost`. `Every_Northroom_screen_runs_on_the_headless_host` checks the 24 dp inset, the shared back arrow, Keep, and font scale 2. The UIKit host was not compiled. `net10.0-ios` was not compiled.

## Northroom widgets

This table is the UIKit host for the widgets Northroom actually builds. It was written from `samples/showcase` and `spikes/ios/NativePoc.iOS/UIKitHost.cs`. The UIKit project was not compiled. `net10.0-ios` was not compiled. There is no iOS binary.

| Northroom | UIKit view the host creates | Missing in that host |
| --- | --- | --- |
| `ScrollView` on home, the piece, and the room. The shelf is a `ListView` | `UIScrollView` for those scroll screens | The shelf grid is not a recycled collection here. `LayoutScroll` places subviews again and does not keep `ContentOffset`. |
| `Column` | `UIStackView`, vertical | Padding is not applied. `HeadlessLayout` does apply it. |
| `Column` with `Style.Across` (the stats row and the back arrow row). The shelf grid is a `ListView` with `Across` 2 | the same vertical `UIStackView` | `SetStyle` does not read `Across`, so the row stays vertical. `HeadlessLayout` splits a column and lays that shelf list in columns. |
| `Text` | `UILabel` | Bold, color, size, and the heading trait are set. |
| `Button` | `UIButton` (`UIButtonType.System`) | `Style.Wash` (the second gradient stop), `Style.Stroke` (the Keep outline), and elevation are not set. `Style.Radius` is `CornerRadius`. |
| `Switch` on the room (Night, Large type) | `UISwitch` | The host sets the value. Night mode and font scale are not read here. |
| `Style.Wash` on the home banner and the shelf tiles | no layer | No gradient layer. The style value stays on the element. `Northroom_wash_stroke_and_across_remain_on_the_headless_host` reads it. |
| `Style.Stroke` on Keep before it is kept | no border | No stroke. The same headless test reads `0xFF0F6E56`. |
| `Style.Radius` on cards and the pill | `Layer.CornerRadius`, clips | No shadow. |
| System-bar inset, 24 dp | none | The host does not pad for bars. `HeadlessLayout.MeasureBelowBars` shifts the tree. |
| `Theme.Mode` and `Theme.Scale` | none | Android repaints and recreates for scale. The UIKit host has no `fontScale` read. |
| `Nav.Push` / `Nav.Pop` | `IosPlatform.Push` and `Pop`, authored | The project was not compiled. |
| `Persist.Put` for `northroom.keep` | not this call | `IosBridge` has storage set and get. It does not write `nativepoc-persist`. |

`Keyed_shelf_insert_delete_and_rename_keep_the_scroll` is the portable half of the scroll row: the scroll view and a tile whose key stays are the same objects, and a scroll offset stored on that view stays, after a delete, an insert, and a key rename. The renamed tile is a new view. That test does not construct UIKit.
