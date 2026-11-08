# Packing sample runtime validation

Main `a0a88102b161c17cdc1fbc18977bfe07a7c9719d` (packing feature `087e2c2` is an ancestor). Validation only: no edits under `src/`, `samples/`, or `tools`.

Emulator: existing KVM guest, `sys.boot_completed=1`, QEMU pid 6985 holds `/dev/kvm`. Package `com.scratch.nativepoc`, activity `nativepoc.PackingActivity`. Harness: `observe.sh`. Each configuration starts with `logcat -c` and `am force-stop`. `AndroidRuntime` logs are empty.

Debug APK is the mono runtime (`libmonosgen-2.0.so`, `libmonodroid.so`, `classes.dex` 401928 bytes), SHA256 `a49b4d05f652147b281b348678a2f439306ddba1858c8cf5d191f41d7a11b465`, installed by `dotnet build -c Debug -t:Install` (install log ends `Build succeeded`, 0 warnings). Release APK is the NativeAOT publish (`classes.dex` 28020 bytes, `lib/x86_64/libNativePoc.Android.so` 5823264 bytes), SHA256 `494784e190a32dc2bd3fc577b9db3eff66e0a72beba48e610d7a02d1cca33020`. `release-publish.log` ends `PUBLISH_EXIT:0` after two ILC notes on `Android.Runtime.RuntimeNativeMethods` methods that always throw. Installed with `adb install -r`.

## Verdicts

Both configurations produced the same outcomes.

**Seed list renders.** Verified. First `packing-rendered` line is `route=List packed=1 total=4 switch-deliveries=0 field-deliveries=0`. The force-stop accessibility dump contains `1 of 4 packed`, `Canvas tent`, and `Packed Camp stove` checked true.

**Edit, save, add, and in-app Back.** Verified. Replacing the first name logs `field-deliveries` 1 through 11 and returns `route=List packed=1 total=4 field-deliveries=11`. The accessibility check required `Trail tent` and rejected `Canvas tent`. Add then in-app Back kept `total=4` and `1 of 4 packed`. A second editor visit and in-app Back returned to the list with `Trail tent` still present.

**One managed callback per switch toggle.** Not verified. `switch-deliveries` stays 0 for the whole log. There is no `switch-subscribe` line and no `haptic-*` line on any switch tap. The visible Camp stove switch (`content-desc` `Packed Camp stove`, bounds y 443–514) changed `checked` from true to false. The model stayed `packed=1`, and the later copy screenshot shows Camp stove on again after the next render. The canvas switch (bounds y 195–266) stayed `checked=false`; its center sits under the action-bar title at y 166–237, so that tap did not reach the widget. The editor switch stayed `checked=false` as well.

`AndroidHost.Attach` returns inside `if (view is AndroidButton button)` before the `AndroidSwitch` branch. `Android.Widget.Switch` extends `Button`, so `CheckedChange` is never attached and `Detach` never logs `switch-unsubscribe`. Edit navigation and list finish both recorded `switch-unsubscribe` count 0.

**Dispose on teardown.** Verified for `AndroidNativeServices.Dispose`, which logs `native-services-disposed`. Each configuration logs that line four times: system back from the editor, landscape rotation, return to portrait, and system back from the list. That log is not switch unsubscribe and not a Java peer-handle check.

**System back from the editor does not navigate.** Verified. API 36 does not enter the `OnBackPressed` editor branch. The line is `native-services-disposed`, the process stays up (debug pid 12770, release pid 13973), and the next `packing-rendered` is `route=Editor`. Static `PackingSession.Shared` still holds the editor route. The in-app Back button then logs `route=List`.

**Copy list.** Verified inside the process. `CopyText` writes the clip, reads `PrimaryClip` back, and logs `clipboard-ok chars=54` only when the strings match. 54 is the length of `Trail tent`, `Camp stove [packed]`, `Ridge map`, and `Water bladder` joined by newlines, which is the seed list after the rename and with Camp stove still packed. `cmd clipboard get` on this image returns `No shell command implementation`, and `dumpsys clipboard` is empty, so the text was not read back from outside the process.

**Haptic.** API call only. The single `haptic-ok` is the line immediately after `clipboard-ok`. `Pulse` calls `Vibrate(CreateOneShot(20, DefaultAmplitude))` when `HasVibrator` is true. Nothing in this run felt or measured a physical vibration. Switch taps never reached `Pulse`.

**Rotation keeps process state.** Verified. Landscape and the return to portrait each log `native-services-disposed` and a new `packing-start` on the same pid, then `route=List packed=1 total=4 field-deliveries=11`. The accessibility check required `Trail tent` and `1 of 4 packed` in both orientations. The landscape screenshot shows Camp stove still on. The first row sits under the action bar in that screenshot, so the renamed title is in the accessibility tree and the render log, not in the visible band of the capture.

**Relaunch while the process lives keeps the rename.** Verified. After system back from the list, `am start` on the same pid logs `field-deliveries=11` and the accessibility check required `Trail tent`.

**Force-stop restores the seed.** Verified. Debug pid becomes 13739 and release pid becomes 14857. The next line is `route=List packed=1 total=4 switch-deliveries=0 field-deliveries=0`. The accessibility dump has `Canvas tent` and `Camp stove` checked true, and `Trail tent` is absent.

## For the sample writer

The switch host treats every `Switch` as a `Button` and returns before `CheckedChange`. A visible toggle can flip the widget with zero `switch-deliveries`, zero `switch-subscribe`, and zero `switch-unsubscribe`. List progress does not change. System back on this API 36 image destroys the activity from the editor instead of running `PackingActions.Back`. The action bar is drawn over the first rows (content origin y=42, action-bar title y=166–237).
