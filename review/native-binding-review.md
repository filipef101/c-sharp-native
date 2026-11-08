# Independent review of the Spark 1.2.0 binding probe

The question is whether `b77c4c8653310df6362ddc0e02004ebd7e6fffd3` on `feat-sparkline-aar-binding-0a93` shows that a modern .NET Android binding of the 2016 Spark AAR can deliver a Java callback under Release NativeAOT, and whether that result belongs in the framework. It does not. The probe is a separate executable under `compatibility/`. `src/` and `samples/` are untouched. This file is the review. It does not change the probe.

throughput checkpoint: n/a, read-only investigation.

## Overview

The probe binds `com.robinhood.spark:spark:1.2.0` with the .NET 10 Android generator, wraps `SparkView` in `SparklineViewAdapter`, and runs that wrapper from `compatibility/Sparkline.Probe`. Release is `PublishAot` for `android-x64`. The committed APK runs. On this machine it logged `dynamic=False compiled=False`, then one scrub of `index=1 value=4`, then `unsubscribe` with `listener-null=True` and no later callback line.

That is enough to keep third-party AAR binding at the application. It is not enough to say the managed and Java peers were disposed, or that a duplicate callback was observed and suppressed. The recommendation in `docs/compatibility/binding-decision.md` is the right scope. The evidence table in that file overstates two rows.

## Key concepts

`Sparkline.Binding` is the generator project. It points `AndroidLibrary` at `libs/spark-1.2.0.aar` and at `support-annotations-26.1.0.jar` with `Bind=false`. `Transforms/Metadata.xml` removes the annotations package and `SparkView.FillType`.

`ScrubBridge` is a `Java.Lang.Object` that implements `SparkView.IOnScrubListener`. It holds a nullable reference to `SparklineViewAdapter`. `SparklineViewAdapter` owns the `SparkView`, the `FloatSeriesBridge`, and the `ScrubBridge`. The activity sees `SparklineSeries`, the adapter, and `SparklineScrub`. It does not name a Java type.

`Unsubscribe` sets a flag, assigns `ScrubListener = null`, logs whether the property reads back null, and then calls `Detach`, which clears the bridge's owner. `Dispose` calls `Unsubscribe`, drops the C# event, and calls `Dispose` on the points bridge, the scrub bridge, and the view. The Unsubscribe button does not call `Dispose`.

## How it works

`MainActivity.OnCreate` builds a series of eight floats, `1, 4, 2, 7, 3, 8, 5, 6`, and a `SparklineViewAdapter`. The adapter constructs a `SparkView`, sets `ScrubEnabled` to true, and assigns one adapter and one scrub listener. The packaged style in the AAR sets `spark_scrubEnabled` to false. The constructor override is what turns scrubbing on. A scrub calls `ScrubBridge.OnScrubbed`, then `SparklineViewAdapter.Receive`, then the C# `Scrubbed` event. An integer index is looked up in the series. A null value is the end of the gesture.

`Receive` drops a second event when the index equals `_lastIndex`, and a second end when `_lastEnded` is already true. Both paths log `suppress-duplicate`. Neither log in the commit contains that line. The Release log and the Debug log each contain two gestures. Each gesture is one `raw index=1`, one `emit index=1 value=4`, one `raw ended`, and one `emit ended`.

The Recreate button calls `Activity.Recreate`. `OnDestroy` removes the C# handler and calls `Dispose`. The committed Release log for pid 7903 is `destroy id=1`, `unsubscribe id=1 listener-null=True`, `dispose id=1`, then `create id=2` on the same pid. The next scrub is `id=2` only. That shows the new activity installed a new listener. The `dispose id=1` line is the last statement of `Dispose`, so `_points.Dispose()`, `_scrub.Dispose()`, and `_view.Dispose()` returned. The log does not record a JNI handle, a Java identity, or a later call on those objects.

The Unsubscribe button removes the C# handler, calls `Unsubscribe`, and sets the status text. It leaves `_adapter` and the view in place. The committed Release log ends at `unsubscribe id=2 listener-null=True`. There is no `dispose id=2`. The screenshot `release-unsubscribed.png` shows the chart still on screen and the status `unsubscribed`. `observe-probe.sh` takes that screenshot before the third press. The third press is checked by counting `raw index=` lines. The count stays at 2. The saved log also has no `raw-after-unsubscribe` line, which is what `ScrubBridge` logs when Java calls it after `Detach`.

## Where things live

`compatibility/Sparkline.Binding` holds the AAR, the annotations jar, `NOTICE.txt`, `licenses/Apache-2.0.txt`, and `Transforms/Metadata.xml`.

`compatibility/Sparkline.Adapter` holds the view, the scrub bridge, and the series. `compatibility/Sparkline.Probe` is the executable. `NativePoc.slnx` does not list these projects.

`docs/compatibility/binding-decision.md` is the writeup. `docs/compatibility/evidence/release-sparkprobe.log` and `debug-sparkprobe.log` are the callback logs. `docs/compatibility/artifacts/sparkline-probe-nativeaot-android-x64.apk` is the signed Release APK.

