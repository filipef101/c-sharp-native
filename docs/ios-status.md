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

## Conformance gap

`PlatformConformance.Run` is the suite a screen platform has to pass: create, update, remove, text, a list, an accessibility label, and a hot-reload `Apply`. It ran on Linux against `HeadlessHost` and against `ViewReconciler` with `MemoryHost`. That reconciler is the managed side `AndroidHost` implements `IElementHost<View>` for. `Android.Views` is not constructed here. The UIKit project was not built. There is no iOS binary, simulator log, or device log. The list below is a reading of `spikes/ios`, not a run.

To pass the same suite, `spikes/ios` would need these changes:

- `UIKitHost.Remove` throws unless the parent is a `UIStackView`. The suite removes a column child. A scroll child cannot be removed, and `Insert` into a `UIScrollView` ignores the index.
- `SetText` writes `UILabel.Text` only. It does not set an accessibility value, and an empty string stays visible. `AndroidHost.SetText` sets `ContentDescription` and `Gone`.
- `SetList` clears the scroll and adds a `UILabel` per row, and a row that is not `Text` becomes an empty label. It is not a recycled list. Nothing on this host prints the accessibility dump the suite reads (`count=2`, `label="Save name"`).
- `SetGesture` adds recognizers and does not set a 48 pt minimum. `AndroidHost.SetGesture` calls `TouchTarget` when the mask is nonzero. A long-press name would stay at the label's line height.
- `SetStyle` sets the font and color. It does not mark a heading. `AndroidHost` sets `AccessibilityHeading` when `FontSize` is at least 20.
- `SetFieldMode` ignores `lines`, so a multiline field stays a single-line `UITextField`.
- There is no `Apply` of a `RecordProgram`. `IosPlatform.RequestRender` asks `ScreenController` to render the compiled `ScreenNav` tree. A hot-reload program would not replace that tree or keep a bag.
- `IosPlatform.Module` throws on a module other than clipboard, haptics, share, storage set, and theme. `http` and `location` are not implemented. `CopyText`, `WriteBack`, `Push(row)`, and `Continue` are the interface defaults.

None of that was compiled or run.
