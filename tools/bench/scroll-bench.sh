#!/usr/bin/env bash
# Scroll benchmark for a device that has adb on PATH.
# Installs the APK, launches the activity, waits until the list logs
# list-first=, then resets gfxinfo so frames from earlier screens are
# not in the window. Swipes, then reports frames, janky percent, and
# p50/p90/p99 frame time from framestats. Host load is `uptime`.
# Percentiles are Hyndman-Fan type 7, the same function as mac-bench.sh.
#
#   tools/bench/scroll-bench.sh --self-test
#   tools/bench/scroll-bench.sh --serial emulator-5554 \
#     --apk feed.apk \
#     --activity com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#     --swipes 8 --rounds 1 \
#     --extra "--es nativepoc_root feed"
set -euo pipefail

serial=""
apk=""
activity=""
swipes=8
rounds=1
out=""
extra=""
dry_run=0
self_test=0
wait_seconds=45

usage() {
	cat <<'EOF'
tools/bench/scroll-bench.sh [options]

  --serial <id>     adb serial. --device is the same option.
                    Default: the only device in `adb devices`.
  --apk <path>      APK to install.
  --activity <cmp>  component, package/class.
  --swipes <count>  swipes after the list is showing. Default: 8.
  --rounds <count>  launches. Default: 1.
  --extra <words>   extra arguments to `am start`, for example
                    --extra "--es nativepoc_root feed"
  --out <dir>       raw gfxinfo and the summary. Default: tools/bench/out/scroll-bench
  --wait <seconds>  how long to wait for list-first=. Default: 45.
  --dry-run         print the plan and do not call adb.
  --self-test       check percentile, uptime parsing, and framestats.
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--serial|--device) serial="${2:?}"; shift 2 ;;
		--apk) apk="${2:?}"; shift 2 ;;
		--activity) activity="${2:?}"; shift 2 ;;
		--swipes) swipes="${2:?}"; shift 2 ;;
		--rounds) rounds="${2:?}"; shift 2 ;;
		--extra) extra="${2:?}"; shift 2 ;;
		--out) out="${2:?}"; shift 2 ;;
		--wait) wait_seconds="${2:?}"; shift 2 ;;
		--dry-run) dry_run=1; shift ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

if ! [[ "$swipes" =~ ^[1-9][0-9]*$ ]]; then
	echo "--swipes must be a positive integer" >&2
	exit 2
fi
if ! [[ "$rounds" =~ ^[1-9][0-9]*$ ]]; then
	echo "--rounds must be a positive integer" >&2
	exit 2
fi
if ! [[ "$wait_seconds" =~ ^[1-9][0-9]*$ ]]; then
	echo "--wait must be a positive integer" >&2
	exit 2
fi

percentile() {
	local p="$1"
	shift
	awk -v p="$p" '
		{ v[NR] = $1 }
		END {
			n = NR
			if (n < 1) exit 1
			for (i = 2; i <= n; i++) {
				key = v[i]
				j = i - 1
				while (j >= 1 && v[j] > key) {
					v[j + 1] = v[j]
					j--
				}
				v[j + 1] = key
			}
			h = 1 + (n - 1) * p
			lo = int(h)
			frac = h - lo
			hi = (frac > 0) ? lo + 1 : lo
			if (lo < 1) lo = 1
			if (hi > n) hi = n
			if (lo > n) lo = n
			printf "%.1f", v[lo] + frac * (v[hi] - v[lo])
		}
	'
}

parse_load() {
	printf '%s\n' "$1" | awk '
		{
			line = $0
			key = "load averages:"
			n = index(line, key)
			if (n == 0) {
				key = "load average:"
				n = index(line, key)
			}
			if (n == 0) {
				print "-"
				exit
			}
			rest = substr(line, n + length(key))
			gsub(/,/, " ", rest)
			gsub(/^[ \t]+/, "", rest)
			split(rest, parts, /[ \t]+/)
			if (parts[1] == "")
				print "-"
			else
				print parts[1]
		}
	'
}