`src/NativePoc.Android/AndroidHost.cs` reaches the clipboard with `GetSystemService` and `ClipboardManager`. That call has no binding project.

## Gotchas

**Unsubscribe is not dispose.** The button path clears the Java listener property and the managed owner pointer. The chart stays. `Dispose` runs on `OnDestroy`, which the Recreate button causes and the Unsubscribe button does not. `listener-null=True` means the property read back null. It does not mean the `SparkView` or the `ScrubBridge` peer was released. For id=1, peer `Dispose` was invoked and returned. For id=2, it was not invoked at all.

**The duplicate-callback claim is not warranted.** `binding-decision.md` marks "Duplicate delivery" as RUN, and the same page says the suppressor did not run because Spark did not repeat an index. The second sentence matches the logs. The table row does not. No `suppress-duplicate` line exists in either committed log. A second scrub on the committed APK, done for this review, also logged one index and no suppressor line. The code path is untested. The observation that these gestures logged one index each is warranted.

**The harness can pass without proving a hit.** `observe-probe.sh` fails the unsubscribe check only when `raw index=` increases. A press that misses the chart passes. A callback that stops in `Detach` also passes, because that path logs `raw-after-unsubscribe` and does not log `raw index=`. The committed log happens to contain neither extra line, and the third press reuses coordinates that had just produced a scrub. That is support for "Spark did not call the listener," not a proof that a disposed peer was silent.

**The publish log does not say the process exited 0.** `docs/compatibility/evidence/release-publish.log` is 12 lines. It names the three assemblies, the trimmer, native code generation, and two ILC notes about `monodroid_unhandled_exception` and `monodroid_debugger_unhandled_exception`. It has no `Build succeeded` line and no exit code. It also has no `IL3050`, no `IL3053`, no `XA1040`, and no `BG8C01`. The Debug log does say `0 Warning(s)` and `0 Error(s)`. The sentence that the generator still warns `BG8C01` is not in any saved log. The class file supports the premise of that sentence. `ScrubGestureDetector$ScrubListener` is an interface with flags `0x0600`, which is abstract and not public. `SparkView$OnScrubListener` is public. The public listener is the one `ScrubBridge` implements.

**`IL3053` is hidden on the probe project only.** `Sparkline.Probe.csproj` puts `IL3053` in `NoWarn` and `XA1040` in `WarningsNotAsErrors`. `Sparkline.Adapter.csproj` sets `TreatWarningsAsErrors` and does not mention `IL3053`. `Sparkline.Binding.csproj` sets neither. A missing `IL3050` line in this log means this publish did not print one. It does not mean every binding `ProjectReference` is safe under NativeAOT. The earlier `AotProbe` failure in `NativePoc.Core` is a different call. This Spark run does not refute that result.

## Verdict

**The bounded probe is accepted as an application-level NativeAOT demonstration. It is not a framework feature, and two of its claims are stronger than the logs.**

| Claim | Verdict | Evidence |
| --- | --- | --- |
| Release NativeAOT executed a Java-to-C# scrub | **Pass** | Committed APK sha256 `acfe7905d1fd9f89d0beaa47b6378d40e39097e7a471de5d4feaa7d3a7bbb0aa`, 2457947 bytes. Contents are `classes.dex` at 58648 bytes and `lib/x86_64/libSparkline.Probe.so` at 5522648 bytes. Pid 7903 logged `dynamic=False compiled=False` and `emit index=1 value=4`. This review installed that same APK on the local KVM emulator, API 34, `x86_64`. Pid 11783 logged the same flags and the same emit. Component `com.scratch.sparklineprobe/crc6434c5177003199d2f.MainActivity`. |
| Callback ownership is a single registered listener | **Pass for the shape, one registration observed** | `SparklineViewAdapter` assigns `ScrubListener` once and logs `listener-set`. The activity only handles `SparklineScrub`. `Detach` clears the owner before a late JNI call would reach `Receive`. No log shows a second `listener-set` for the same id. |
| Recreate keeps the process and installs a new listener | **Pass on the committed log** | Pid 7903 stays through `dispose id=1` and `create id=2`. The following scrub is `id=2`. This review did not press Recreate. |
| Unsubscribe stops delivery | **Pass for the listener property and the following press** | `listener-null=True` on id=2 in the committed log, and on id=1 in this review. The following press added no `raw index=` and no `raw-after-unsubscribe`. The view stayed on screen. |
| Dispose released the managed and Java peers | **Not shown** | `dispose id=1` means `Dispose` returned during recreate. There is no handle, no peer identity, and no call after dispose. Id=2 is never disposed. Unsubscribe must not be scored as dispose. |
| Duplicate callbacks were suppressed | **Not shown** | Zero `suppress-duplicate` lines. One index per gesture is what happened. The suppressor was not exercised. |
| AOT reflection warnings behaved as claimed | **Partial** | The saved Release log prints two workload ILC notes and no trim warning from the adapter or the probe. It does not record exit 0. `BG8C01` is absent from the Debug log, which reports zero warnings. `XA1040` is absent, and the project would not fail the build if it appeared. |
| License and source match the published artifacts | **Pass for the bytes** | `spark-1.2.0.aar` sha256 `bf8841ce3cb9b583c161acf70792d664088c132df73e0ec72cc01f57f788d534` matches `https://repo1.maven.org/maven2/com/robinhood/spark/spark/1.2.0/spark-1.2.0.aar` byte for byte. The annotations jar sha256 `99d6199ad5a09a0e5e8a49a4cc08f818483ddcfd7eedea2f9923412daf982309` matches the Google Maven jar byte for byte. The AAR manifest is package `com.robinhood.spark`, `minSdkVersion` 14, `targetSdkVersion` 26, and it requests no permissions. The AAR archive contains no LICENSE entry. `licenses/Apache-2.0.txt` and `NOTICE.txt` are added by this repo. |
| Keep typed SDK use minimal and leave AAR binding at the app | **Agree** | `git diff origin/main...b77c4c8 --stat` changes only `compatibility/` and `docs/compatibility/`. The solution file does not include the probe. The clipboard path in `AndroidHost.cs` is a framework service lookup. This library also needed a metadata edit, an unbound annotations jar, and a hand-written listener lifetime. One working AAR is an application project. |

