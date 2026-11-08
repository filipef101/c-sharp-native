# iOS feasibility

This is an explanation of the .NET 10 iOS constraints for the existing screen. The Mac commands live in `spikes/ios/scripts`. They were not run here.

## What this machine did

| Claim | State |
| --- | --- |
| UIKit host, scene delegate, project file, and Mac scripts | Authored |
| `net10.0` spike tests and Core tests | Compiled and tested. 3 spike tests passed. 27 Core tests passed. `sh spikes/ios/scripts/check-authored.sh` printed `authored-check ok`. |
| `net10.0-ios` compile | Not compiled. See `docs/ios/linux-ios-build.txt`. |
| `iossimulator-arm64` run | Simulator-unverified |
| `ios-arm64` Release Mono AOT and NativeAOT | Device-unverified |

`sh spikes/ios/scripts/prerequisites.sh`, `simulator.sh`, and `device-aot.sh` each exited 1 on this host with the text `This script needs macOS. This host is Linux. It does not build or run UIKit.`

## Overview

The Android screen is already a pure tree. `Screen.Render` in `src/NativePoc.Screen/Screen.cs` returns `Column`, `Text`, `TextField`, and `Button`. `ViewReconciler` in `src/NativePoc.Core/ViewReconciler.cs` applies that tree through `IElementHost`. An iOS app can implement that host with UIKit and leave Core alone.

The open question is which runtime can execute that host, and which reload mechanism can run there. Hypothesis, not a measurement. The screen is a go on an arm64 simulator Debug build that interprets every assembly, and a go on a device Release build that is full AOT and does not reload. Method-body hot reload is a conditional go only while the interpreter executes the assembly and no AOT image is loaded for it. Shipping Android's collectible `AssemblyLoadContext` reload on iOS is a no-go.

## Key concepts

`UseInterpreter` set to true is the macios shorthand for `MtouchInterpreter` set to `all`. If both properties are set, `MtouchInterpreter` wins. The non-MAUI default is off. MAUI turns it on for a configuration named Debug. The spike sets it explicitly because this app is not a MAUI app.

`TrimMode` value `copy` is the current name for link mode None. The trimmer still runs. It copies assemblies instead of rewriting them. Device builds default to partial trimming. Simulator builds default to copy. Those defaults are documented as of .NET 9 and may change.

`RuntimeFeature.IsDynamicCodeSupported` and `RuntimeFeature.IsDynamicCodeCompiled` are the flags the app prints at startup. `sh spikes/ios/scripts/check-authored.sh --classify <supported> <compiled>` maps the pair.

| Supported | Compiled | Class | Counts as iOS reload evidence |
| --- | --- | --- | --- |
| true | false | interpreter | Yes, on arm64, and only for assemblies that were not AOT-compiled |
| true | true | jit | No. This is the x64 simulator shape |
| false | false | aot-only | No reload. This is the Release and NativeAOT shape |
| false | true | none | The script exits 1. That pair is not a documented runtime |

Full AOT means the process has no JIT and no interpreter. A missing method then throws `System.ExecutionEngineException` with the message `Attempting to JIT compile method while running in aot-only mode`.

## How it works

Debug on a device, and Debug on an arm64 simulator, cannot JIT. Apple disallows dynamically generated code there. The interpreter executes IL without generating native code. An x64 simulator can JIT. The MAUI runtimes table lists iOS Debug as JIT on x64 and as AOT plus interpreter on ARM64. Release Mono on iOS is full AOT. NativeAOT has neither a JIT nor an interpreter, and `UseInterpreter` does nothing in that mode.

The spike Debug group sets `UseInterpreter` true, `TrimMode` copy, `PublishAot` false, and portable debug symbols. It does not set `MtouchInterpreter`, because that property would override `UseInterpreter`. The Release group sets `UseInterpreter` false and `PublishAot` false. Release does not set `TrimMode`, so the SDK default remains. The official `dotnet new ios` template sets Release `TrimMode` to full. This spike does not, so a device AOT run measures the SDK default rather than the template's size trim.

`ScreenController` prints the two runtime flags and then calls `Screen.Render`. `UIKitHost` maps `Column` to `UIStackView`, `Text` to `UILabel`, `Button` to `UIButton`, and `TextField` to `UITextField`. Listeners are added once. `Retarget` updates the path stored on that listener so a keyed reorder still calls the element that now sits at the path. A programmatic field write is skipped when the string is already equal, and a flag drops an `EditingChanged` echo if one arrives during that write.

`HotReload` exists only in Debug. `UpdateApplication` returns unless `MetadataUpdater.IsSupported` is true, then it calls `RequestRender`. It does not read a DLL.

Android's `HotReloadServer.Apply` loads a screen DLL into a collectible `AssemblyLoadContext`, and on `PlatformNotSupportedException` or `NotSupportedException` it loads again with collectible false. That second load still has to execute new IL. iOS Release full AOT and NativeAOT cannot execute that IL. Debug can interpret new IL only when the interpreter is on and the assembly was not AOT-compiled. The current Mono `AssemblyLoadContext` managed constructor on runtime `main` accepts `isCollectible` and forwards it to the native initializer. The old throw `Collectible AssemblyLoadContexts are not yet supported by MonoVM` is not in that file. Unload on iOS was not run here. Issue 34072 is open and now scoped to WebAssembly. A successful load would still be the wrong debug mechanism, and it is not a ship mechanism.

Apple's review guideline 2.5.2, as already quoted in `research/runtime-feasibility.md`, says apps may not download, install, or execute code which introduces or changes features or functionality. This session did not re-fetch that page. The spike does not add a loader to Release.