host_load() {
	parse_load "$(uptime 2>/dev/null || true)"
}

# Print "ms jank" for each finished framestats row.
# ms is FrameCompleted - IntendedVsync. jank is 1 when FrameCompleted is
# after FrameDeadline. Rows still in progress (0 or INT64_MAX) are skipped.
frame_rows() {
	awk -F, '
		function col(name,    i) {
			for (i = 1; i <= n; i++)
				if (names[i] == name)
					return i
			return 0
		}
		$1 == "Flags" {
			n = split($0, names, ",")
			intended = col("IntendedVsync")
			deadline = col("FrameDeadline")
			completed = col("FrameCompleted")
			next
		}
		intended == 0 || deadline == 0 || completed == 0 { next }
		$1 ~ /^[0-9-]/ {
			a = $(intended) + 0
			d = $(deadline) + 0
			c = $(completed) + 0
			if (a <= 0 || c <= 0 || c >= 9223372036854775807)
				next
			if (c < a)
				next
			jank = (d > 0 && c > d) ? 1 : 0
			printf "%.3f %d\n", (c - a) / 1000000, jank
		}
	' "$1"
}

summary_field() {
	local file="$1"
	local label="$2"
	awk -v label="$label" '
		index($0, label) == 1 {
			line = $0
			sub(/^[^:]+:[ \t]*/, "", line)
			print line
			exit
		}
	' "$file"
}

self_test() {
	local p50 p90 load got
	p50="$(printf '%s\n' 10 30 20 40 | percentile 0.50)"
	p90="$(printf '%s\n' 10 30 20 40 | percentile 0.90)"
	if [[ "$p50" != "25.0" || "$p90" != "37.0" ]]; then
		echo "percentile self-test failed: p50=$p50 p90=$p90" >&2
		exit 1
	fi
	load="$(parse_load " 12:01  up 3 days,  1:02, 2 users, load averages: 1.25 0.90 0.40")"
	if [[ "$load" != "1.25" ]]; then
		echo "load averages self-test failed: $load" >&2
		exit 1
	fi
	load="$(parse_load " 12:01:02 up 1 min,  1 user,  load average: 0.08, 0.03, 0.01")"
	if [[ "$load" != "0.08" ]]; then
		echo "load average self-test failed: $load" >&2
		exit 1
	fi
	local fixture
	fixture="$(mktemp)"
	cat >"$fixture" <<'EOF'
Total frames rendered: 4
Janky frames: 1 (25.00%)
50th percentile: 16ms
90th percentile: 30ms
99th percentile: 40ms
Flags,FrameTimelineVsyncId,IntendedVsync,Vsync,OldestInputEvent,NewestInputEvent,HandleInputStart,AnimationStart,PerformTraversalsStart,DrawStart,FrameDeadline,FrameInterval,FrameStartTime,SyncQueued,SyncStart,IssueDrawCommandsStart,SwapBuffers,FrameCompleted,DequeueBufferDuration,QueueBufferDuration,GpuCompleted,SwapBuffersCompleted,DisplayPresentTime,CommandSubmissionCompleted
0,1,1000000000,1000000000,0,0,0,0,0,0,1012000000,16666666,0,0,0,0,0,1016000000,0,0,0,0,0,0
0,2,2000000000,2000000000,0,0,0,0,0,0,2032000000,16666666,0,0,0,0,0,2008000000,0,0,0,0,0,0
0,3,3000000000,3000000000,9223372036854775807,9223372036854775807,0,0,0,0,3012000000,16666666,0,0,0,0,0,9223372036854775807,0,0,0,0,0,0
EOF
	got="$(frame_rows "$fixture")"
	rm -f "$fixture"
	local lines
	lines="$(printf '%s\n' "$got" | awk 'NF { n++ } END { print n + 0 }')"
	if [[ "$lines" != "2" ]]; then
		echo "framestats self-test kept $lines rows" >&2
		printf '%s\n' "$got" >&2
		exit 1
	fi
	local first_jank second_jank
	first_jank="$(printf '%s\n' "$got" | awk 'NR == 1 { print $2 }')"
	second_jank="$(printf '%s\n' "$got" | awk 'NR == 2 { print $2 }')"
	if [[ "$first_jank" != "1" || "$second_jank" != "0" ]]; then
		echo "framestats jank self-test failed: $got" >&2
		exit 1
	fi
	local times p50f
	times="$(printf '%s\n' "$got" | awk '{ print $1 }')"
	p50f="$(printf '%s\n' "$times" | percentile 0.50)"
	if [[ "$p50f" != "12.0" ]]; then
		echo "framestats percentile self-test failed: $p50f" >&2
		exit 1
	fi
	echo "scroll-bench self-test ok"
}

