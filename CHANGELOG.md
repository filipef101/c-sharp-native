# Changelog

Measured claims only. A number here names who measured it. Cold-start sample lists are in [docs/release-notes.md](docs/release-notes.md). The README keeps the earlier emulator boots.

Percentiles are Hyndman-Fan type 7 unless a sentence says otherwise.

## v0.1.0-poc

Observed on AVD `poc34`, API 34, x86_64, KVM. The clips are Release asset `packing-release-nativeaot-x86_64.apk`, sha256 `86d22bdb08537c51d566d0e6736c1b06a0c7c80f050ddc291ca9eda08c825d9d`, checked before install. Packing toggles Canvas tent, renames Ridge map to Trail map, adds Lantern, copies the list, and rotates. The counter types Ada and presses Save. That asset was not timed as a cold start.

## Unreleased

These two APKs are the previous tree. They were not published. This tree's x64 rebuild is sha256 `011a9785723326e575abcf191ff4992e222a0a9a174c88dd9b91364c0276ea7f`, 2724653 bytes, `.so` 6301632 bytes. It is the slot-reuse scroll below. It was not published. arm64 was not rebuilt.

| ABI | sha256 | APK bytes | `.so` bytes |
| --- | --- | ---: | ---: |
| x86_64 | `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7` | 2720557 | 6297424 |
| arm64-v8a | `ac502d27eae357b7514787abe4faa245a859380ca3322406cadc41d19905b3d4` | 2573104 | 6507952 |

Package `com.scratch.nativepoc`. Activity `com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity`. Neither `.so` contains `npoc-hotreload`. The x86_64 APK is the one in the counter-tail series and the row-prefix scroll below. The arm64 APK was built and not run.

### Coordinator-measured, Mac arm64

Not measured in this workspace.

Filipe's MacBook Pro M5, AVD `pixel_8`, `arm64-v8a`, Android 16. `am start -W -S`, LaunchState COLD, n=20 after one warmup. APK `nativepoc-0.2.0-android-arm64.apk`, sha256 `b5b4a809f6e652642e0bea1394a451fa766fa00b0b05d39828773202f4873bac`. logcat `IsDynamicCodeSupported=False`. No Java floor on that run. Counter TotalTime p50 479.5 ms, p90 844.9 ms. packing1000 (`window rows=1000 built=20 views=32`) p50 457.0 ms, p90 619.2 ms.

A later interleaved run on that machine used nativepoc sha256 `ce39c83e7c1aa8aca8334fbbafd7d0dfa9be7e8e75fa933ce42abd111b881acb` and empty Java floor sha256 `7a8b6e2daa328c69a943211fdba85daac225017ff760409d964bbb49b5fefb1c`. One warmup each, then n=20 rounds of native, floor, packing1000. All COLD. Present versus FrameCompleted was not captured.

| Series | p50 | p90 |
| --- | ---: | ---: |
| counter | 463.0 | 760.3 |
| empty Java floor | 400.0 | 506.2 |
| packing1000 | 437.5 | 582.2 |

Counter is 63.0 ms over the floor at p50. Counter p90 is the samples 943 and 948. packing1000's largest sample is 656.

A second run on that Mac, same script `tools/bench/mac-bench.sh --n 20`, all COLD, arm64 APK `ac502d27eae357b7514787abe4faa245a859380ca3322406cadc41d19905b3d4`, floor `7a8b6e2daa328c69a943211fdba85daac225017ff760409d964bbb49b5fefb1c`. Host load average 14.3 at the start. This does not replace the table above.

| Series | p50 | p90 | present p50 | UI p50 | draw p50 |
| --- | ---: | ---: | ---: | ---: | ---: |
| counter | 423.5 | 784.5 | 213.5 | 24.8 | 0.2 |
| empty Java floor | 362.5 | 744.0 | 196.0 | 21.9 | 0.1 |
| packing1000 | 413.0 | 562.6 | 246.0 | 23.9 | 0.2 |

