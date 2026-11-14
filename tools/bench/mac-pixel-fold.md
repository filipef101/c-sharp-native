# pixel_fold_mc

The MacBook emulator AVD for this bench is named `pixel_fold_mc`. Boot it, then take the serial from `adb devices`. The serial is not the AVD name. These commands were not run on that emulator for this tree.

Cold start is the same interleaved pair as `tools/bench/interleave-cold.sh`: install each apk once, no `cmd package compile -m speed`, guest load below 2.0 before every sample, pairs left then right, one discarded warmup, `am start -W -S`. Fully drawn is the line after that sample's logcat mark. On this tree that line is the visually complete frame.

```sh
tools/bench/pixel-fold.sh cold --device <serial> --out /tmp/pixel-fold-cold --n 20 \
  --left-apk counter.apk --left-pkg com.scratch.nativepoc --left-name counter \
  --right-apk showcase.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
```

Feed scroll is `tools/bench/scroll-bench.sh`. It launches the feed, resets gfxinfo, swipes, and reads frame time from `dumpsys gfxinfo <package> framestats`. Frame time is FrameCompleted minus IntendedVsync.

```sh
tools/bench/pixel-fold.sh scroll --device <serial> --out /tmp/pixel-fold-scroll \
  --apk feed.apk \
  --activity com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
  --swipes 8 --rounds 5 \
  --extra "--es nativepoc_root feed"
```

`tools/bench/pixel-fold.sh --self-test` checks that plan and does not call adb.