if [[ "$self_test" -eq 1 ]]; then
	self_test
	exit 0
fi

if [[ -z "$apk" || -z "$activity" ]]; then
	echo "--apk and --activity are required" >&2
	usage >&2
	exit 2
fi
if [[ ! -f "$apk" ]]; then
	echo "APK not found: $apk" >&2
	exit 1
fi

adb_cmd() {
	if [[ -n "$serial" ]]; then
		adb -s "$serial" "$@"
	else
		adb "$@"
	fi
}

if [[ -z "$serial" ]]; then
	online=()
	while IFS= read -r line; do
		case "$line" in
			*"	device") online+=("${line%%	*}") ;;
		esac
	done < <(adb devices)
	if [[ "${#online[@]}" -ne 1 ]]; then
		echo "Pass --serial. Online devices: ${online[*]:-none}" >&2
		exit 1
	fi
	serial="${online[0]}"
fi

pkg="${activity%%/*}"
if [[ -z "$out" ]]; then
	out="$(pwd)/tools/bench/out/scroll-bench"
fi

echo "serial=$serial apk=$apk activity=$activity swipes=$swipes rounds=$rounds extra=${extra:-none}"
if [[ "$dry_run" -eq 1 ]]; then
	echo "dry-run install -r $apk"
	echo "dry-run am force-stop $pkg"
	echo "dry-run am start -W -n $activity ${extra}"
	echo "dry-run wait list-first= then gfxinfo $pkg reset"
	echo "dry-run input swipe x $swipes"
	exit 0
fi

mkdir -p "$out"
install_out="$(adb_cmd install -r "$apk" 2>&1)" || true
if ! printf '%s\n' "$install_out" | grep -q "Success"; then
	printf '%s\n' "$install_out" >&2
	echo "Install failed" >&2
	exit 1
fi

: >"$out/frames.txt"
: >"$out/loads.txt"

