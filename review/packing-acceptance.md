# Packing checklist acceptance

The question is whether the packing sample on main `996591f4eeb921826456a41239dc65b7ea925583` is a small useful checklist after the switch dispatch, predictive back, and action-bar fixes. It is, in portrait, on one process. Landscape rows past the fold are a bounded limitation. They do not block that checklist.

throughput checkpoint: n/a, read-only investigation.

## Overview

`samples/NativePoc.Packing` builds an element tree for a four-item kit list and an editor. `PackingActivity` mounts that tree with the existing reconciler. Release publish sets `PublishAot`. Debug and Release both passed the harness in `review/packing-fix`. This review reran the unit tests. A fresh KVM install did not boot.

## Key concepts

`Switch` and `Button` are sibling element records. The Android widget `Switch` is a subclass of `Button`. `HostDispatch.Choose` tests the switch type before the button type, so a switch gets `CheckedChange` and a plain button gets `Click`.

`INativeServices` is the typed native module. `AndroidNativeServices` implements clipboard and vibrator. `PackingActivity` is the activity shell. It owns predictive back, the action-bar inset, and the call into `PackingScreen.Render`.

`PackingSession.Shared` is one static object. Rotation and a same-process relaunch read it. Force-stop builds a new process, and `PackingState.Seed` runs again. Nothing is written to a bundle or a file.

## How it works

The list renders a title, a progress line, one column per item, Add, and Copy. Each item column has the name, a `Switch` labeled `Packed <name>`, and an Edit button. Checking a switch writes `Packed`, calls `Pulse`, and requests a render. Add and Edit open the editor. Save with a non-empty name returns to the list. Back, including system back from the editor, returns to the list and drops the draft. System back from the list finishes the activity.

`AndroidHost.Attach` asks `HostDispatch` which listener to install. `Detach` removes that listener. A switch logs `switch-unsubscribe` with the Java handle. `OnDestroy` calls `ViewReconciler.Release`, then `AndroidNativeServices.Dispose`, which logs `native-services-disposed`.

On API 33 and newer, `OnBackPressed` calls the platform base and does not handle the route. `OnBackInvokedCallback` calls `HandleSystemBack`. The harness sends `KEYCODE_BACK`. If the editor is still showing, it sends that key a second time. The passing check is that the process id stays, `packing-start` does not increase, and `native-services-disposed` does not increase, while the route returns to the list.

`PlaceBelowActionBar` measures `action_bar_container` and adds the overlap to the root padding. The portrait dump in `review/packing-fix/debug-ui.xml` has the bar at `[0,0][1080,275]`. The list title `Packing` starts at y=317. The progress line `1 of 4 packed` is at y=368. The Canvas tent switch is at y=470. The writeup's "content starts at y=368" is the progress line. The title is the first content line, and it is already below the bar.

## Where things live

`samples/NativePoc.Packing/PackingScreen.cs` is the composition. `samples/NativePoc.Packing/PackingState.cs` is the static session. `src/NativePoc.Core/HostDispatch.cs` is the subtype rule. `src/NativePoc.Android/AndroidHost.cs` creates `Android.Widget.Switch` and the listeners. `src/NativePoc.Android/AndroidNativeServices.cs` is the clipboard and vibrator. `src/NativePoc.Android/PackingActivity.cs` is the activity shell.

`review/packing-fix/results.txt` says `PASS debug` and `PASS release`. The passing logs are `debug-pass-nativepoc.log` and `release-nativepoc.log`. `observe.sh` is the harness. `failures.txt` and `attempt-1` through `attempt-3` are aborted runs, including a Release copy line that logged `chars=63` before the list text was added to the log.

## Gotchas

**No scroll is a bounded limitation, not a blocker of the portrait checklist.** `release-added.png` shows five rows, Add, and Copy on a portrait screen. `debug-rotated.png` is landscape with `1 of 5 packed`. It shows Canvas tent, Camp stove, and a clipped Ridge map Edit. Water bladder, Lantern, Add, and Copy are below the fold. The list is a `Column`, not a scrolling container. The landscape render line is still `total=5`. After rotation back to portrait the harness requires the text `Lantern`. A small checklist you pack in portrait is accepted. A claim that every row stays tappable after rotation is not.

**State is the process, not a file.** Debug pid 18603 and Release pid 20962 keep `total=5` across rotation and a same-process relaunch. Force-stop starts pid 19748 in Debug and pid 22084 in Release, both at `packed=1 total=4`, and the harness forbids the text `Lantern`.

**Clipboard readback is the same process.** `CopyText` writes `PrimaryClip` and reads it back. The passing line is `clipboard-ok chars=63 text=Canvas tent|Camp stove [packed]|Ridge map|Water bladder|Lantern`. That string is 63 characters and matches `Copy_text_is_one_line_per_item_with_a_packed_mark`. `cmd clipboard get` on the image returns no shell implementation. This is not a second-app read.

**Haptic is the API call.** Toggle logs `haptic-ok` after `Vibrator.Vibrate` of 20 ms. The report says physical vibration was not measured. This review did not feel a device.

## Verdict

**Accepted for a small portrait checklist on one process. No product blocker in that scope.** Landscape without scrolling is the acceptance limit.

