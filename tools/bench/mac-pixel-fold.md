# pixel_fold_mc

The MacBook emulator AVD for this bench is named `pixel_fold_mc`. Boot it, then take the serial from `adb devices`. The serial is not the AVD name. The cold-start numbers from the progress91 arm64 apks on a MacBook M5 host, API 36 arm64 emulator with hardware GL, are in [docs/performance.md](../../docs/performance.md). That run asked for n=20 and recorded about 120 samples a side, because the guest wait reset the loop index. This script keeps the loop index local and writes `n_requested` and `n_run`.

`tools/bench/pixel-fold.sh` uses adb, awk, grep, tail, and sort. It does not build, and it does not call another script. bash 3.2 is enough. Cold start installs each apk once. A signature mismatch (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`) uninstalls that package and retries the install once. It waits until the first field of `/proc/loadavg` is below 2.0, discards one warmup of each side, then runs pairs left then right. It writes `DIR/results.txt` with TotalTime and fully drawn, Hyndman-Fan type 7, and the n that actually ran.

For the arm64 Mac emulator AVD `pixel_fold_mc`:

```sh
tools/bench/pixel-fold.sh cold --device <serial> --out ~/pixel-fold-cold --n 20 \
  --left-apk counter-arm64.apk --left-pkg com.scratch.nativepoc --left-name counter \
  --right-apk showcase-arm64.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
```

The release showcase compiled with `NativePocHttp` includes `samples/feed/FeedScreen.cs`. Its screen id is `feed`. `am start -S` with `--es nativepoc_root feed` opens that screen on `com.scratch.nativepoc.showcase`. Scroll installs the apk, starts it that way, and writes `DIR/results.txt` with janky frame percent and p50/p90/p99 frame time. The same summary is `DIR/summary.json`. The last frame is `DIR/end.png`. The raw `dumpsys gfxinfo framestats` stays in `DIR/framestats-raw.txt`.

```sh
tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-scroll \
  --apk showcase-arm64.apk \
  --activity com.scratch.nativepoc.showcase/crc64ee53024f7ed0aa60.MainActivity \
  --swipes 8 --rounds 5 \
  --extra "--es nativepoc_root feed"
```

`--scenario hard` is the same counts with faster flings, both directions, over the 1000-row feed. The script serves a 640x240 PNG and a small `/feed` document on `127.0.0.1:8094`, and reverse-forwards that port, so row images decode while the list moves. The summary counts `image-bitmap`, `image-fail`, and how many `/img/` responses the server sent. A profile row longer than 2000 ms is left out of the percentiles and counted as `skipped`. The easy scenario is the default. The 0.7% Mac number is the easy one.

```sh
tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-hard \
  --apk showcase-arm64.apk \
  --activity com.scratch.nativepoc.showcase/crc64ee53024f7ed0aa60.MainActivity \
  --swipes 8 --rounds 5 --scenario hard \
  --extra "--es nativepoc_root feed"
```

A screenshot tries the requested display, then each other `mDisplayId`, then a capture with no `-d`. It strips a leading `[Warning] Multiple displays were found` line and rejects a file that is not a PNG, including the 79-byte miss from `-d 0` on the foldable:

```sh
tools/bench/pixel-fold.sh shot --device <serial> --out ~/pixel-fold-shot.png
```

`tools/bench/pixel-fold.sh --self-test` checks the percentile, the loop index, the signature retry, the screenshot strip, `n_run`, and the framestats summary. It does not call adb.