Rounds 0–11 were steady. Rounds 12–16 were noisy in every series, floor included (floor 739, 1008, 789; counter 778, 1111, 688, 843). The p90 tail on this run is host contention. Counter is 61 ms over the floor at p50. Draw p50 on this GPU is 0.2 ms.

### Emulator-run, x86_64 API 34, KVM

AVD `poc34`. `emulator -accel-check` reported KVM. QEMU pid 4419 holds `/dev/kvm` on the boots that say so in the README.

Release cold start, `am start -W -S`, COLD, n=20. The cut that drops stack traces, metrics, and startup hooks, `.so` 6,189,440 bytes: TotalTime p50 766.5 ms, p90 865.8 ms. Managed main p50 32.0, activity p50 50.0, first render p50 101.0, first present p50 136.0. Empty Java floor in that series, 12,631 bytes: TotalTime p50 728.0 ms, p90 817.7 ms. Native TotalTime sat 38.5 ms over that floor.

packing1000 after the 20-slot window, `.so` 6,259,872 bytes: TotalTime p50 599.0 ms, p90 686.1 ms, first render p50 92.5, first present p50 132.0, `rows=1000 built=20 views=32`. The same screen on that boot before the window was TotalTime p50 1939.5 ms.

A later boot of sha256 `b4f3b328ea82ffa33ee9ab15f21e73ea7f265c860e6437044828dd509373dd54`, host load 0.11: packing1000 TotalTime p50 696.5 ms, p90 863.4 ms. Floor sha256 `bb38c40cc4bad863d55741b9c425c78f5f316f71d880ab49da084cebbac43ba0`, 12,649 bytes: TotalTime p50 679.0 ms. Native sat 17.5 ms over that floor.

ReportFullyDrawn binary sha256 `f7e2a6411480cc6337c30d995463ae654fc7171f11d92433d6e68acae03ccab9`, packing1000, n=20, all COLD. First present p50 137.0. FrameCompleted p50 482.1. Present to FrameCompleted p50 362.3. FrameCompleted to Displayed p50 23.8. TotalTime and Fully drawn p50 513.0. `DisplayPresentTime` was 0. The UI thread is done at first present. The rest of Displayed on that boot is the emulator frame pipeline.

Variable-height rows, one COLD packing1000 launch, not an n=20 series. sha256 `25e14d2e2ae7a9d7d39a972fb6713d6b7df3a47fa32634f3e442b4effdc9c734`. `startup window rows=1000 built=20 views=92`. Eight swipes reached `first=106`. Scrolling back kept Item 0000's subtitle and image. gfxinfo: 135 frames, 37 janky (27.41%), p50 34ms, p90 48ms, 16 slow UI thread, 36 slow issue-draw-commands.

`cnative agent a11y --check` reported `violations=0` for packing at font scale 1, 1.3, and 2, and for trips at font scale 2. Density 420. That image's dump has no heading attribute.

Counter versus packing1000 on sha256 `0ac29e1248567a90377ba68d5c4c179145cf8a5f4f7dd2ff5e52e3e671a14da7`, interleaved, n=20, all COLD, host load 0.71 at the start. Counter TotalTime p50 763.0, p90 828.6. packing1000 p50 578.5, p90 641.5. First present p50 138.0 and 137.0. `startup field-focus focused=0` on all 20 counter launches. `startup phase=catalog` was 0 ms on 18 and 1 ms on 2, after first-present. `startup gc` was gen0=1, gen1=0, gen2=0 at activity and at present on every launch of both series. No `Every` and no `Effect` on the counter. First-frame UI thread (DrawStart minus PerformTraversalsStart) p50 110.0 ms and 109.3 ms. Issue-draw-commands (SwapBuffers minus IssueDrawCommandsStart) p50 301.6 ms and 145.7 ms. The x86 gap is that draw-command interval. It is not the Mac two-sample tail.

