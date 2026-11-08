# iOS host shape

The caller is the existing screen. `Screen.Render` stays the function a button and a field invoke. The iOS process adds a UIKit host behind `IElementHost` and does not add a type that `Screen` can see.

## Problem

Core already has the tree, the diff, and the reconciler. The iOS work is a second host plus a runtime choice. Two traps sit next to that host. A shared policy library would duplicate rules Android already inlines. A port of `HotReloadServer` would load IL that device AOT cannot run.

## Usage

The Mac command is `sh spikes/ios/scripts/simulator.sh`.

That script runs `dotnet run --project spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj -c Debug -f net10.0-ios -r iossimulator-arm64`.

The app calls `Screen.Render(AppSession.Shared.State, platform)` and gives the returned tree to `ViewReconciler<UIView>`. Save still calls `IPlatform.CopyNonce` and `IPlatform.RequestRender`. No new method is required at that call.

## Shape

The data is the existing `Element` tree and `AppState`. `UIKitHost` is the adapter. `ScreenController` owns the reconciler, the root view, and the render coalesce flags. `PlatformBridge` is the clipboard and the render request. UIKit types do not leave those three types.

`IElementHost<UIView>` is the public surface the reconciler already uses. The host hides stack insert, listener lifetime, and the field echo flag. Callers do not sequence those steps. The interface did not grow.

Debug reload, if a Mac run shows `MetadataUpdater.IsSupported`, is `HotReload.UpdateApplication` calling `RequestRender`. There is one reload path in this spike. There is no assembly loader.

Per laziness, the echo flag and the coalesce flags stay private fields, matching `AndroidHost` and `MainActivity`. Per boundary discipline, the pasteboard read is checked at `PlatformBridge` and the screen function stays free of UIKit. Per model-the-domain, no new state machine was added. The tree is already the model.

## Synthesis decision

Two shapes were written down in this run. No second model was spawned. See `docs/ios/poteto-log.md`.

Shape A was a `net10.0` policy library with field-echo, render coalesce, and nonce check types, referenced by the UIKit host and by tests. It lost. The library would be a pass-through in front of UIKit events, and it would be a second copy of policy that `AndroidHost` already owns. That is split ownership and two ways to do one task.

Shape B is the app-local host above. It is the base. The Linux check was taken from the need shape A was trying to meet, without the library. `check-authored.sh` locks the project file, and the `net10.0` tests call `Screen.Render`.

## Tradeoffs accepted

- We accept an uncompiled UIKit file in exchange for not pretending a `net10.0` test is a simulator run.
- We accept Release `TrimMode` left unset in exchange for measuring the SDK default on the first device build.
- We accept a Debug-only `MetadataUpdateHandler` that may never run, in exchange for having the call site ready if the interpreter reports support.
- We accept grep checks on the csproj text in exchange for a check this Linux VM can re-run. MSBuild reads that text. A rename of `UseInterpreter` would change the Mac build.

## Alternatives considered

Shape A, the policy library, hides almost nothing and forces the next reader to learn both the library and UIKit. Shape B hides the UIKit details behind the interface that already exists.

A third shape, porting `HotReloadServer` into the iOS app, was rejected before sketching types. The runtime cannot execute the loaded IL in the configurations that matter. See `docs/ios/feasibility.md`.

## Open questions and risks

Does `InsertArrangedSubview` take `nuint` in the .NET 10 binding this workload installs? The first Mac compile answers that.

Does `UIPasteboard.General.String` round-trip on the simulator without a prompt? The first Mac run answers that. A mismatch should throw, as it does on Android.

Does arm64 Debug print interpreter flags and then accept a method-body edit? That is the conditional hot-reload hypothesis. It is unanswered.

## Next implementation step

On a Mac that already has Xcode 26 and the `ios` workload, run `sh spikes/ios/scripts/simulator.sh` and read the `NativePoc runtime` line. Do not start that work by editing Core.
