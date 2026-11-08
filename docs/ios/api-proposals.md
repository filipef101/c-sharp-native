# Core changes that were not made

`Screen.Render` already has the counter, the field, the button, and the empty greeting. `IElementHost` already has create, insert, remove, property updates, mount, retarget, and unmount. `IPlatform` already has `CopyNonce` and `RequestRender`. The iOS host uses those members. No edit to `src/` was required for the spike, and none was made.

## Loader

Do not add `AssemblyLoadContext` to Core or to the iOS app. Android's collectible load is a Debug server in the Android project. Copying it into shared code would make every host carry a mechanism iOS Release cannot run. The proposal is to leave reload in the app project that can support it. The iOS Debug handler only requests another render, and only when `MetadataUpdater.IsSupported` is true.

## Main thread

`RequestRender` is called from a control callback. On Android that callback is on the UI thread, and `MainActivity` still marshals if it is not. `ScreenController` does the same with `NSThread.IsMain` and `InvokeOnMainThread`. A future `IPlatform.Post(Action work)` would matter if a handler started off the main thread. Nothing in this screen does that. The method was not added.

## Text field cursor

`SetTextField` is emitted when the value, placeholder, or accessibility label changes. The host skips the write when the control already has the same string. That keeps a render during typing from replacing the field. The reconciler already skips the patch when the element value did not change. A new Core flag for the same comparison would be a second copy of one rule. It was not added.

## Accessibility role

`Button` is a `UIButton`, so the button trait comes from the control. `AccessibilityLabel` is already on the element. A role enum was not added.

## Clipboard

`CopyNonce` throws when the value read back differs. `PlatformBridge` uses `UIPasteboard` and then throws the same way `PlatformBridge` does on Android. If a simulator read returns null, record it on the Mac. Do not drop the throw in Core to hide that result.
