# Performance

Cold-start numbers for the default counter (`com.scratch.nativepoc`) and the showcase (`com.scratch.nativepoc.showcase`). Every row names the method. Percentiles are Hyndman-Fan type 7. A bootstrap interval is B=1000, Python `random.Random` seed 8946, and is the 2.5 and 97.5 percentiles of the resampled p50. `am start -W -S` TotalTime, LaunchState COLD, one discarded warmup. A sample is flagged when the post-sample guest load is above 2.0. A load of exactly 2.0 stays. The device is emulator-5554, API 34, x86_64, SwiftShader. `simpleperf` is on the image and was not recorded. The phase slices below are `atrace`.

## Startup

| Series | TotalTime p50 | Fully drawn p50 | Method |
| --- | --- | --- | --- |
| Counter, framework chrome deferred | 710.0 (683.0–731.5), p90 765.8 | 710.0 (685.0–740.5) | This boot, guest `/proc/uptime` 67385.14. Install once, dexopt `status=verify` `reason=install` before and after, no `cmd package compile -m speed`. Guest load below 2.0 before every sample. `tools/bench/interleave-cold.sh`, pairs counter then showcase, n=20. Fully drawn is the first `Fully drawn <package>/` line after that sample's logcat mark. apk 3,154,805 bytes, sha256 `a0b56b097039340f4e6967ca3fbffd1889da1aaeb30be824442d2329a3f701fb`. Flagged 4. p50 without those samples 719.5 (673.0–746.5), n=16. |
| Showcase, same interleaved run | 596.5 (589.5–617.0), p90 651.1 | 846.0 (775.0–1043.5) | Same method and the same boot. apk 3,850,554 bytes, sha256 `ce9c8386542875a7dd17702e08c6f2c7f2ab00e8ffefad326c5624115d80e6ec`. Publish flags: Showcase, Nav, Persist, Transition, Http, marks off. Image and Recycler follow from Showcase. Flagged 4. p50 without those samples 596.5 (589.5–618.0), n=16. Fully drawn waits for the styled chrome frame, so it sits after TotalTime. |
| Counter, install `verify`, alone | 591.5 (570.0–604.0), p90 630.1 | 597.0, p90 634.8 | Same boot, after the interleaved run. Streamed `adb install --no-incremental` of the counter apk above. dexopt stayed `verify` / `install`. `tools/bench/cold-start.sh --wait-guest --quiesce warn`, n=20. Gate loads 1.88–1.99. Flagged 2. p50 without those samples 591.5, n=18. |
| Counter, install `speed-profile` from `base.dm`, alone | 580.0 (568.0–599.5), p90 620.3 | 587.0, p90 621.4 | Same boot, immediately before the `verify` row. `adb install-multiple` of that apk plus a `base.dm` whose stored entry is the captured `primary.prof`. dexopt `status=speed-profile` `reason=install-dm` before and after. No `cmd package compile -m speed`. Same `cold-start.sh` flags, n=20. Gate loads 0.00–1.95. Flagged 2. p50 without those samples 587.0, n=18. The two solo intervals overlap. |
| Counter, before the chrome deferral, interleaved | 879.0 (859.0–893.0) | 885.5 | Earlier on this emulator, up 17:52, guest `/proc/uptime` 64367.49. Install once, dexopt `verify` / `install`, no speed compile, gate below 2.0, pairs counter then showcase, n=20. apk is the e90430e counter, 3,146,613 bytes, sha256 `95275307bd5e54e4859a8d884b23560466c113a32e6409c0f75e84d474413024`. Flagged 0. |
| Showcase, that same interleaved run | 584.5 (572.0–588.5) | 686.0 | Same method. apk is the 4c2bf63 showcase, 3,559,738 bytes, sha256 `9b58c68e4ef179ef4736cbdad030e604a5427e27251602e93761dcfdefde1759`. Flagged 1 (post-sample load 2.39). p50 without it 584.0, n=19. |
| Counter 52dbcbf, quiet | 651.5 (628.0–677.0) | 661.5 | Same boot as the 879.0 row, same gate, install once, no speed, n=10, one package. apk 3,117,941 bytes, sha256 `91322855c1fc3f10c04511aa8180ef8423527b8991601ecb5d7969526d99a32a`. Flagged 1. p50 without it 656.0, n=9. |
| Counter e90430e, quiet | 671.5 (629.0–692.1) | 674.5 | Immediately after the 52dbcbf row, same method, n=10. Flagged 0. |