| Capability | Verdict | Rerunnable evidence |
| --- | --- | --- |
| Switch dispatches as a switch, not as a button | **Accepted** | `HostDispatch.Choose` tests the toggle type first. `HostDispatchTests` uses a `Toggle` that extends `PlainButton` and expects `HostInput.Switch`. Device toggles change `packed` and log `haptic-ok`. Navigation logs `switch-unsubscribe` before the editor render. |
| Add, edit, and cancel | **Accepted** | Passing logs reach `route=List packed=1 total=5`. `release-added.png` and `debug-system-back.png` show Lantern. In-app Back and system back leave `Ridge map` in place. Field deliveries climb while typing and do not climb on the cancel render. |
| Predictive back from the editor | **Accepted on the harness behavior** | `KEYCODE_BACK` from the editor leaves the pid and does not add `native-services-disposed` or `packing-start`. The saved files do not record `ro.build.version.sdk`. The code uses `OnBackInvokedCallback` from API 33 up. |
| Action bar does not cover the title | **Accepted** | Portrait dump, title y=317, bar bottom y=275. Landscape log `content-inset overlap=189 top=231 bar-bottom=189`. The landscape screenshot shows the list title under the black bar, not inside it. |
| Callback cleanup | **Accepted for listener detach and service dispose** | Editor entry unsubscribes the list switches. List system back increases `switch-unsubscribe` and logs `native-services-disposed`. `Release_disposes_the_mounted_switches` checks the test host. No log records a JNI handle after `Dispose`. |
| Copy and haptic, with the stated bounds | **Accepted as in-process and API-only** | Clipboard line above. `haptic-ok` on toggle. No second process and no physical pulse. |
| Portrait checklist of the seed plus one row | **Accepted** | Five names, Add, and Copy are on `release-added.png`. |
| Landscape rows past the fold | **Bounded limitation** | No scroll. The added row and Water bladder are off the landscape capture. State is still in the log. |
| Durable storage | **Out of scope, and absent** | Static `PackingSession.Shared` only. |

## Tests this review ran

On the `996591f` tree, SDK 10.0.403.

- `dotnet test tests/NativePoc.Core.Tests` passed 28, failed 0, 75 ms.
- `dotnet test tests/NativePoc.Packing.Tests` passed 13, failed 0, 72 ms.
- `dotnet test tools/devx/NativePoc.Devx.Cli.Tests` passed 54, failed 0, 678 ms.

`evidence/packing-unit-tests.log` still says Core 27 and packing 11. That file is older than `HostDispatchTests` and `Copy_text_is_one_line_per_item_with_a_packed_mark`. The source counts are 28 and 13. CLI 54 matches the file and this run.

`dotnet build` of `NativePoc.Android` Debug `android-x64` succeeded with 0 warnings. The committed APK hashes point at `/opt/cursor/artifacts/nativepoc-packing-*.apk`. Those files are not in this checkout.

## KVM smoke on this machine

`/dev/kvm` opened after `chmod a+rw`. QEMU pid 11559 held `/dev/kvm` and `anon_inode:kvm-vm`. It did not create a `kvm-vcpu` fd. `dmesg` shows `kvm_spurious_fault` inside `kvm_arch_vcpu_create`. `adb devices` stayed empty for the boot attempt, including a retry with `-cores 1`. This review did not install an APK and did not tap the checklist. The device verdict above is the committed logs, dumps, and screenshots, checked against the source.

`review/packing-fix/release-publish.log` ends with `PUBLISH_EXIT:0` and the two ILC notes for `monodroid_unhandled_exception`. The packing runtime log does not print `IsDynamicCodeSupported`. NativeAOT is the Release project setting plus that publish line.

## What is framework, and what is the activity

The composition proof is the element tree, the reconciler, and `HostDispatch`. A switch and a button can sit in one column because the host classifies the Android view by the switch type first. That rule is in `src/NativePoc.Core` and `AndroidHost`. It is not packing-only.

The typed native SDK extension is `INativeServices`. Clipboard and vibrator are two methods with an in-process readback and an API pulse. The packing screen calls them. It does not name `ClipboardManager` or `Vibrator`.

`PackingActivity` is the sample shell. Predictive back, the action-bar padding, `SetContentView`, and the static session live there. Those do not belong in the shared host. The missing scroll would be a sample layout change, a scrolling element, not a new native service.

## How this review was run

pstack `0.15.36` is at `/home/ubuntu/.cursor/plugins/cache/cursor-public/9717366/ae5650febf4320793844c12c4adaa937f417f8da`. `~/.cursor/rules/pstack-models.mdc` is absent.

Leaves read and applied this session.

- `skills/poteto-mode/SKILL.md` and `skills/poteto-mode/playbooks/investigation.md`
- `skills/how/SKILL.md` with `references/explorer-prompt.md` and `references/explainer-prompt.md`
- `skills/why/SKILL.md`
- `skills/setup-pstack/SKILL.md`
- `skills/unslop/SKILL.md` and `skills/technical-writing/SKILL.md`
- `skills/no-comments/SKILL.md`, skipped because it edits the diff and this review must not change `src/`
- `skills/principle-prove-it-works/SKILL.md`
- `skills/principle-attack-the-premise/SKILL.md`
- `skills/principle-test-behavior-not-implementation/SKILL.md`
- `skills/principle-explain-the-number/SKILL.md`

The how and why leaves want `grok-4.7-xhigh-fast` explorers and a `claude-opus-5-5-xhigh` explainer or synthesizer. The Task tool has no readonly flag. Every role was required to be Grok 4.7 without `-fast`, and a large panel was ruled out. The review was done in this session from a detached worktree at `/tmp/pack-996591f`. This pass is not a second reviewer of itself.

**Prove it works** is why the tests were rerun and why the dump coordinates were read from the XML instead of the writeup sentence. **Attack the premise** is why "no scrolling blocks the checklist" was checked against the portrait screenshot and the landscape screenshot separately. **Test behavior, not implementation** is why `require_text` in a dump is not treated as proof a row is on screen. **Explain the number** was read and not applied. This review reports no duration.
