# Native view

A `HostView` is an element that owns one platform view. The record program names the view and its prop. The host builds that view with a direct constructor call. There is no reflection, no `Activator`, and no assembly load, so the NativeAOT compiler sees the type.

```csharp
new HostView("webview", "url", state.Url)
{
    Key = "page",
    Event = "navigate",
    OnEvent = value =>
    {
        state.Url = value;
        platform.RequestRender();
    },
}
```

`Kind` chooses the view. `Prop` and `Value` are the typed property. `Event` is the event name. `OnEvent` receives the payload when the platform view raises that name. A different name is ignored. The sample is `samples/NativePoc.Web/WebScreen.cs`. The Android arm is `NativeViews`: `webview` constructs `Android.Webkit.WebView`, `url` calls `LoadUrl` for `http`, `https`, and `about`, and any other value is HTML passed to `LoadData`. A navigation raises `navigate`.

The reconciler mounts one `HostSlot` for the element. A later render with the same kind keeps that slot and writes the new prop. The slot loads the URL again only when the kind, the prop name, or the value changes. A different kind removes the inner view and constructs the new one in the same slot. The element key does not change, so a reload does not remount the slot.

## A view from an AAR

Reference the AAR from the Android project. Add one arm to `NativeViews.Create` that calls the view's constructor, one arm in `Apply` for each prop you support, and a listener in `Listen` that calls `emit("event-name", payload)`. The AOT compiler and the Android linker both see that constructor. A kind that is not in the switch throws `Unknown native view`. It is not looked up by name at runtime.

The UIKit host maps `ElementKind.Host` to `UIView`. That host is authored and is not compiled here.