Row-height prefix, same x64 APK, one COLD packing1000, pid 31636, `rows=1000 built=20 views=92`. gfxinfo reset after the first frame, then eight swipes to `first=174` and back to `first=0`. Canvas stayed 60489. Item 0000 still had `Ridge line` and `marker`. gfxinfo: 283 frames, 80 janky (28.27%), p50 34ms, p90 53ms, 36 slow UI thread, 78 slow issue-draw-commands. Those two counts are separate. Draw commands are the larger bucket. This is not the 135-frame session.

Same swipe script on this boot, before the slot change, field-stroke publish sha256 `8c45935195126497b1dc3e6df2eeb48d83bf4ca9cdea6edf02075a5d9760a7e6` (not kept; the list code matched the previous tree): 249 frames, 65 slow issue-draw-commands, 24 slow UI thread, GPU p50 22ms. Two swipes kept in framestats: 16 janky and 16 slow issue-draw-commands. Command recording p50 2.65ms. GPU p50 22.11ms. The gfxinfo line is Android 14 `kSlowRT` (IssueDrawCommandsStart to FrameCompleted on a missed deadline). `RowWindowPlan` rebinds one slot per row of scroll. Same script on sha256 `011a9785723326e575abcf191ff4992e222a0a9a174c88dd9b91364c0276ea7f`, host load 1.14: 236 frames, 65 slow issue-draw-commands, 20 slow UI thread, `first=159` then `first=0`. Item 0000 kept `Ridge line` and `marker`. Item 0004 kept `marker` and had no subtitle. The bucket did not move. A per-slot hardware layer (86 slow of 229) and a one-view row draw (60 slow of 253, GPU p50 still 21ms) are not in this tree.

Counter first-frame issue-draw-commands, n=10, host load 0.03, p50 290.2ms. One field stroke, n=10, host load 1.05, p50 273.3ms. Ranges overlap. The stroke is not in this tree.

### Emulator-run, dev loop

Same AVD. Fixed-socket reload, n=20, one pid: edit-to-screen p50 59.8246 ms, p90 74.8963 ms. CLI edit-to-reply p50 60.0 ms, p90 72.0 ms.

Packing row-label edit to present, n=12, pid 9382: p50 20.1565 ms, p90 30.7825 ms.

Counter `Screen.cs`, n=12, pid 11590: `OnDraw` p50 41.3773 ms, p90 50.4457 ms. gfxinfo `FrameCompleted` p50 77.5302 ms, p90 94.5824 ms.

Listener pid 20966, n=10. Title `OnDraw` p50 28.5024 ms, frame p50 56.1642 ms. Packing `OnDraw` p50 25.7990 ms, frame p50 53.1393 ms.

Listener pid 32111, n=10, `IsDynamicCodeSupported=False`. Style-only frame p50 50.2316 ms. Image swap frame p50 46.0833 ms. Places effect applied once, frame p50 46.5731 ms. The form note caret stayed `start=13 end=13`. Filled submit logged `share-text chars=13` and `haptic-ok`.

1000-row apply-to-present, n=5 fresh processes. ScrollView, 1037 views: first present p50 1071.3305 ms, TOTAL PSS p50 52,997 KB. Recycled window, 34 views: first present p50 254.0235 ms, TOTAL PSS p50 40,717 KB.

`tools/bench/mac-bench.sh --self-test` checks the interleaved Mac p50/p90 values, the round order, `uptime` load parsing, and the floor contended-round flag. That self-test was run in this workspace. The measurement script was not run on a Mac from this workspace.

`cnative new preview` then `cnative run android --once` on this emulator, Debug `android-x64`, not NativeAOT. Pid 3027. Log: `cnative: show samples/preview/PreviewScreen.cs`, `reloaded in 58.1 ms (apply 57.2 ms)`, `reply Hello program=1 present=1`. The dump showed Hello and Count 0. A tap on Add, same pid, showed Count 1. The sample was deleted and is not in the tree. The headless check of that template taps `preview/add` and goes from Count 0 to Count 1. Tested: Core 55, Devx 97.
