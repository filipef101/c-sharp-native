# Runtime feasibility

This file is the plan. The Android proof of concept that followed it, and every number measured on this machine, is in [android-poc-report.md](android-poc-report.md). Figures below are from the cited docs or are the plan's acceptance gates. They are not the measurements.

Retrieved 2026-11-08.

Tags:

- **Fact.** A statement taken from the cited primary page or repo.
- **Vendor claim.** A project's own docs, not re-tested here.
- **Inference.** A conclusion from those sources. The basis is named.

Scope of this revision: a plan for both Android and iOS. The first goal is a fully working Android proof of concept. iOS is phase two. A separate section answers whether React Native Turbo Modules and Fabric components, Expo modules, or NativeScript plugins can be reused, and whether any of that belongs in the Android proof of concept.

## Decision

Build a small C# element tree and reconcile it onto real `android.view.View` instances, using the .NET for Android SDK bindings. Ship that as a `net10.0-android` app, not a .NET MAUI app. Debug it with Mono's default Debug configuration and `dotnet watch`. On a non-MAUI Android app that default packages the interpreter instead of the JIT. Publish a Release build with Mono AOT, which is the .NET 10 Android default, and also publish NativeAOT and record the result. NativeAOT on Android is still experimental.

Keep the element types, the state object, and the prop updates free of reflection and free of Android types, so the same tree can later drive UIKit. Hot reload stays a debug host that patches method bodies. It does not load new assemblies in the shipping app.

Do not put a JavaScript engine, a JSI host, or the Expo module runtime in the process. Compatibility with existing React Native, Expo, and NativeScript packages is a later, narrow binding task: bind an AAR whose public Java API does not mention those hosts. It is not part of the Android proof of concept.

## Runtime facts that constrain the plan

### .NET 9 and .NET 10

**Fact.** For the .NET MAUI 8/9/10 documentation moniker, Mono is the mobile runtime. The summary table describes Android Debug as Mono + JIT + interpreter, and Android Release as Mono + Mono AOT. iOS Debug is Mono + JIT on x64, and Mono + AOT + interpreter on ARM64. iOS Release is Mono + Mono AOT, and full AOT (no interpreter and no JIT) is the default for Mono release builds on iOS and Mac Catalyst. [Runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0).

**Fact.** The .NET for Android property page describes `AndroidUseInterpreter` differently. The property makes a Mono APK contain the interpreter and not the normal JIT. It defaults to true for Debug builds that use Mono, and false otherwise. It applies to .NET 10 and earlier, and .NET 11 Android has no interpreter. [Build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties).

**Inference.** Those two pages disagree about a default Android Debug process. The MAUI table lists a JIT. The Android workload property, which is the one a non-MAUI `net10.0-android` app actually reads, says Debug replaces the JIT with the interpreter. The proof logs `RuntimeFeature.IsDynamicCodeSupported` and `RuntimeFeature.IsDynamicCodeCompiled` at startup. Hot-reload samples count as evidence for the later iOS port only when dynamic code is supported and not JIT-compiled. That is also the runtime's own Mono test gate for `MetadataUpdater.ApplyUpdate`. [ApplyUpdateUtil.cs](https://github.com/dotnet/runtime/blob/9a975e65/src/libraries/System.Runtime.Loader/tests/ApplyUpdateUtil.cs).

**Fact.** The MAUI runtimes page says enabling the interpreter on Android Release disables JIT compilation. Combined with `AndroidUseInterpreter` defaulting to false outside Debug, the default Android Release Mono AOT build still has a JIT for code that was not precompiled. An Android Release success does not, by itself, prove an iOS full-AOT or NativeAOT success.

**Fact.** NativeAOT on .NET MAUI 8/9/10 is an opt-in publish for iOS and Mac Catalyst (`PublishAot=true`). It is not the Debug runtime. It has no JIT and no interpreter, and the Mono interpreter properties do nothing under NativeAOT. [Native AOT deployment on iOS and Mac Catalyst](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0).

**Fact.** The runtime's NativeAOT target table for .NET 9+ lists iOS Arm64 and iOS Simulator x64/Arm64 as supported, and lists Android x64, Arm64, and Arm as "Experimental, no built-in Java interop." [.NET Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/). Java interop for Android apps is layered on by .NET for Android and is still warned as experimental (XA1040). [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040).

**Fact.** In .NET 10, CoreCLR on Android is an experimental opt-in (`UseMonoRuntime=false`), not the default. [Runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0).

**Fact.** Published template numbers on that page, for `dotnet new maui`, hardware-dependent, under the .NET 8/9/10 moniker: startup about 642 ms Mono vs 274 ms NativeAOT on Android, and about 275 ms vs 130 ms on iOS. iOS app size about 14.3 MB Mono, 11.5 MB Mono with full trimming, 5.4 MB NativeAOT. The .NET 11 section of the same page does not repeat these numbers. They are a reference point, not a pass line for this plan.

**Fact.** A Mono Android APK carries `classes.dex`, `libmonosgen-2.0.so`, `libmonodroid.so`, a packed assembly blob, and in Release `libaot-*.dll.so`. A NativeAOT Android APK carries `classes.dex` and one native library with the runtime linked in. [Runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0). Each extra ABI copies those native libraries.

### .NET 11

