# iOS status

This file is the status of the UIKit host after unit 37. It was written on Linux. Nothing in this file is an iOS run.

## Authored

These files exist and name the current program and record contract. They are not part of `NativePoc.slnx`.

| File | What it is |
| --- | --- |
| `spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj` | `net10.0-ios` project. Debug uses the interpreter. Release leaves `PublishAot` off. |
| `spikes/ios/NativePoc.iOS/UIKitHost.cs` | `IElementHost<UIView>` for Column, Text, Button, TextField, Switch, Scroll, List, and Image. Applies `Style`, `Motion`, and the gesture mask. |
| `spikes/ios/NativePoc.iOS/ScreenController.cs` | Renders `ScreenNav.Shared.Top()` through the same reconciler, and falls back to `Screen.Render`. |
| `spikes/ios/NativePoc.iOS/PlatformBridge.cs` | `IPlatform` for nav, effects, and the five modules (`clipboard`, `haptics`, `share`, `storage`, `theme`). |
| `spikes/ios/NativePoc.iOS/IosCatalog.cs` | `RecordProgram.ScreenId` plus `ScreenNav.Shared.Register` / `ShowRoot`. |
| `spikes/ios/NativePoc.iOS/AppDelegate.cs` | Window and root controller. |

The gesture mask uses `UITapGestureRecognizer`, `UILongPressGestureRecognizer`, `UISwipeGestureRecognizer`, and `UIPanGestureRecognizer`. A list is a `UIScrollView` of labels, the same shape as the Android row window's text slots, not a recycled table.

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