The 710.0 and 596.5 rows are the same-method comparison after the chrome change. The 591.5 row is that counter alone, on a quieter stretch than the interleaved run. The 580.0 row is the profile experiment in the next section. Older series, including the `mac-bench.sh` counter and floor lists, stay in the README and in [devx/evidence/cold-start.txt](devx/evidence/cold-start.txt).

## First frame

One `atrace` pair, before the deferral, marks off, both packages `verify`. Counter TotalTime 891. Showcase TotalTime 547.

Counter, milliseconds from the process: PostFork 33.1, ActivityThreadMain 18.2, OpenDex 6.4, bindApplication 84.3 (monitor contention 24.9, resources overlay 12.8, setupGraphicsSupport 22.4), performCreate 24.6, activityStart 60.0, activityResume 5.3, view measure 1.9. The first `Choreographer#doFrame` is 571.1. The slice named `layout` inside it is 558.2 and is the GPU: setSurface 281.0, then shader compiles of NonAALatticeOp 49.3, FillRectOp 54.1, ShadowCircularRRectOp 33.4, CircularRRectOp 47.7, AtlasTextOp 7.8, and eglSwap 69.4. `startup phase=nav` was 23 µs, `decode-ms=0`, `loaded=0`, `staged=1`. Release does not open `nativepoc-state.bin` on that launch.

Showcase, same trace session: ZygoteInit 11.9, ActivityThreadMain 16.2, OpenDex 1.2, bindApplication 38.5, performCreate 23.2, activityStart 81.6, activityResume 3.7, view measure 3.8. The first frame is 248.7. Its shaders are AtlasTextOp and FillRectOp. `startup phase=state-load`, `nav-restore`, and `persist-read` were 0 ms. Both packages use `NativePocTheme`, with `windowBackground` and `windowSplashScreenBackground` set to `@color/page`.

The counter's buttons have no authored style, so the first frame draws the Material button: a rounded nine-patch and a raised shadow. The field draws `edit_text_material`. The showcase's buttons already have a background and a radius, so the first frame is a flat color and the gradient, shadow, and outline wait for the idle handler. `StartupButton` and `AndroidField` now keep those framework drawables, paint a transparent `ColorDrawable` for the reported frame, and put the saved drawables back from `StartupMarks.AfterPresent`. An authored background replaces the placeholder, so that restore leaves it alone. A later screen, after `StartupMarks.Presented`, restores immediately.

One counter trace after that change, TotalTime 660: bindApplication 37.7, first frame 288.0. The shaders inside that frame are AtlasTextOp 11.6 and FillRectOp 57.6. A later traversal of 427 ms is the restored framework chrome. View measure on the first frame is the open slice that contains setSurface 111.9 and postAndWait 158.9.

## Baseline profile

`classes.dex` in the counter apk is 55,872 bytes. Install-time dex2oat at `compiler-filter=verify` took 67.601 ms inside a 113 ms ArtService wall time and wrote a 74,768-byte odex. OpenDex on the counter trace above is 6.4 ms.

A warm launch wrote `primary.prof`, 293 bytes, ART profile version 015. `profman --dump-only` matches the installed dex. The methods in it are the Java interop stubs (`MainActivity`, `StartupButton`, `AndroidField`, `NativeAotRuntimeProvider`, the mono listeners). Copying that file to `assets/dexopt/baseline.prof` and to `assets/art-profile/baseline.prof`, then re-signing, left the streamed install at `verify`. artd logged `Merge skipped because there are no existing profiles`.

`adb install-multiple` of the original apk and a `base.dm` (a stored zip of that `primary.prof`) did apply. dexopt became `speed-profile` / `install-dm`. dex2oat wall time was 118 ms and the odex was 129,544 bytes. The solo rows in the table are that install and a fresh streamed install of the same apk with no dm. The p50 moved from 591.5 to 580.0, and the intervals overlap. The template does not ship a `baseline.prof`. `NativePocAotProfile` is the NativeAOT profile, which is a different file.

## Fully drawn

`tools/bench/interleave-cold.sh` clears logcat after the guest gate, writes `nativepoc-sample <name> <index>`, and reads the first Fully drawn line for that package after the mark. The mark match stops before another digit, so sample 1 is not sample 10. `+1s40ms` is 1040 ms. `cold-start.sh --self-test` runs that parser check.
