# Release notes

v0.2.0 was not published from this tree. The arm64 APK below is the one named by the coordinator.

## Coordinator-measured, Mac arm64 emulator

Not measured in this workspace. No Java floor was measured on that Mac.

- Machine: Filipe's MacBook Pro M5
- Device: Android emulator AVD `pixel_8`, `arm64-v8a`, Android 16
- APK: `nativepoc-0.2.0-android-arm64.apk`
- sha256: `b5b4a809f6e652642e0bea1394a451fa766fa00b0b05d39828773202f4873bac`
- Method: `am start -W -S`, LaunchState COLD, n=20 after one discarded warmup
- Percentile: Hyndman-Fan type 7. For n=20, p50 is the mean of the 10th and 11th sorted samples. p90 is the 18th plus 0.1 times the gap to the 19th.
- logcat: `IsDynamicCodeSupported=False`

Counter TotalTime, sample order: 800 447 683 575 736 497 419 1059 871 816 842 444 485 463 444 449 474 400 462 406.

Sorted: 400 406 419 444 444 447 449 462 463 474 485 497 575 683 736 800 816 842 871 1059.

p50 479.5 ms. p90 844.9 ms. The first ~10 samples are noisy.

packing1000, `--es nativepoc_bench packing1000`, logcat `window rows=1000 built=20 views=32`. Sample order: 447 364 408 362 382 449 369 361 594 918 486 470 428 577 544 457 457 469 514 846.

Sorted: 361 362 364 369 382 408 428 447 449 457 457 469 470 486 514 544 577 594 846 918.

p50 457.0 ms. p90 619.2 ms.

`tools/bench/cold-start.sh` repeats this method. `--self-test` checks the percentile function against both series. `--floor` builds the empty Java activity and measures it on the device `adb` can see. That floor was not part of the Mac series.

The x86_64 KVM numbers, including the Java floor from an earlier boot on that emulator, stay in the README. They are a different machine.
