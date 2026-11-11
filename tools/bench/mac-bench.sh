#!/usr/bin/env bash
# Interleaved cold start for a Mac with adb.
# One discarded warmup per series, then n rounds. Each round runs every
# series once, in the order given. Every sample must report LaunchState COLD.
# p50 and p90 are Hyndman-Fan type 7, the same function as cold-start.sh.
# Each sample records `am start -W -S`, NativePoc/Floor logcat marks, and
# the first gfxinfo framestats row after a reset.
#
#   tools/bench/mac-bench.sh --self-test
#   tools/bench/mac-bench.sh --n 20 --out /tmp/mac-bench \
#     --run counter:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#     --run floor:floor.apk:com.scratch.floor/.FloorActivity \
#     --run 'packing:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity:--es nativepoc_bench packing1000'
set -euo pipefail

samples=20
out=""
device=""
self_test=0
dry_run=0
labels=()
apks=()
components=()
extras=()

usage() {
	cat <<'EOF'
tools/bench/mac-bench.sh [options] --run label:apk:component[:am extras]

  --n <count>     samples after the discarded warmup. Default: 20.
  --out <dir>     where the raw starts, logcat, framestats, and summary go.
  --device <serial>
  --run <spec>    repeat for each series. Spec is label:apk:component
                  or label:apk:component:am extras.
  --dry-run       print the interleaved plan and do not call adb.
  --self-test     check Hyndman-Fan type 7 against the interleaved Mac n=20
                  series, and check that a three-series plan round-robins.
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--n) samples="${2:?}"; shift 2 ;;
		--out) out="${2:?}"; shift 2 ;;
		--device) device="${2:?}"; shift 2 ;;
		--run)
			spec="${2:?}"
			label="${spec%%:*}"
			rest="${spec#*:}"
			apk="${rest%%:*}"
			rest="${rest#*:}"
			component="${rest%%:*}"
			extra=""
			if [[ "$rest" == *:* ]]; then
				extra="${rest#*:}"
			fi
			if [[ -z "$label" || -z "$apk" || -z "$component" || "$label" == "$spec" ]]; then
				echo "Bad --run spec: $spec" >&2
				exit 2
			fi
			labels+=("$label")
			apks+=("$apk")
			components+=("$component")
			extras+=("$extra")
			shift 2
			;;
		--dry-run) dry_run=1; shift ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

if ! [[ "$samples" =~ ^[1-9][0-9]*$ ]]; then
	echo "--n must be a positive integer" >&2
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

if [[ "$self_test" -eq 1 ]]; then
	counter=(513 378 463 328 468 382 423 943 948 740 384 531 463 401 444 635 393 423 485 716)
	floor=(423 416 378 501 407 313 410 337 505 502 341 460 367 307 386 296 358 393 530 517)
	packing=(436 302 359 498 377 427 353 656 620 578 447 383 333 324 439 440 557 373 460 474)
	fail=0
	c50="$(printf '%s\n' "${counter[@]}" | percentile 0.50)"
	c90="$(printf '%s\n' "${counter[@]}" | percentile 0.90)"
	f50="$(printf '%s\n' "${floor[@]}" | percentile 0.50)"
	f90="$(printf '%s\n' "${floor[@]}" | percentile 0.90)"
	p50="$(printf '%s\n' "${packing[@]}" | percentile 0.50)"
	p90="$(printf '%s\n' "${packing[@]}" | percentile 0.90)"
	[[ "$c50" == "463.0" ]] || { echo "counter p50 $c50" >&2; fail=1; }
	[[ "$c90" == "760.3" ]] || { echo "counter p90 $c90" >&2; fail=1; }
	[[ "$f50" == "400.0" ]] || { echo "floor p50 $f50" >&2; fail=1; }
	[[ "$f90" == "506.2" ]] || { echo "floor p90 $f90" >&2; fail=1; }
	[[ "$p50" == "437.5" ]] || { echo "packing p50 $p50" >&2; fail=1; }
	[[ "$p90" == "582.2" ]] || { echo "packing p90 $p90" >&2; fail=1; }
	plan="$(bash "$0" --dry-run --n 2 \
		--run counter:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
		--run floor:floor.apk:com.scratch.floor/.FloorActivity \
		--run 'packing:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity:--es nativepoc_bench packing1000')"
	expect=$'warmup counter\nwarmup floor\nwarmup packing\nround 0 counter\nround 0 floor\nround 0 packing\nround 1 counter\nround 1 floor\nround 1 packing'
	if [[ "$plan" != "$expect" ]]; then
		echo "plan mismatch" >&2
		printf '%s\n' "$plan" >&2
		fail=1
	fi
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "self-test p50/p90 Hyndman-Fan 7 matches the interleaved Mac n=20 series"
	exit 0