## Recommendation

Keep framework code on the wrappers the workload already ships. Add a binding project in the application that needs a specific library. Do not add a general AAR-binding feature to the Native PoC.

| Choice | What the app owner keeps | What this probe showed |
| --- | --- | --- |
| SDK service, as `ClipboardManager` does | No binding project and no `Metadata.xml` | Already used under `src/NativePoc.Android` |
| One binding project next to the feature | The AAR, the metadata edits, and the listener lifetime | Spark scrub ran under NativeAOT on `android-x64` |
| A framework API for arbitrary AARs | Name collisions, package-private interfaces, and NativeAOT warnings for every library | Not built, and this result does not justify it |

## Evidence gaps

- `release-publish.log` has no exit line. The runnable APK is the proof that a NativeAOT image was produced.
- No artifact records `ro.build.version.sdk` for the original run. The writeup names an `android-36` image. This review's install ran on API 34.
- `kvm-fd.txt` is the line `kvm-fd=yes pid=6985`. This review did not inspect that process.
- `arm64` is unverified, as the writeup says.
- Peer disposal has no JNI handle and no post-dispose call.
- `BG8C01` is not in a saved build log.
- The duplicate suppressor has no triggering input.
- The third press has no screenshot. The unsubscribed screenshot is taken before that press.
- The AAR was not compared with a GitHub tag. It was compared with the Maven bytes.
- This review did not press Recreate on its own install.

## How this review was run

pstack `0.15.36` is installed at `/home/ubuntu/.cursor/plugins/cache/cursor-public/9717366/ae5650febf4320793844c12c4adaa937f417f8da`. `plugin.json` in that directory is the version source. `~/.cursor/rules/pstack-models.mdc` is absent, so there is no installed role override.

Files read and applied:

- `skills/poteto-mode/SKILL.md`
- `skills/poteto-mode/playbooks/investigation.md`
- `skills/how/SKILL.md`, `skills/how/references/explorer-prompt.md`, and `skills/how/references/explainer-prompt.md`
- `skills/why/SKILL.md`
- `skills/setup-pstack/SKILL.md`
- `skills/unslop/SKILL.md`
- `skills/technical-writing/SKILL.md`
- `skills/no-comments/SKILL.md`
- `skills/principle-prove-it-works/SKILL.md`
- `skills/principle-attack-the-premise/SKILL.md`
- `skills/principle-test-behavior-not-implementation/SKILL.md`
- `skills/principle-explain-the-number/SKILL.md`

The matched playbook is Investigation. The how skill would spawn `generalPurpose` explorers on `grok-4.7-xhigh-fast` and one explainer on `claude-opus-5-5-xhigh`, with `readonly: true`. The why skill would spawn investigators on `grok-4.7-xhigh-fast` and a synthesizer on `claude-opus-5-5-xhigh`. The Task schema in this session has no readonly parameter. The user required every role to be Grok 4.7 without `-fast`, and forbade another family. That nested fan-out is unrepresentable under those constraints. The review was done in this session, in order, by reading the commit in a detached worktree at `/tmp/spark-b77c4c8`. This session did not spawn a second reviewer and does not call its own pass an independent second opinion.

`no-comments` was skipped. That leaf spawns Comment Sicko and then edits the scoped diff. The user forbade `src/` and sample edits, and forbade changes to the probe branch. The current branch stayed `feat-android-poc-independent-review-4bad`. The probe branch was fetched and not checked out.

**Prove it works** is why the APK was installed and why the AAR was downloaded from Maven again. **Attack the premise** is why "duplicate delivery passed" and "dispose was observed" were checked against the log lines instead of the table. **Test behavior, not implementation** is why the harness count of `raw index=` is not treated as proof of a dead peer. **Explain the number** was read and not applied to a timing claim. This review reports no duration.
