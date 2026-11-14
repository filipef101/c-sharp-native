# pixel_fold_mc

The MacBook emulator AVD for this bench is named `pixel_fold_mc`. Boot it, then take the serial from `adb devices`. The serial is not the AVD name. These commands were not run on that emulator for this tree.

`tools/bench/pixel-fold.sh` uses adb, awk, and sort. It does not build, and it does not call another script. bash 3.2 is enough. Cold start installs each apk once, waits until the first field of `/proc/loadavg` is below 2.0, discards one warmup of each side, then runs pairs left then right. It writes `DIR/results.txt` with TotalTime and fully drawn, Hyndman-Fan type 7.

For the arm64 Mac emulator AVD `pixel_fold_mc`:

```sh
tools/bench/pixel-fold.sh cold --device <serial> --out ~/pixel-fold-cold --n 20 \
  --left-apk counter-arm64.apk --left-pkg com.scratch.nativepoc --left-name counter \
  --right-apk showcase-arm64.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
```

Scroll installs the apk, starts it, and appends raw `dumpsys gfxinfo <package> framestats` for each round to the same results file.

```sh
tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-scroll \
  --apk feed.apk \
  --activity com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
  --swipes 8 --rounds 5 \
  --extra "--es nativepoc_root feed"
```

`tools/bench/pixel-fold.sh --self-test` checks the percentile and that this file does not delegate. It does not call adb.