one_round() {
	local round="$1"
	local load start_out i found size_line dims width height x y1 y2 swipe
	local gfx summary_frames summary_janky rows janky janky_pct p50 p90 p99
	adb_cmd shell am force-stop "$pkg" >/dev/null 2>&1 || true
	adb_cmd logcat -c >/dev/null 2>&1 || true
	load="$(host_load)"
	printf '%s\n' "$load" >>"$out/loads.txt"
	if [[ -n "$extra" ]]; then
		# shellcheck disable=SC2086
		start_out="$(adb_cmd shell am start -W -n "$activity" $extra 2>&1)" || true
	else
		start_out="$(adb_cmd shell am start -W -n "$activity" 2>&1)" || true
	fi
	printf '%s\n' "$start_out" >"$out/start-$round.txt"
	found=0
	i=0
	while [[ "$i" -lt "$wait_seconds" ]]; do
		if adb_cmd logcat -d -s NativePoc:I 2>/dev/null | grep -q "list-first="; then
			found=1
			break
		fi
		i=$((i + 1))
		sleep 1
	done
	if [[ "$found" -ne 1 ]]; then
		echo "list-first= was not logged within ${wait_seconds}s" >&2
		adb_cmd logcat -d -s NativePoc:I -t 80 >&2 || true
		exit 1
	fi
	adb_cmd shell dumpsys gfxinfo "$pkg" reset >/dev/null 2>&1 || true
	size_line="$(adb_cmd shell wm size | tr -d '\r' | tail -n 1)"
	dims="$(printf '%s\n' "$size_line" | awk '
		{
			n = split($0, parts, /[^0-9x]+/)
			for (i = 1; i <= n; i++)
				if (parts[i] ~ /^[0-9]+x[0-9]+$/)
					last = parts[i]
		}
		END { print last }
	')"
	width="${dims%%x*}"
	height="${dims#*x}"
	if ! [[ "$width" =~ ^[0-9]+$ && "$height" =~ ^[0-9]+$ ]]; then
		echo "Could not read display size from: $size_line" >&2
		exit 1
	fi
	x=$((width / 2))
	y1=$((height * 3 / 4))
	y2=$((height * 3 / 10))
	swipe=0
	while [[ "$swipe" -lt "$swipes" ]]; do
		adb_cmd shell input swipe "$x" "$y1" "$x" "$y2" 250 >/dev/null
		swipe=$((swipe + 1))
	done
	sleep 1
	adb_cmd shell dumpsys gfxinfo "$pkg" >"$out/gfx-$round.txt" || true
	adb_cmd shell dumpsys gfxinfo "$pkg" framestats >"$out/framestats-$round.txt" || true
	summary_frames="$(summary_field "$out/gfx-$round.txt" "Total frames rendered:")"
	summary_janky="$(summary_field "$out/gfx-$round.txt" "Janky frames:")"
	frame_rows "$out/framestats-$round.txt" >"$out/rows-$round.txt"
	rows="$(awk 'END { print NR + 0 }' "$out/rows-$round.txt")"
	janky="$(awk '{ s += $2 } END { print s + 0 }' "$out/rows-$round.txt")"
	if [[ "$rows" -eq 0 ]]; then
		janky_pct="-"
		p50="-"
		p90="-"
		p99="-"
	else
		janky_pct="$(awk -v j="$janky" -v n="$rows" 'BEGIN { printf "%.2f", (100 * j) / n }')"
		p50="$(awk '{ print $1 }' "$out/rows-$round.txt" | percentile 0.50)"
		p90="$(awk '{ print $1 }' "$out/rows-$round.txt" | percentile 0.90)"
		p99="$(awk '{ print $1 }' "$out/rows-$round.txt" | percentile 0.99)"
		awk '{ print $1 }' "$out/rows-$round.txt" >>"$out/frames.txt"
	fi
	{
		echo "round=$round load=$load size=${width}x${height} swipe=${x},${y1},${x},${y2},250"
		echo "summary_frames=${summary_frames:-none}"
		echo "summary_janky=${summary_janky:-none}"
		echo "framestats_rows=$rows framestats_janky=$janky framestats_janky_pct=$janky_pct"
		echo "framestats_p50_ms=$p50 framestats_p90_ms=$p90 framestats_p99_ms=$p99"
		echo "frame_time=FrameCompleted-IntendedVsync jank=FrameCompleted>FrameDeadline percentile=Hyndman-Fan-7"
	} | tee "$out/round-$round.txt"
}

round=0
while [[ "$round" -lt "$rounds" ]]; do
	one_round "$round"
	round=$((round + 1))
done

if [[ -s "$out/frames.txt" ]]; then
	all_p50="$(percentile 0.50 <"$out/frames.txt")"
	all_p90="$(percentile 0.90 <"$out/frames.txt")"
	all_p99="$(percentile 0.99 <"$out/frames.txt")"
else
	all_p50="-"
	all_p90="-"
	all_p99="-"
fi
{
	echo "rounds=$rounds swipes=$swipes"
	echo "pooled_framestats_p50_ms=$all_p50 pooled_framestats_p90_ms=$all_p90 pooled_framestats_p99_ms=$all_p99"
	echo "load=$(tr '\n' ' ' <"$out/loads.txt" | sed 's/ $//')"
} | tee "$out/summary.txt"
