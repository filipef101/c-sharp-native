# Developer preview

This page is the status of the tree. A number is labeled with how it was obtained. Nothing here was published.

## What works on Android

The listener and the Release build are NativeAOT. `IsDynamicCodeSupported` stays false. Both ran on the x86_64 API 34 emulator, boot `56815464-4bc4-4f4c-a503-944cb91ab31f`.

A screen file in the subset is lowered to a record and applied on the running listener. The walkthroughs are [devx/evidence/listener-handlers.txt](devx/evidence/listener-handlers.txt), [devx/evidence/listener-action-vm.txt](devx/evidence/listener-action-vm.txt), and [devx/evidence/listener-helpers.txt](devx/evidence/listener-helpers.txt). A public method marked `[ReloadCallable]`, and a public method in a namespace named by `[assembly: ReloadCallable]`, is a compiled switch. On pid 6912 that switch ran `CounterService.Next` and `LibraryFormat.Label` in one process. Saving the method body did not swap it. The next launch, pid 7154, ran the body compiled into that binary. Evidence: [devx/evidence/call-table.txt](devx/evidence/call-table.txt).

A Debug restart restores the screen that was showing and the program that was bound. On the listener whose `.so` is sha256 `ac9162fd16dd20982a98a507642aa4a7995527d8079f38516a08a1aaf39f2132`, pid 8195 was on packing (`source=8e9e4475be5bce8ca4a5a68791c6cf64`, stack `counter` then `packing`). After `am force-stop` and `am start`, pid 8297 loaded `state-loaded bytes=2175`, showed title `Packing`, and printed the same source and the same stack. `IsDynamicCodeSupported=False`. Evidence: [devx/evidence/nav-restore.txt](devx/evidence/nav-restore.txt). Emulator-run.

The Release counter render on pid 8067, `.so` sha256 `faea64b458078a87cfd2d57b721e5ab20b2112622f075af229bab9456cab5280`, is in [devx/evidence/tap-phases.txt](devx/evidence/tap-phases.txt). Emulator-run.

## What hot reload refuses

`IsDynamicCodeSupported` is false, so the process does not compile new IL. The call table is a switch that was compiled into the binary. Saving a method body prints that the body was not swapped. The running handler keeps the previous body until `cnative run android --restart-on-rude` relaunches. A method the table does not list, a new top-level type, a generic the edit constructs for the first time, and `await` are refused for the same reason: the binary has no arm, and a reload cannot invent one. The list is in [screen-program-subset.md](screen-program-subset.md). An edit under `src/NativePoc.Core` or `src/NativePoc.Android` is rude because the loader only swaps the screen assembly. That is written in [devx/comparison.md](devx/comparison.md).

The other toolkits, from their own pages and not timed on this boot, are in [comparison.md](comparison.md). .NET MAUI's .NET 10 page reloads XAML in a debug session with the debugger attached and states that it does not reload C#. Uno's page says it modifies XAML and C#. Avalonia's page plugs into .NET Hot Reload through `dotnet watch`, and C# follows those rules. This tree reloads a screen record. It does not reload a compiled method body.

## Startup

These are not this binary. This page does not remeasure cold start.

Mac, not measured in this workspace. Filipe's MacBook Pro M5, AVD `pixel_8`, arm64, Android 16. `am start -W -S`, LaunchState COLD, n=20 after one discarded warmup, Hyndman-Fan type 7. APK sha256 `b5b4a809f6e652642e0bea1394a451fa766fa00b0b05d39828773202f4873bac`. Counter TotalTime p50 479.5 ms, p90 844.9 ms. packing1000 TotalTime p50 457.0 ms, p90 619.2 ms. An interleaved run on APK sha256 `ce39c83e7c1aa8aca8334fbbafd7d0dfa9be7e8e75fa933ce42abd111b881acb` was counter p50 463.0 ms and packing1000 p50 437.5 ms. A second Mac run, host load 14.3, arm64 APK sha256 `ac502d27eae357b7514787abe4faa245a859380ca3322406cadc41d19905b3d4`, was counter TotalTime p50 423.5 ms, present p50 213.5 ms, UI p50 24.8 ms, draw p50 0.2 ms. The sample lists and the method are in [release-notes.md](release-notes.md).

Emulator, measured in this workspace on an older x86_64 API 34 binary, APK sha256 `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7`. Same interleaved method, n=20, all COLD. Counter TotalTime p50 763.0 ms. packing1000 TotalTime p50 578.5 ms. First present p50 138.0 ms and 137.0 ms. A later packing1000 series on sha256 `f7e2a6411480cc6337c30d995463ae654fc7171f11d92433d6e68acae03ccab9` was TotalTime p50 513.0 ms, first present p50 137.0 ms. Not remeasured on the binaries named above.

## iOS

The UIKit host is authored. It is not in `NativePoc.slnx`. It has not been compiled. There is no iOS workload on this machine, no Mac, and no simulator. No binary and no simulator log exist. [ios-status.md](ios-status.md). Unverified as a build.

## `cnative doctor`

`cnative doctor` checks the machine it runs on. It does not build or install. A failing line names the next command, and the process exits 1 when any line is `FAIL`. The checks are the .NET 10 SDK, `dotnet workload list` containing the android workload, `adb` on `PATH` or under `ANDROID_HOME` / `ANDROID_SDK_ROOT`, an online emulator or device, and the ILCompiler pack (or a note that the first Release publish restores it).

Measured on this VM, exit 0:

```
OK dotnet: 10.0.403
OK android workload: installed.
OK adb: adb
OK device: emulator-5554
OK NativeAOT: /home/ubuntu/.nuget/packages/microsoft.dotnet.ilcompiler
```

The three cases (a ready machine, missing tools, an offline emulator) are `DoctorTests`. Tested.
