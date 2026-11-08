# Packing fix runtime

The checklist had to change packed state, survive navigation, and stay visible under the action bar. Debug and Release NativeAOT both did that on the existing KVM emulator (`sys.boot_completed=1`, QEMU pid 6985).

`HostDispatch` matches `Switch` before `Button`. A plain button still mounts a click. The counter on this Release image logged `rendered count=0` then, after Save, `rendered count=1` and `clipboard-ok poc-1`.

`PackingActivity` registers `OnBackInvokedCallback`. System back from the editor logs `route=List` on the same pid and does not log a new `native-services-disposed`. System back from the list does dispose, and `switch-unsubscribe` increases. In-app Back cancels an edited draft.

The root padding uses the measured action bar. Portrait content starts at y=368 under a bar that ends at y=275. The canvas switch starts at y=470.

Copy round-trips inside the process and logs `clipboard-ok chars=63 text=Canvas tent|Camp stove [packed]|Ridge map|Water bladder|Lantern`. `cmd clipboard get` on this image returns `No shell command implementation`. `haptic-ok` is the vibrator API. Physical vibration was not measured.

State stays in the process. Relaunch on the same pid keeps Lantern and `2 of 5 packed`. Force-stop starts a new pid and restores Canvas tent, Camp stove packed, and `1 of 4 packed`. Rotation disposes and recreates the activity on the same pid and keeps `total=5`. Lantern is below the landscape fold because the list does not scroll. The render line still says `total=5`.

Earlier failed attempts stay in `attempt-1`, `attempt-2`, and `attempt-3`.