**Fact.** The .NET 11 release-notes schedule, as retrieved 2026-11-08, lists 11.0.0-rc.3 on 2026-10-25 as the latest dated build and states a support window starting 2026-11-10. [release notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/README.md). This plan uses .NET 10 GA as the proof-of-concept baseline so the result is not tied to a runtime that was still in release candidate when the plan was written.

**Fact.** In .NET 11, CoreCLR is the default on Android, iOS, and Mac Catalyst. Mono is unsupported: `UseMonoRuntime=true` is rejected (NETSDK1242). Android Debug is CoreCLR + JIT. Android Release is CoreCLR + composite partial ReadyToRun + JIT. iOS Debug and Release use ReadyToRun plus the CoreCLR interpreter, because Apple platforms do not allow JIT. NativeAOT is opt-in on publish and experimental on Android. [Runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0), [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040), [What's new in .NET MAUI for .NET 11](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11).

**Fact.** `RunAOTCompilation` is the Mono AOT switch. On CoreCLR or NativeAOT it produces XA1044. .NET 11 Android has no interpreter. [Build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties), [XA1044](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1044).

**Inference.** Mono AOT versus NativeAOT is a .NET 10 measurement. A .NET 11 port of the same app would instead compare CoreCLR + ReadyToRun against experimental NativeAOT. The proof of concept stays on `net10.0-android` so both numbers exist. A .NET 11 publish is worth recording if the RC SDK is installed. It is not the pass gate.

### Trimming and a reflection-shaped UI

**Fact.** NativeAOT always full-trims. It cannot `Assembly.LoadFile`, cannot `System.Reflection.Emit`, and flags unconstrained reflection. [Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [Native AOT security](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/security).

**Fact.** MAUI documents these as incompatible with full trimming and NativeAOT: string binding paths, runtime `LoadFromXaml`, `QueryPropertyAttribute`, `OnPlatform` / `OnIdiom` markup extensions, and `SearchHandler.DisplayMemberName`. `DynamicallyAccessedMembers` can preserve a known set of members. Microsoft describes that approach as error-prone for open-ended XAML and does not recommend it. Compiled bindings are required instead. [Trim a .NET MAUI app](https://learn.microsoft.com/en-us/dotnet/maui/deployment/trimming?view=net-maui-10.0), [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0).

**Fact.** `DllImport` marshalling stubs are generated at runtime and are not the NativeAOT path. Source-generated `LibraryImport` is. [P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation).

**Fact.** On iOS, the dynamic registrar uses runtime reflection. NativeAOT needs a static registrar. .NET 11 RC 1 uses the trimmable-static registrar by default for NativeAOT. [Build properties](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-properties), [What's new in .NET 11](https://github.com/dotnet/docs-maui/blob/main/docs/whats-new/dotnet-11.md), [trimmable-static registrar](https://github.com/dotnet/macios/pull/25079). Android NativeAOT defaults `AndroidTypeMapImplementation` to `trimmable` when `PublishAot` is true. [Build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties).

**Inference.** A UI layer that sets properties by name (`GetProperty(name).SetValue`) will run while the trimmer is off, which is the Debug default, and then lose those members under full trim and NativeAOT. The Android proof of concept applies props with direct calls or source-generated setters so Debug and the NativeAOT publish stay on the same code.

The pattern that fails the gate:

```csharp
void Apply(object view, string property, object value)
{
	view.GetType().GetProperty(property)!.SetValue(view, value);
}
```

The pattern that can be trimmed:

```csharp
static void ApplyTitle(TextView view, string next, string? previous)
{
	if (previous is null || previous != next)
		view.Text = next;
}
```

## Architectures considered

| Host | What the user touches | Accessibility | Fits this project |
| --- | --- | --- | --- |
| Native widgets | Real `android.view.View` and, later, `UIView` | The OS tree. MAUI maps semantic properties onto that tree and deliberately does not force the platforms to match. [MAUI accessibility](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0) | This is the proof of concept. |
| Drawn renderer (Skia, or Flutter's Impeller) | One canvas | A second semantics tree must be bridged. Flutter documents that standard widgets build a semantics tree and that TalkBack and VoiceOver read the platform tree produced from it. [Flutter assistive technologies](https://docs.flutter.dev/ui/accessibility/assistive-technologies). Uno's own table marks Android and iOS Skia screen-reader support as in progress, and native rendering as supported. [Uno accessibility](https://platform.uno/docs/articles/features/accessibility/index.html), [Uno 6.6](https://platform.uno/blog/uno-platform-6-6/). Avalonia draws with Skia on mobile and states that VoiceOver and TalkBack see its controls. [Avalonia mobile](https://avaloniaui.net/avalonia/mobile). An earlier Avalonia change described Android as an opaque `SurfaceView` and added a TalkBack bridge. [Avalonia PR 17704](https://github.com/AvaloniaUI/Avalonia/pull/17704) | Wrong default. Text input, IME, and the accessibility bridge are a second product. |
| Wrapper over MAUI handlers | MAUI controls, plus a C# component API | Inherits MAUI's native handlers and their gaps | MauiReactor already demonstrates the developer experience. [MauiReactor on the .NET blog](https://devblogs.microsoft.com/dotnet/mauireactor-mvu-for-dotnet-maui/). The proof of concept would then measure MAUI's size and trim surface, not a thinner host. |

**Inference.** Impeller is Flutter's renderer. There is no .NET Impeller host to adopt. SkiaSharp would be the drawn path, and it is out of scope for the first proof of concept.

## Android proof of concept

### What "fully working" means

One installable Android app. One screen. The screen is a column with a title, a text field, a button, and a line of result text. Typing, pressing the button, and rotating the activity all work. A string change in the component function updates the title in place and leaves the typed text in place. A Release Mono AOT build does the same screen without hot reload. A NativeAOT publish is produced and the outcome is written down. A platform API is called through the SDK bindings with no binding project in the tree.

Out of scope for this proof: lists, images, navigation, animation, a style system, custom fonts, multiple activities, a JavaScript engine, Expo or React Native hosting, iOS, and the Play App Bundle. Those are later products. They are not required to call the proof of concept done.

### SDK and project shape

- Target `net10.0-android`. One RID for measurements: `android-arm64`.
- An SDK-style Android application project. It does not reference MAUI packages.
- `IsAotCompatible=true` so trim and AOT analyzers run while editing, not only when publishing NativeAOT. [Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
- Release Mono configuration: `PublishTrimmed=true`, `TrimMode=partial` (MAUI's Release default), and the workload's default `RunAOTCompilation=true` for .NET 10 Mono Release. Do not set `PublishAot` on this configuration.
- A second Release publish with `PublishAot=true` and `EnablePreviewFeatures=true`. Expect warning XA1040. Do not set `TrimMode`; NativeAOT full-trims itself. [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040).
- Minimum Android API: whatever the .NET 10 Android workload defaults to. The virtual device should be API 34 or newer so the result is not an old-API special case. .NET 11's minimum of API 24 is a later constraint, not this build's. [What's new in .NET 11](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11).

**Inference.** `dotnet new android` is the likely template once the `android` workload is installed. Confirm with `dotnet new list` at the start of the spike. The pass conditions do not depend on the template name. They depend on the absence of MAUI.

### Concrete architecture

Four layers, even though the proof is one project. The split is what makes the iOS port a second reconciler rather than a rewrite.

1. **State.** A plain class the activity does not own. Survives `Activity.Recreate()` because it is not a field of the activity. Process death may drop it. That is acceptable here.
2. **Elements.** Immutable records. No `Android.*` types, no `Java.Lang` types.
3. **Render function.** `Element Render(AppState state)`. Reads state. Returns a tree. Event handlers close over the state and then ask the reconciler to render again, on the UI thread.
4. **Android reconciler.** Walks the tree on the UI thread. Creates `LinearLayout`, `TextView`, `Button`, and `EditText`. Updates properties when the new tree differs. Removes views that disappeared. Does not rebuild the activity.

Illustrative only. This was not compiled.

```csharp
abstract record Element;

sealed record Column(IReadOnlyList<Element> Children) : Element;

sealed record Text(string Value) : Element;

sealed record Button(string Title, Action OnPress, string AccessibilityLabel) : Element;

sealed record TextField(string Value, Action<string> OnChange, string Placeholder) : Element;

static Element Render(AppState state) => new Column(
[
	new Text("Counter " + state.Count),
	new TextField(state.Name, value =>
	{
		state.Name = value;
		RequestRender();
	}, "Name"),
	new Button("Save", () =>
	{
		state.Count++;
		RequestRender();
	}, "Save name"),
	new Text(state.Name.Length == 0 ? "Enter a name" : "Hello " + state.Name),
]);
```

Identity for this proof is sibling index. That is enough for a fixed four-node screen. A list would need explicit keys. Do not add lists to make the proof look more complete.

Prop updates are direct assignments in the reconciler (`view.Text = ...`, `button.Text = ...`, `button.ContentDescription = ...`). A source generator is the right follow-on once there is a second platform. Hand-written setters for four widgets are the smaller proof and they are already trim-safe.

The button's click listener is attached once, when the view is created, and reads the current handler from the latest element. Replacing the `Click` delegate on every render is a leak. **Inference.** This is ordinary Android listener hygiene, and it is also how a later iOS `TouchUpInside` handler should work.

UI-thread hops go through one helper that posts to the Android looper. The iOS port replaces that helper with `InvokeOnMainThread`. Call sites in the render function use the helper, not `Looper` directly.

### Minimal component set

| Element | Android widget | Behavior the proof must show |
| --- | --- | --- |
| `Column` | vertical `LinearLayout` | Children appear in order. |
| `Text` | `TextView` | String replacement updates the existing instance. |
| `Button` | `android.widget.Button` | Click increments state. `ContentDescription` is set. |
| `TextField` | `EditText` | Keystrokes update state. The widget is not recreated while typing, so the cursor is not thrown away. |

Empty state is the greeting `"Enter a name"` when `Name` is empty. The button still increments the counter in that state.

### Hot reload

**Fact.** On Mono, hot reload depends on the interpreter. For a .NET for Android Debug build, `AndroidUseInterpreter` defaults to true and is documented as shipping the interpreter instead of the JIT. [Build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties). iOS ARM64 has no JIT either. [Runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0). A hot-reload success in that Android Debug configuration is the right shape of evidence for iOS, and it is still not sufficient: iOS Debug is AOT plus the interpreter, with linking required to be off, and iOS Release is full AOT.

**Fact.** .NET hot reload applies method-body edits through `MetadataUpdater`. Adding a method can be supported. Renames, deleting members, and signature changes are rude edits and need a restart. [Supported code changes](https://learn.microsoft.com/en-us/visualstudio/debugger/supported-code-changes-csharp). The runtime test gate enables `ApplyUpdate` on Mono only when the interpreter is the execution engine, the process is not Mono AOT, and the runtime advertises capabilities. [ApplyUpdateUtil.cs](https://github.com/dotnet/runtime/blob/9a975e65/src/libraries/System.Runtime.Loader/tests/ApplyUpdateUtil.cs).

**Fact.** Visual Studio's hot-reload matrix lists MAUI (WinUI, Android, iOS) as working with the debugger attached, not working without the debugger, and without `MetadataUpdateHandler` support in that matrix. [Hot Reload](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload). `dotnet watch` is still the tool the proof should try first, because the .NET 11 MAUI notes describe hot reload via the IDEs and `dotnet watch` on CoreCLR, and the Mono interpreter is what makes hot reload possible on the .NET 10 Mono runtime. [CoreCLR progress](https://devblogs.microsoft.com/dotnet/coreclr-progress-and-mono-timeline-dotnet-maui/).

**Fact.** NativeAOT has no interpreter and cannot load assemblies. Hot reload is a Debug activity. MAUI's own guidance is to develop on the normal runtime and publish NativeAOT at the end. [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0).

Approach for the proof:

- Debug build, interpreter left at the default, linker/trimmer off.
- Change a string literal inside `Render`. That is a method-body edit.
- `dotnet watch` applies the delta.
- A `MetadataUpdateHandler` on the application type posts `Render` to the UI thread after an update. Guard it with `MetadataUpdater.IsSupported` so Release and NativeAOT trim it out.
- The handler is what makes the title change without a further tap. If the runtime applies the new IL but never calls the handler, the proof records that and the hot-reload gate fails. Do not "fix" it by reinstalling the APK.

**Inference.** An assembly-swap loader (the MauiReactor model: recompile, send a DLL, `Assembly.Load`, copy state) can run anywhere the interpreter or a JIT will execute new IL. MauiReactor documents that model for Android and iOS. [MauiReactor hot reload, vendor description](https://devblogs.microsoft.com/dotnet/mauireactor-mvu-for-dotnet-maui/). It is the wrong mechanism for this proof. NativeAOT cannot load that assembly, iOS Release full AOT is not a place to ship a loader, and method-body edit-and-continue is the smaller instrument. Rude edits (a new field on `AppState`) are allowed to require a restart in the proof. Record one rude edit and confirm the process restarts or the edit is refused. Passing that case is not required.

### Native API access

The component set does not wrap the Android SDK. The screen's Save handler calls the clipboard through the bindings that ship with .NET for Android.

Illustrative only.

```csharp
static void CopyNonce(Activity activity, string nonce)
{
	var clipboard = (ClipboardManager)activity.GetSystemService(Context.ClipboardService)!;
	clipboard.PrimaryClip = ClipData.NewPlainText("nonce", nonce);
	var read = clipboard.PrimaryClip?.GetItemAt(0)?.Text;
	if (read != nonce)
		throw new InvalidOperationException("Clipboard round-trip failed.");
}
```

No `ApiDefinition`, no `Metadata.xml`, no binding project. The call is a stand-in for "the developer reached a platform API the framework did not wrap." **Fact.** The Android binding pipeline already generates those SDK wrappers: `class-parse` reads `.class` files into `api.xml`, optional Java source parsing adds parameter names, `generator` emits C#, and the C# compiler produces the assembly. [dotnet/android blueprint](https://github.com/dotnet/android/wiki/Blueprint).

**Inference.** A NativeScript-style open metadata blob plus a generic JNI invoker is possible, because JNI's `Call*MethodA` family is a fixed set of entry points and can be `LibraryImport`ed. It is a worse developer experience than the generated SDK (no types, the trimmer cannot see which Java methods a string name uses) and it duplicates `generator`. The proof does not build one. Third-party AARs, later, go through that same generator at build time.

### Build and AOT

Three builds, all `android-arm64`:

| Build | Command shape | Role |
| --- | --- | --- |
| Debug | `dotnet build -c Debug -f net10.0-android -r android-arm64` | Hot reload and the interactive screen. |
| Release Mono AOT | `dotnet publish -c Release -f net10.0-android -r android-arm64` with trimming as above, `PublishAot` unset | Functional ship gate on .NET 10. |
| NativeAOT | `dotnet publish -c Release -f net10.0-android -r android-arm64 -p:PublishAot=true -p:EnablePreviewFeatures=true` | Required measurement. Experimental. |

Confirm the Mono Release APK actually contains `lib/arm64-v8a/libaot-*.dll.so` (or the current equivalent the workload emits). Confirm the NativeAOT APK contains a single app native library and `classes.dex`, matching the documented layout.

**Fact.** XA1040's text is that Android NativeAOT is not yet suitable for production. [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040). A NativeAOT failure is a real result about the runtime. It does not erase a Mono AOT proof that meets the functional gates. The written verdict must then say the screen works on Mono AOT and is not yet NativeAOT-shippable. A NativeAOT success (the screen runs, and the proof's own source has no IL2026, IL2070, IL2072, IL2075, IL3050, or IL3056) is what makes the architecture a candidate for a later NativeAOT ship configuration.

Warnings inside the Android workload or the runtime pack are listed and do not fail the proof. Warnings whose source file is in this repo do.

### Acceptance criteria

All of these are measured on one Android Virtual Device, API 34 or newer, with the package force-stopped between cold starts. Prefer an `arm64-v8a` image so the native libraries match a phone. An `x86_64` image is valid for the functional tests: unlike iOS, both Android ABIs can run the interpreter, and the startup log says which engine ran. Size and startup comparisons must use the same RID as the MAUI template built on the same machine. No number below was measured for this plan.

Functional, Debug:

1. Process starts and shows the four widgets. `adb shell uiautomator dump` (or a walk of the live view tree logged by the app) finds a `android.widget.Button` whose content description is `Save name`, a `TextView` containing `Counter 0`, and an `EditText`. The startup log includes `RuntimeFeature.IsDynamicCodeSupported` and `RuntimeFeature.IsDynamicCodeCompiled`. The hot-reload items below are evidence for the iOS phase only if supported is true and compiled is false. If compiled is true, the Debug build is a JIT and the run is repeated with `AndroidUseInterpreter=true` forced on.
2. Enter `Ada` in the field. The greeting becomes `Hello Ada`. The `EditText` instance identity (the Java handle) is unchanged across those keystrokes. Log the handle. 20 keystrokes, 20 matches.
3. Tap Save. The counter text becomes `Counter 1`. The field still contains `Ada`.
4. With the field empty, the greeting is `Enter a name`, and Save still increments the counter.
5. `Activity.Recreate()` (a debug affordance or an instrumentation call) leaves `Ada` in the field and leaves the counter value. One manual rotation on the emulator is recorded as well.
6. Save copies a nonce through `ClipboardManager` and reads it back. The test fails if the read value differs. There is no binding project in the repository.

Hot reload, Debug, n = 20, same process:

7. With `Ada` in the field, change the literal `"Counter "` to `"Tally "` and save.
8. p50 time from the file's save timestamp to the on-screen text `Tally <n>` is ≤ 1500 ms. p95 ≤ 3000 ms.
9. `adb shell pidof <package>` is unchanged for every successful sample.
10. The field still contains `Ada` after every sample.
11. A rude edit (add a field to `AppState`) is attempted once. The log records whether the runtime refused it or the process restarted. This item does not have to succeed as an in-place update.

A reinstall, a new pid, or a wiped field fails items 7–10 even if the new string appears quickly.

Release Mono AOT:

12. The published APK installs and items 1–6 pass on that build. Hot reload is not required.
13. `libaot-*.so` (or the workload's current Mono AOT artifact) is present under `lib/arm64-v8a/`.
14. The proof's own source contributes none of the trim/AOT warnings listed above.
15. Size: the arm64 Release APK byte length is less than a `dotnet new maui` Release APK built with the same SDK, same RID, and MAUI's default trim. Record both byte lengths. The ~14.3 MB iOS figure in the docs is not this gate.
16. Startup: `adb shell am force-stop <package>` then `adb shell am start -W -n <package>/.MainActivity`, discard the first run, take `TotalTime` for 10 runs, compare medians. The proof's median is less than or equal to the MAUI template's median on that same virtual device. Also log a managed timestamp from the activity's first layout of the button, so a later iOS port has a runtime-side number that does not depend on `am start`.

NativeAOT, required to run, separately scored:

17. The publish command above finishes. XA1040 is present. Any other warning is classified as "this repo" or "workload."
18. If the APK installs and items 1–4 pass, NativeAOT is marked **passed** and the APK size and `TotalTime` median are recorded next to the Mono numbers.
19. If publish fails, or the process crashes before the button is shown, NativeAOT is marked **failed** with the exception or warning text. The Mono proof can still be **passed**. The architecture is then not NativeAOT-shippable until that failure is understood.

The proof of concept is **fully working** when items 1–16 pass. Item 17 is mandatory bookkeeping. Items 18–19 choose the NativeAOT verdict. They do not rewrite items 1–16.

## iOS phase

Start this only after the Android proof is fully working under the definition above. The iOS work is a second reconciler and a second measurement, not a new component model.

### What changes

| Android proof | iOS phase |
| --- | --- |
| `LinearLayout`, `TextView`, `Button`, `EditText` | `UIStackView` or plain `UIView` layout, `UILabel`, `UIButton`, `UITextField` |
| Looper post | `InvokeOnMainThread` |
| `ClipboardManager` | `UIPasteboard` via the iOS workload bindings, still with no hand-written `ApiDefinition` for UIKit |
| `ContentDescription` | `AccessibilityLabel`, and the control is a real `UIButton` so the button trait is implicit |
| Debug interpreter instead of JIT (`AndroidUseInterpreter` default). Confirm with `RuntimeFeature.IsDynamicCodeCompiled == false`. | ARM64 Debug is AOT + interpreter. There is no JIT on device or on the ARM64 simulator. [Interpreters](https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter) |
| Release Mono AOT, JIT still available for leftovers | Release full AOT. Missing generic instantiations throw `System.ExecutionEngineException` ("Attempting to JIT compile method while running in aot-only mode"). [Interpreters](https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter) |
| NativeAOT experimental (XA1040) | NativeAOT is the supported opt-in publish on iOS. [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0) |

**Fact.** Apple's App Review guideline 2.5.2 says apps may not download, install, or execute code which introduces or changes features or functionality. [App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/#software-requirements). A debug-only hot reload on a developer machine is a different situation from shipping a loader that accepts new IL. The iOS phase keeps assembly loading out of Release.

**Fact.** XAML hot reload on iOS, in the MAUI docs, requires the Debug configuration to be named `Debug` and the linker set to Don't Link. It does not reload C#. [XAML Hot Reload](https://learn.microsoft.com/en-us/dotnet/maui/xaml/hot-reload). The iOS phase is testing C# method-body reload of `Render`, so the relevant machinery is `MetadataUpdater` plus the interpreter, with linking off in Debug. **Inference.** That combination is what MAUI's C# hot reload has required on Mono. It still has to be measured. The ARM64 simulator is the stand-in for a device. An x64 simulator JIT run does not count.

Third-party native libraries, when they show up, use the existing generators. iOS: Objective Sharpie emits `ApiDefinition.cs` from Clang, and a human still clears `[Verify]` annotations. [Objective Sharpie](https://learn.microsoft.com/en-us/dotnet/maui/ios/objective-sharpie/get-started). Android: the blueprint pipeline above. **Inference.** Pure Swift that is not exposed to Objective-C is outside Sharpie's header parse. That is a later limit, not an Android-proof concern.

### iOS acceptance, when that phase starts

Same screen, same items 1–11 and 14, on an `ios-arm64` simulator (or an `iossimulator-arm64` RID, whichever the workload names). Release is Mono full AOT and must pass the screen. NativeAOT publish (`PublishAot=true`, no `EnablePreviewFeatures` requirement of the Android kind) must pass the screen with no trim/AOT warnings in this repo's source. Startup and size use the same comparative gate against `dotnet new maui` on that SDK, plus the docs' ~130 ms / ~5.4 MB NativeAOT template figures as context only.

Hot-reload samples that only pass on the x64 simulator are discarded.

## What the Android design must keep portable

These are constraints on the Android code, not future refactors.

- Element records and `AppState` have no Android or Java usings. Platform widgets exist only in the reconciler file.
- Prop updates are assignments or source-generated calls. No `PropertyInfo`, no `MakeGenericType` on a runtime `Type`, no `Reflection.Emit`.
- The hot-reload path is `MetadataUpdater` on a method body, Debug only, behind `IsSupported`. There is no `Assembly.Load` of a new component assembly in shared code.
- Render is a function of state. The reconciler owns view lifetime. State is not stored in the activity, so a later iOS view controller can hold the same object.
- Accessibility is data on the element (`AccessibilityLabel` today; a role enum when a widget's type does not already imply one). Each reconciler maps it. The shared tree does not call `ContentDescription` or `AccessibilityLabel` itself.
- The platform escape hatch is "call the SDK binding from the app project." It is not a stringly-typed JNI or `objc_msgSend` interpreter in the framework.
- One UI-thread abstraction. No `Java.Lang.Thread` or `NSRunLoop` in the render function.
- Listeners are attached once per view instance.
- `IsAotCompatible=true` stays on, so an Android Debug session still tells you when a change would be illegal on iOS NativeAOT.

**Inference.** Android `Java.Lang.Object` peers and iOS `NSObject` handles are both "a managed wrapper keeps a native object alive." The reconciler must drop its references when it removes a view, on both platforms. The proof should clear the Android reference and rely on the usual `Dispose` pattern the binding uses. Getting this wrong shows up as a leak, not as a failed functional test. A ten-minute session that types and re-renders, followed by a heap snapshot if the tooling is there, is worth recording and is not a pass gate.

## React Native, Expo, and NativeScript

### What those plugins actually are

**Fact.** Since React Native 0.82 (October 2025), the New Architecture is the only architecture. Setting `newArchEnabled=false` is ignored. [React Native 0.82](https://reactnative.dev/blog/2025/10/08/react-native-0.82).

**Fact.** Turbo Modules start from a typed Flow or TypeScript spec named `Native<MODULE_NAME>`. Codegen emits C++, Objective-C++, and Java. They call native code through JSI rather than the old JSON bridge. [Turbo modules](https://github.com/reactwg/react-native-new-architecture/blob/main/docs/turbo-modules.md).

**Fact.** Fabric is the C++ renderer. It reads JavaScript values through JSI instead of serializing JSON. [Fabric](https://reactnative.dev/architecture/fabric-renderer). Hermes is built and versioned with React Native because JSI must be the same binary interface in both. [Bundled Hermes](https://reactnative.dev/architecture/bundled-hermes).

**Fact.** The Expo Modules API "is an abstraction layer on top of JSI and other low-level primitives that React Native is built upon." A module is a Kotlin or Swift class whose `definition()` builds a `ModuleDefinition`. JavaScript obtains it with `requireNativeModule` from `expo-modules-core`. `AppContext` exposes `reactContext` and `hasActiveReactInstance`. Exported views inherit `ExpoView`, which on iOS is an `RCTView`. [Module API](https://docs.expo.dev/modules/module-api). Installing modules in an existing native app is documented as installing them in an existing React Native app, via the `expo` package. [Install Expo modules](https://docs.expo.dev/bare/installing-expo-modules/), [existing library](https://docs.expo.dev/modules/existing-library). The official tutorial reads preferences through `appContext.reactContext`. [Create a native module](https://docs.expo.dev/modules/native-module-tutorial).

**Fact.** A NativeScript Android plugin is an npm package of JavaScript plus, optionally, `platforms/android/*.aar` or `*.jar` and an `include.gradle`. The JavaScript calls Android APIs through build-time metadata consumed by the NativeScript runtime (V8 on Android, JNI, a metadata generator over bytecode). [Android plugins](https://old.docs.nativescript.org/plugins/android-plugins-infrastructure), [Metadata](https://docs.nativescript.org/guide/metadata), [execution flow](https://old.docs.nativescript.org/core-concepts/android-runtime/advanced-topics/execution-flow).

### The three options

| Option | What it would take | What you could run | Risk | In the Android proof? |
| --- | --- | --- | --- | --- |
| Host Hermes or V8 next to CoreCLR/Mono | A second GC and a second debugger. JNI plus either JSI (Hermes must be the build React Native compiled against) or NativeScript's metadata runtime. Two UI threads to keep straight. A store build that downloads new JS sits under Apple's 2.5.2 rules on iOS. | Existing JS modules whose native half is also in-process | High. The app becomes a host of React Native or NativeScript, and the size and AOT questions become theirs. | No. |
| Reimplement JSI or the Expo module host in C# | JSI is a C++ API compiled into Turbo Module `.so` files. A C# interface with similar method names does not satisfy that linker. An Expo host shim means reimplementing `AppContext`, the event emitter, the view factory, and autolinking, then still providing a React context for modules that touch `reactContext`. Fabric views also need the C++ mounting layer. | A few modules that only use the Kotlin DSL and never touch React | High cost, low coverage. Each module uses a different slice of the host. | No. |
| Bind the underlying Kotlin/Java AAR and discard the JS and the module shell | The existing .NET Android generator. One binding project per library, plus `Metadata.xml` fixups when names are wrong. | Libraries whose public Java API is ordinary Android types | Low per library, and bounded. Coverage is a subset. | No. It is a later experiment. |

### What is realistic

**Inference.** Hosting Hermes so that Fabric and Turbo Modules run is "embed React Native." The C# UI framework would then be a guest, or a second UI toolkit in the same process. That contradicts the size and AOT goals. The same is true of hosting NativeScript's V8 runtime so that `index.js` plugins run.

**Inference.** A C# Expo-shaped DSL (`Name`, `Function`, `View`) would be a pleasant way to write *new* modules in this framework. It would not load existing `class Foo : Module()` binaries. Those binaries call `expo.modules.kotlin` and expect `definition()` to be interpreted by that library. The Android binding generator would surface `definition()`, not `getTheme()`, because `getTheme` in the official tutorial is a lambda inside the DSL, not a Java method. [Tutorial](https://docs.expo.dev/modules/native-module-tutorial).

**Inference.** Turbo Module Kotlin that codegen generated against `com.facebook.react` pulls React Native types (`Promise`, `ReadableMap`, `ReactApplicationContext`) into the public surface. Binding that class means binding a large piece of React Native, which is the first option again. C++ Turbo Modules are further out: they are `.so` files linked to JSI.

**Inference.** The reusable subset is an AAR or JAR that does not reference `com.facebook.react`, `expo.modules`, or `org.nativescript` on its public API. Some plugins are only a thin shell over such an AAR (maps, a camera SDK, a vendor library). Many Expo modules are a thin shell over the Android SDK itself (`Vibrator`, `ClipboardManager`, `SharedPreferences`). For those, calling the SDK from C# is the compatible behavior, and the Expo class is not worth binding. NativeScript plugins that are only JavaScript have no native binary to bind. Their `platforms/android` AAR, when they ship one, is the same subset.

Expo Modules API on Android is the most attractive of the three and still does not belong in the proof of concept. A convincing demo would either ship `expo-modules-core` and a React host, or call `ClipboardManager` and label it "Expo compatible." The first destroys the experiment. The second does not test Expo.

### Later experiment, after the proof

One library, chosen because `jar tf` / `javap` shows no React, Expo, or NativeScript types on its public methods. Bind it with the Android binding project. Call one method from the Save handler. Record generator warnings and any `Metadata.xml` edits. Stop there. If a popular Expo or NativeScript package cannot be separated from its host, write that down and do not build a shim.

## Hardest technical risk

Android Release will hide the bug that kills the iOS port and the NativeAOT publish.

Default .NET 10 Android Release is Mono AOT with a JIT still available for methods that were not precompiled. The MAUI docs say turning the interpreter on for that configuration disables the JIT, which is how you know the JIT is otherwise still there. A missing generic instantiation, or a `Reflection.Emit` stub the interpreter never had to run, can succeed on that Release build and throw `ExecutionEngineException` on iOS full AOT.

Debug is a separate trap only if `IsDynamicCodeCompiled` is true. The Android workload documents the default as the interpreter instead of the JIT, while the MAUI table still says "JIT + interpreter." The proof records the two `RuntimeFeature` flags so the hot-reload result is not described as interpreter evidence when a JIT was present.

A reconciler that finds properties by name fails a different check. The trimmer deletes those members under NativeAOT even when a JIT would have executed the reflection. The analyzers and the NativeAOT publish are what surface that. A Mono screen that "works" without those artifacts has not de-risked the framework.

The second risk, module hosting, is easier to avoid than to solve. Pulling Hermes or `expo-modules-core` into the proof would make that risk look like progress.

## What was not done

- No project was compiled. The snippets are illustrations.
- No APK was built, so the size and `TotalTime` gates have no results yet.
- Hot reload on Android Mono via `dotnet watch` was not executed. The plan says to try it and to fail the gate if the only way to see the edit is a reinstall.
- NativeAOT on Android was not published. XA1040 is a documented warning, not a crash log from this repo.
- Expo, React Native, and NativeScript packages were not decompiled. The "public API has no host types" test is the method for the later experiment.
- Apple's Developer Program License Agreement was not re-read. Guideline 2.5.2 was. Secondary write-ups describe a license exception for interpreted code that does not change the app's purpose. This plan does not rely on that exception, because the shipping app does not download IL or JS.

## Sources

- [.NET MAUI runtimes and compilation](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0)
- [Native AOT on iOS and Mac Catalyst](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0)
- [Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Native AOT security / reflection limits](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/security)
- [Trim a .NET MAUI app](https://learn.microsoft.com/en-us/dotnet/maui/deployment/trimming?view=net-maui-10.0)
- [Interpreters on iOS and Mac Catalyst](https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter)
- [What's new in .NET MAUI for .NET 11](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11)
- [.NET 11 release notes schedule](https://github.com/dotnet/core/blob/main/release-notes/11.0/README.md)
- [XA1040 NativeAOT experimental on Android](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040)
- [XA1044 Mono AOT properties versus CoreCLR](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1044)
- [.NET for Android build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties)
- [.NET for Android binding blueprint](https://github.com/dotnet/android/wiki/Blueprint)
- [P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)
- [Hot Reload support matrix](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload)
- [Supported C# edits](https://learn.microsoft.com/en-us/visualstudio/debugger/supported-code-changes-csharp)
- [MAUI XAML Hot Reload](https://learn.microsoft.com/en-us/dotnet/maui/xaml/hot-reload)
- [MAUI accessibility](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0)
- [CoreCLR and the Mono timeline](https://devblogs.microsoft.com/dotnet/coreclr-progress-and-mono-timeline-dotnet-maui/)
- [MauiReactor](https://devblogs.microsoft.com/dotnet/mauireactor-mvu-for-dotnet-maui/)
- [Uno accessibility](https://platform.uno/docs/articles/features/accessibility/index.html)
- [Flutter assistive technologies](https://docs.flutter.dev/ui/accessibility/assistive-technologies)
- [Avalonia mobile](https://avaloniaui.net/avalonia/mobile)
- [App Review Guidelines 2.5.2](https://developer.apple.com/app-store/review/guidelines/#software-requirements)
- [React Native 0.82](https://reactnative.dev/blog/2025/10/08/react-native-0.82)
- [Turbo modules](https://github.com/reactwg/react-native-new-architecture/blob/main/docs/turbo-modules.md)
- [Fabric](https://reactnative.dev/architecture/fabric-renderer)
- [Bundled Hermes](https://reactnative.dev/architecture/bundled-hermes)
- [Expo Module API](https://docs.expo.dev/modules/module-api)
- [Install Expo modules](https://docs.expo.dev/bare/installing-expo-modules/)
- [NativeScript metadata](https://docs.nativescript.org/guide/metadata)
- [NativeScript Android plugins](https://old.docs.nativescript.org/plugins/android-plugins-infrastructure)

## Summary

1. The first build is a fully working `net10.0-android` proof: a C# element tree reconciled onto real Android views, in an app that does not reference MAUI.
2. The component set is `Column`, `Text`, `Button`, and `TextField`, on one screen with typed text, a counter, an empty-state greeting, and state that survives activity recreation.
3. Debug uses Mono's default interpreter-instead-of-JIT setting and `dotnet watch` method-body hot reload; the title must change in place, the typed text and process id must stay, and p50 must be within 1.5 seconds.
4. Release Mono AOT is the functional ship gate; NativeAOT is published and scored separately because XA1040 still marks it experimental and not for production.
5. Android Release Mono AOT can still JIT leftover methods, so the proof also turns on AOT analyzers and treats NativeAOT warnings in its own code as failures of the design.
6. iOS is phase two on ARM64, where there is no JIT: the same elements map to UIKit, Debug hot reload needs the interpreter and linking off, and Release is full AOT or NativeAOT.
7. Shared code stays portable by keeping Android types out of the element tree, applying props without reflection, and confining hot reload to `MetadataUpdater` in Debug.
8. Hosting Hermes, V8, or a C# JSI/Expo shim would embed React Native or NativeScript and is rejected for the proof and for the shipping architecture.
9. Expo Modules API, Turbo Modules, and Fabric need that host; the realistic later subset is binding an AAR whose public Java API does not mention React, Expo, or NativeScript.
10. No emulator or device was run; the gates above are the measurement plan, and the published MAUI template timings are context, not results for this app.