Method-body reload is the mechanism runtime issue 44806 specifies. It requires the Mono interpreter with inlining off, `DOTNET_MODIFIABLE_ASSEMBLIES=debug`, and debug symbols. The same issue's task list says to block updates to assemblies that have AOT images loaded, and states there is no supported hybrid interpreter and AOT scenario. macios issue 12378, closed by PR 12447, is the SDK side that sets that environment variable for an interpreted app with debug symbols. A configuration that AOT-compiles the screen assembly and also turns the interpreter on is the hybrid case that issue says is unsupported.

macios issue 24782 was closed on 2026-09-21 with the comment `Completed`. The issue body still shows `PreserveSmartEnumConversions` unchecked. The close is not a measurement that a method-body edit of `Screen.Render` updates this app. Hypothesis. Arm64 Debug with `UseInterpreter` true and `TrimMode` copy is the configuration that matches the written rules. Confirm it on a Mac by reading `IsDynamicCodeSupported=True` and `IsDynamicCodeCompiled=False`, then by changing a string inside `Screen.Render` and seeing `RequestRender` show the new string in the same process. An x64 simulator run does not confirm it. A device Release run must not attempt it.

## Where things live

`spikes/ios/NativePoc.iOS` is the throwaway UIKit app. It references `src/NativePoc.Core` and `src/NativePoc.Screen`. It is not in `NativePoc.slnx`.

`spikes/ios/NativePoc.iOS.Tests` is a `net10.0` test project. It runs the authored-file script and calls `Screen.Render`.

`spikes/ios/scripts/prerequisites.sh` checks for macOS, Xcode 26 or newer, the .NET SDK, and an installed `ios` workload. It does not install the workload.

`spikes/ios/scripts/simulator.sh` is the command to run once a Mac is connected.

`spikes/ios/scripts/device-aot.sh` compiles `ios-arm64` with code signing off. `device-aot.sh native-aot` publishes with `PublishAot` true. Neither script installs to a device or creates a signing identity.

`docs/ios/architecture.md` records the shape that was kept. `docs/ios/api-proposals.md` records Core changes that were not made. `docs/ios/mac-worker.md` records the Cursor worker note.

## Gotchas

The Linux error text names the `wasm-tools` workload. The missing pack is `Microsoft.iOS.Sdk.net10.0_26.0`, and the manifest on this machine lists that pack under the `ios` workload only. Installing `wasm-tools` would not compile this project. The `ios` workload was not installed here, because this VM cannot run the result.

`MtouchInterpreter` set to `-all` enables the interpreter and then interprets nothing. Combined with `TrimMode` copy, that AOT-compiles every full assembly. The spike forbids that property.

Binding names in `UIKitHost` are authored and uncompiled. `InsertArrangedSubview`, `NSThread.IsMain`, `UIPasteboard.General.String`, and `NativeHandle` string conversion get their first compile on a Mac. A failure there is a binding mismatch, not a Core defect.

`SupportedOSPlatformVersion` is 15.0. .NET 10 for iOS can target 12.2. Scene connection needs iOS 13 or newer. 15.0 keeps the spike off the oldest OS the workload can still name.

The workload manifest observed on this SDK is `microsoft.net.sdk.ios` 26.0.11017. The .NET 10 macios release notes require Xcode 26 or newer on macOS 15.6 or newer. That requirement is enforced by `prerequisites.sh` and was not executed on a Mac.

## Hypotheses

1. Arm64 simulator Debug, with the spike's Debug properties, shows the counter, the field, and Save, and prints interpreter flags. Go. Simulator-unverified.
2. Device Release Mono, `UseInterpreter` false, shows the same screen and does not reload. Go. Device-unverified. Android Release is not this proof, because Android Release can still JIT methods that were not precompiled.
3. An x64 simulator success is a no-go as evidence. The script never passes `iossimulator-x64`.
4. Method-body reload of `Screen.Render` on arm64 Debug is a conditional go. It needs interpreter class flags, `MetadataUpdater.IsSupported` true, and a same-process title change. It is a no-go if the log class is `jit` or `aot-only`, and a no-go if the screen assembly was AOT-compiled.
5. Collectible or non-collectible DLL reload, as `HotReloadServer` does on Android, is a no-go on iOS. Release cannot execute the new IL. A Debug interpreter load is a different, unshippable mechanism and was not implemented.
6. NativeAOT publish of this screen is a conditional go for the static screen and a no-go for reload. `UseInterpreter` has no effect. The macios `docs/nativeaot.md` page still describes the support as experimental. Device-unverified.
7. .NET 11 CoreCLR is out of scope. This spike stays on `net10.0-ios` and Mono.

## Sources read this session

- https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0
- https://github.com/dotnet/macios/blob/main/docs/building-apps/build-properties.md
- https://github.com/dotnet/macios/blob/main/docs/nativeaot.md
- https://github.com/dotnet/macios/wiki/.NET-10-release-notes
- https://github.com/dotnet/runtime/blob/main/src/mono/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.Mono.cs
- https://github.com/dotnet/runtime/blob/9a975e65/src/libraries/System.Runtime.Loader/tests/ApplyUpdateUtil.cs
- https://github.com/dotnet/runtime/issues/44806
- https://github.com/dotnet/runtime/issues/34072
- https://github.com/dotnet/macios/issues/12378
- https://github.com/dotnet/macios/issues/24782
- https://github.com/dotnet/macios/issues/24482
- https://raw.githubusercontent.com/dotnet/macios/main/dotnet/Templates/Microsoft.iOS.Templates/ios/csharp/Main.cs
- `research/runtime-feasibility.md` for the existing 2.5.2 citation. The guideline page was not fetched again.
