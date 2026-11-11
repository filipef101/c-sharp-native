# Release notes

v0.2.0 was not published from this tree. [CHANGELOG.md](../CHANGELOG.md) is the measured record since v0.1.0-poc. The arm64 APK in the first section is the one named by the coordinator. The release builds at the bottom were built here and not uploaded.

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

## Coordinator-measured, Mac arm64 emulator, interleaved

Not measured in this workspace. Present versus FrameCompleted was not captured.

- Machine: Filipe's MacBook Pro M5
- Device: Android emulator AVD `pixel_8`, `arm64-v8a`, Android 16
- nativepoc APK sha256: `ce39c83e7c1aa8aca8334fbbafd7d0dfa9be7e8e75fa933ce42abd111b881acb`
- nativepoc package: `com.scratch.nativepoc`
- nativepoc activity: `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`
- floor APK sha256: `7a8b6e2daa328c69a943211fdba85daac225017ff760409d964bbb49b5fefb1c`
- floor package: `com.scratch.floor`
- floor activity: `com.scratch.floor/.FloorActivity`
- Method: `am start -W -S`, all COLD, one warmup each, n=20 interleaved (native, floor, packing1000 per round)
- Percentile: Hyndman-Fan type 7

Counter TotalTime, sample order: 513 378 463 328 468 382 423 943 948 740 384 531 463 401 444 635 393 423 485 716. p50 463.0 ms. p90 760.3 ms.

Empty Java floor TotalTime, sample order: 423 416 378 501 407 313 410 337 505 502 341 460 367 307 386 296 358 393 530 517. p50 400.0 ms. p90 506.2 ms.

packing1000 TotalTime, sample order: 436 302 359 498 377 427 353 656 620 578 447 383 333 324 439 440 557 373 460 474. p50 437.5 ms. p90 582.2 ms.

Counter is 63.0 ms over the floor at p50. The counter p90 is a fat tail (943 and 948). packing1000 does not have that tail; its largest sample is 656.

`tools/bench/mac-bench.sh` repeats this interleaved method and also records logcat marks and the first gfxinfo framestats row. `--self-test` checks these three percentiles and the round order. That script was not run on the Mac.

## Emulator-run, counter tail versus packing1000

Measured in this workspace on the x86_64 API 34 emulator. Same interleaved method, n=20, all COLD, Hyndman-Fan type 7. Host load average 0.71 at the start. APK sha256 `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7`. `.so` 6,297,424 bytes.

Counter TotalTime p50 763.0, p90 828.6. packing1000 TotalTime p50 578.5, p90 641.5. First present p50 138.0 and 137.0.

The counter does not run a timer or an effect. `startup gc` was gen0=1, gen1=0, gen2=0 at activity and at present on every launch of both series. The other screens are staged with `Post` after the first render. `startup phase=catalog` was 0 ms on 18 launches and 1 ms on 2, and it is after first-present. `startup phase=nav` is 0. Those bytes are not the gap.

Before this binary, a counter launch logged `AutofillManager.notifyViewEnteredForFillDialog` before first-present. packing1000 did not. The name field now stays out of touch focus until the first pre-draw, and autofill stays off until then. `startup field-focus focused=0` on all 20 counter launches. A tap still focuses the field after that pre-draw.

That did not close the TotalTime gap. The first framestats row, UI thread (DrawStart minus PerformTraversalsStart), p50 110.0 ms counter and 109.3 ms packing1000. Issue-draw-commands (SwapBuffers minus IssueDrawCommandsStart) p50 301.6 ms and 145.7 ms. On this swiftshader emulator the smaller screen is slower because recording its first frame takes longer. This run's p90 is not two outliers of the Mac shape. Present versus FrameCompleted on the Mac was not captured.

## Emulator-run, row-height prefix

Same x64 APK, sha256 `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7`. One COLD packing1000 launch, pid 31636, `startup window rows=1000 built=20 views=92`. gfxinfo reset after the first frame, then eight swipes. `first=174`, then back to `first=0`. Canvas stayed 60489. Item 0000 still showed `Ridge line` and `marker`. Item 0004 showed `marker` and no subtitle.

gfxinfo for those swipes: 283 frames, 80 janky (28.27%), p50 34ms, p90 53ms, p95 61ms, p99 69ms. 19 missed vsync. 36 slow UI thread. 1 slow bitmap upload. 78 slow issue-draw-commands. 80 frame deadlines missed. The UI-thread count and the draw-command count are separate. Draw commands are the larger bucket. This is not the earlier 135-frame session (16 slow UI thread, 36 slow issue-draw-commands).

## Release builds, not published

Built from this tree in this workspace. Not uploaded.

| ABI | sha256 | APK bytes | `.so` bytes | Run |
| --- | --- | ---: | ---: | --- |
| x86_64 | `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7` | 2720557 | 6297424 | The counter-tail series and the row-prefix scroll, above |
| arm64-v8a | `ac502d27eae357b7514787abe4faa245a859380ca3322406cadc41d19905b3d4` | 2573104 | 6507952 | Built, not run |

Package `com.scratch.nativepoc`. Activity `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`. Neither native library contains `npoc-hotreload`.

## Emulator-run, present versus the frame

Measured in this workspace on the x86_64 API 34 emulator. Binary sha256 `f7e2a6411480cc6337c30d995463ae654fc7171f11d92433d6e68acae03ccab9`. packing1000, `am start -W -S`, n=20 after one discarded warmup, every launch COLD. `DisplayPresentTime` was 0 on all 20 first frames.

First present p50 137.0. FrameCompleted p50 482.1. Present to FrameCompleted p50 362.3. FrameCompleted to Displayed p50 23.8. TotalTime and Fully drawn p50 513.0. The app is done at first present. The rest of Displayed is the emulator frame pipeline. `ReportFullyDrawn` matches Displayed and does not pull it earlier. The sample lists are in the README.

## Emulator-run, variable-height rows

One COLD packing1000 launch on the x86_64 API 34 emulator, not an n=20 series. `startup window rows=1000 built=20 views=92`. APK sha256 `25e14d2e2ae7a9d7d39a972fb6713d6b7df3a47fa32634f3e442b4effdc9c734`. Eight swipes moved the window to `first=106` (Item 0106 through Item 0124). Scrolling back returned `first=0` with Item 0000's subtitle and image still on that row. gfxinfo for the swipe session: 135 frames, 37 janky (27.41%), p50 34ms, p90 48ms, p95 53ms, p99 61ms. `cnative agent a11y --check` reported `violations=0` for packing at font scale 1, 1.3, and 2, and for trips at font scale 2. Density 420. The dump has no heading attribute.