fi

if [[ "${#labels[@]}" -lt 1 ]]; then
	echo "Pass at least one --run" >&2
	usage >&2
	exit 2
fi

if [[ "$dry_run" -eq 1 ]]; then
	for ((s = 0; s < ${#labels[@]}; s++)); do
		printf 'warmup %s\n' "${labels[$s]}"
	done
	for ((round = 0; round < samples; round++)); do
		for ((s = 0; s < ${#labels[@]}; s++)); do
			printf 'round %s %s\n' "$round" "${labels[$s]}"
		done
	done
	exit 0
fi

if [[ -n "${ANDROID_HOME:-}" && -x "$ANDROID_HOME/platform-tools/adb" ]]; then
	adb_bin="$ANDROID_HOME/platform-tools/adb"
else
	adb_bin="$(command -v adb || true)"
fi
if [[ -z "${adb_bin:-}" ]]; then
	echo "adb not found" >&2
	exit 1
fi

adb_cmd() {
	if [[ -n "$device" ]]; then
		"$adb_bin" -s "$device" "$@"
	else
		"$adb_bin" "$@"
	fi
}

if [[ -z "$device" ]]; then
	online=()
	while IFS= read -r serial; do
		[[ -n "$serial" ]] && online+=("$serial")
	done < <("$adb_bin" devices | awk 'NR > 1 && $2 == "device" { print $1 }')
	if [[ "${#online[@]}" -ne 1 ]]; then
		echo "Pass --device. Online devices: ${online[*]:-none}" >&2
		exit 1
	fi
	device="${online[0]}"
fi

if [[ -z "$out" ]]; then
	out="$(pwd)/tools/bench/out/mac-bench"
fi
mkdir -p "$out"

declare -A installed=()
for ((s = 0; s < ${#labels[@]}; s++)); do
	pkg="${components[$s]%%/*}"
	if [[ -n "${installed[$pkg]:-}" ]]; then
		if [[ "${installed[$pkg]}" != "${apks[$s]}" ]]; then
			echo "Package $pkg is installed from two APKs" >&2
			exit 1
		fi
		continue
	fi
	adb_cmd uninstall "$pkg" >/dev/null 2>&1 || true
	install_out="$(adb_cmd install -r "${apks[$s]}" 2>&1)" || true
	if ! grep -q "Success" <<<"$install_out"; then
		echo "$install_out" >&2
		echo "Install failed for ${labels[$s]}" >&2
		exit 1
	fi
	installed[$pkg]="${apks[$s]}"
done

one_start() {
	local index="$1"
	local -a extra_args=()
	if [[ -n "${extras[$index]}" ]]; then
		read -r -a extra_args <<<"${extras[$index]}"
	fi
	adb_cmd shell am start -W -S -n "${components[$index]}" "${extra_args[@]}"
}

capture_frame() {
	local index="$1"
	local dest="$2"
	local pkg="${components[$index]%%/*}"
	adb_cmd shell dumpsys gfxinfo "$pkg" framestats >"$dest" || true
}

reset_frame() {
	local index="$1"
	local pkg="${components[$index]%%/*}"
	adb_cmd shell dumpsys gfxinfo "$pkg" reset >/dev/null 2>&1 || true
}

echo "device=$device n=$samples series=${labels[*]}" >&2
uptime >&2 || true

for ((s = 0; s < ${#labels[@]}; s++)); do
	adb_cmd logcat -c >/dev/null || true
	reset_frame "$s"
	warmup="$(one_start "$s")"
	if ! grep -q '^LaunchState: COLD$' <<<"$warmup"; then
		echo "$warmup" >&2
		echo "Warmup ${labels[$s]} was not COLD" >&2
		exit 1
	fi
	echo "warmup ${labels[$s]} discarded" >&2
done

for ((s = 0; s < ${#labels[@]}; s++)); do
	: >"$out/${labels[$s]}-starts.txt"
	: >"$out/${labels[$s]}-totals.txt"
	: >"$out/${labels[$s]}-frames.txt"
done

for ((round = 0; round < samples; round++)); do
	for ((s = 0; s < ${#labels[@]}; s++)); do
		label="${labels[$s]}"
		adb_cmd logcat -c >/dev/null || true
		reset_frame "$s"
		text="$(one_start "$s")"
		state="$(printf '%s\n' "$text" | awk '/^LaunchState: / { print $2 }')"
		total="$(printf '%s\n' "$text" | awk '/^TotalTime: / { print $2 }')"
		if [[ "$state" != "COLD" || -z "$total" ]]; then
			echo "$text" >&2
			echo "Sample $label $round was not a COLD TotalTime" >&2
			exit 1
		fi
		{
			echo "ROUND $round"
			printf '%s\n' "$text"
		} >>"$out/$label-starts.txt"
		echo "$total" >>"$out/$label-totals.txt"
		adb_cmd logcat -d -v epoch -s NativePoc:I Floor:I ActivityTaskManager:I >"$out/$label-log-$round.txt" || true
		capture_frame "$s" "$out/$label-gfx-$round.txt"
		echo "$label $round $total" >&2
	done
done

{
	printf '%-12s %4s %8s %8s %10s %10s %10s\n' series n total_p50 total_p90 present_p50 ui_p50 draw_p50
	for ((s = 0; s < ${#labels[@]}; s++)); do
		label="${labels[$s]}"
		p50="$(percentile 0.50 <"$out/$label-totals.txt")"
		p90="$(percentile 0.90 <"$out/$label-totals.txt")"
		presents=()
		uis=()
		draws=()
		for ((round = 0; round < samples; round++)); do
			mark="$(awk '
				/startup mark=first-present since-process-ms=/ {
					value = $0
					sub(/^.*since-process-ms=/, "", value)
					sub(/[^0-9].*$/, "", value)
					print value
					exit
				}
			' "$out/$label-log-$round.txt" || true)"
			if [[ -n "$mark" ]]; then
				presents+=("$mark")
			fi
			frame="$(awk '
				found && $0 ~ /^[0-9]/ { print; exit }
				$0 ~ /^Flags,FrameTimelineVsyncId,/ { found = 1 }
			' "$out/$label-gfx-$round.txt" || true)"
			if [[ -n "$frame" ]]; then
				ui="$(printf '%s\n' "$frame" | awk -F, '{ if ($9 ~ /^[0-9]+$/ && $8 ~ /^[0-9]+$/ && $9 >= $8) printf "%.1f", ($9 - $8) / 1000000 }')"
				draw="$(printf '%s\n' "$frame" | awk -F, '{ if ($16 ~ /^[0-9]+$/ && $15 ~ /^[0-9]+$/ && $16 >= $15) printf "%.1f", ($16 - $15) / 1000000 }')"
				[[ -n "$ui" ]] && uis+=("$ui")
				[[ -n "$draw" ]] && draws+=("$draw")
				printf '%s\n' "$frame" >>"$out/$label-frames.txt"
			fi
		done
		present_p50="-"
		ui_p50="-"
		draw_p50="-"
		if [[ "${#presents[@]}" -eq "$samples" ]]; then
			present_p50="$(printf '%s\n' "${presents[@]}" | percentile 0.50)"
		fi
		if [[ "${#uis[@]}" -eq "$samples" ]]; then
			ui_p50="$(printf '%s\n' "${uis[@]}" | percentile 0.50)"
		fi
		if [[ "${#draws[@]}" -eq "$samples" ]]; then
			draw_p50="$(printf '%s\n' "${draws[@]}" | percentile 0.50)"
		fi
		printf '%-12s %4s %8s %8s %10s %10s %10s\n' "$label" "$samples" "$p50" "$p90" "$present_p50" "$ui_p50" "$draw_p50"
		{
			echo "series=$label n=$samples"
			echo "raw=$(tr '\n' ' ' <"$out/$label-totals.txt" | sed 's/ $//')"
			echo "p50 $p50 p90 $p90"
			echo "present_p50 $present_p50 ui_p50 $ui_p50 draw_p50 $draw_p50"
			echo "ui is DrawStart - PerformTraversalsStart on the first framestats row, milliseconds"
			echo "draw is SwapBuffers - IssueDrawCommandsStart on that row, milliseconds"
			echo "method=am start -W -S LaunchState=COLD warmup=1 discarded per series interleaved=1 percentile=Hyndman-Fan-7"
		} >"$out/$label-summary.txt"
	done
} | tee "$out/summary.txt"
