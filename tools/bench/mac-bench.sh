#!/usr/bin/env bash
# Interleaved cold start for a Mac with adb.
# One discarded warmup per series, then n rounds. Each round runs every
# series once, in the order given. --repeat K runs K independent blocks,
# each with its own warmup. Every sample must report LaunchState COLD.
# p50 and p90 are Hyndman-Fan type 7, the same function as cold-start.sh.
# Each sample records `am start -W -S`, NativePoc/Floor logcat marks, the
# first gfxinfo framestats row after a reset, and the host 1-minute load
# from `uptime` at the start of the round. A floor sample above 1.5x the
# floor median is printed as contended. Those rounds stay in p50 and p90.
#
#   tools/bench/mac-bench.sh --self-test
#   tools/bench/mac-bench.sh --n 20 --out /tmp/mac-bench \
#     --run counter:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#     --run floor:floor.apk:com.scratch.floor/.FloorActivity \
#     --run 'packing:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity:--es nativepoc_bench packing1000'
# Feed cold start, interleaved with the counter. Both series use the same feed
# APK. mac-bench.sh refuses one package installed from two APK paths. The
# Java floor is a different package, so it can sit in the same run. The
# summary prints TotalTime and, when the log has them, since-process p50 for
# runtime, render, attach, bind, draw, and list-first. Each round also
# records guest loadavg, top, whether dex2oat is running, and the GLES
# renderer. Guest load at or above 2.0 prints a warning. The serial is the
# pixel_8 emulator.
#   tools/bench/mac-bench.sh --device <pixel_8> --n 10 --out /tmp/feed-cold \
#     --run counter:nativepoc-feed-android-arm64.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#     --run feed:nativepoc-feed-android-arm64.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity:'--es nativepoc_root feed' \
#     --run floor:nativepoc-listfloor-android-arm64.apk:com.scratch.listfloor/.ListFloorActivity
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/guest-env.sh"
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/portable.sh"

samples=20
repeat=1
out=""
device=""
self_test=0
dry_run=0
quiesce=0
quiesce_mode="refuse"
force_stop_others=0
labels=()
apks=()
components=()
extras=()

usage() {
	cat <<'EOF'
tools/bench/mac-bench.sh [options] --run label:apk:component[:am extras]

  --n <count>     samples after the discarded warmup. Default: 20.
  --repeat <K>    independent interleaved blocks. Default: 1.
                  Prints each block's feed-floor p50 delta, the median of
                  those deltas, and a bootstrap 95% interval.
  --out <dir>     where the raw starts, logcat, framestats, and summary go.
  --device <serial>
  --run <spec>    repeat for each series. Spec is label:apk:component
                  or label:apk:component:am extras.
  --dry-run       print the interleaved plan and do not call adb.
  --quiesce [warn|refuse]
                  before each series warmup, force-stop every bench package,
                  then list other foreground and top-CPU packages.
                  refuse (the default) stops the series and names them.
                  warn names them and starts. Bench packages are stopped
                  in either mode.
  --force-stop-others
                  with --quiesce, am force-stop those packages, then start.
                  system_server and the series package are never stopped.
  --self-test     check Hyndman-Fan type 7 against the interleaved Mac n=20
                  series, the round order, uptime load parsing, and the
                  floor contended-round flag.
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--n) samples="${2:?}"; shift 2 ;;
		--repeat) repeat="${2:?}"; shift 2 ;;
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
		--quiesce)
			quiesce=1
			if [[ "${2:-}" == "warn" || "${2:-}" == "refuse" ]]; then
				quiesce_mode="$2"
				shift 2
			else
				quiesce_mode="refuse"
				shift
			fi
			;;
		--force-stop-others) force_stop_others=1; shift ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

if ! [[ "$samples" =~ ^[1-9][0-9]*$ ]]; then
	echo "--n must be a positive integer" >&2
	exit 2
fi
if ! [[ "$repeat" =~ ^[1-9][0-9]*$ ]]; then
	echo "--repeat must be a positive integer" >&2
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

# One delta per line. Median is Hyndman-Fan type 7. The interval is the
# 2.5 and 97.5 percentiles of B=1000 resampled medians. The generator is
# Park-Miller, seed 8946, which stays inside the 53-bit integer range.
bootstrap_ci() {
	awk '
		function next_seed() {
			seed = (seed * 48271) % 2147483647
			if (seed < 0)
				seed += 2147483647
			return seed
		}
		function percentile(v, n, p,    h, lo, hi, frac, i, j, key) {
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
			return v[lo] + frac * (v[hi] - v[lo])
		}
		{ d[NR] = $1 + 0 }
		END {
			n = NR
			if (n < 1) exit 1
			seed = 8946
			for (b = 1; b <= 1000; b++) {
				delete sample
				for (i = 1; i <= n; i++)
					sample[i] = d[int(next_seed() % n) + 1]
				med[b] = percentile(sample, n, 0.50)
			}
			point = percentile(d, n, 0.50)
			low = percentile(med, 1000, 0.025)
			high = percentile(med, 1000, 0.975)
			printf "median=%.1f low=%.1f high=%.1f\n", point, low, high
		}
	'
}

# Lines are "round total load" when a floor total is above 1.5x its median.
# The median is Hyndman-Fan type 7. Those rounds are not removed.
contended_rounds() {
	local totals_file="$1"
	local loads_file="$2"
	local median
	median="$(percentile 0.50 <"$totals_file")"
	awk -v median="$median" '
		NR == FNR {
			load[FNR] = $0
			next
		}
		($1 + 0) > (1.5 * median) {
			value = load[FNR]
			if (value == "")
				value = "-"
			printf "%d %s %s\n", FNR - 1, $1, value
		}
	' "$loads_file" "$totals_file"
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
	repeat_plan="$(bash "$0" --dry-run --repeat 2 --n 2 \
		--run counter:nativepoc.apk:com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
		--run floor:floor.apk:com.scratch.floor/.FloorActivity)"
	expect_repeat=$'block 0\nwarmup counter\nwarmup floor\nround 0 counter\nround 0 floor\nround 1 counter\nround 1 floor\nblock 1\nwarmup counter\nwarmup floor\nround 0 counter\nround 0 floor\nround 1 counter\nround 1 floor'
	if [[ "$repeat_plan" != "$expect_repeat" ]]; then
		echo "repeat plan mismatch" >&2
		printf '%s\n' "$repeat_plan" >&2
		fail=1
	fi
	ci="$(printf '%s\n' 1 2 3 4 5 6 7 8 9 10 | bootstrap_ci)"
	[[ "$ci" == "median=5.5 low=3.0 high=8.0" ]] || { echo "bootstrap $ci" >&2; fail=1; }
	linux_load="$(parse_load ' 12:00:00 up 1 day,  2:03,  1 user,  load average: 14.30, 10.10, 8.00')"
	mac_load="$(parse_load '15:23  up 2 days,  3:14, 2 users, load averages: 14.30 10.10 8.00')"
	[[ "$linux_load" == "14.30" ]] || { echo "linux load $linux_load" >&2; fail=1; }
	[[ "$mac_load" == "14.30" ]] || { echo "mac load $mac_load" >&2; fail=1; }
	flag_dir="$(mktemp -d)"
	printf '%s\n' 10 10 10 10 40 10 10 10 10 10 >"$flag_dir/floor"
	printf '%s\n' 10 10 10 10 10 10 10 10 10 10 >"$flag_dir/quiet"
	printf '%s\n' 0.20 0.20 0.20 0.20 14.30 0.20 0.20 0.20 0.20 0.20 >"$flag_dir/load"
	flagged="$(contended_rounds "$flag_dir/floor" "$flag_dir/load")"
	[[ "$flagged" == "4 40 14.30" ]] || { echo "flag $flagged" >&2; fail=1; }
	none="$(contended_rounds "$flag_dir/quiet" "$flag_dir/load")"
	[[ -z "$none" ]] || { echo "quiet flag $none" >&2; fail=1; }
	rm -rf "$flag_dir"
	bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/guest-env.sh" --self-test >/dev/null
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "self-test p50/p90 Hyndman-Fan 7 matches the interleaved Mac n=20 series"
	exit 0
fi

if [[ "$force_stop_others" -eq 1 && "$quiesce" -eq 0 ]]; then
	echo "--force-stop-others requires --quiesce" >&2
	exit 2
fi

if [[ "${#labels[@]}" -lt 1 ]]; then
	echo "Pass at least one --run" >&2
	usage >&2
	exit 2
fi

if [[ "$dry_run" -eq 1 ]]; then
	for ((block = 0; block < repeat; block++)); do
		if [[ "$repeat" -gt 1 ]]; then
			printf 'block %s\n' "$block"
		fi
		for ((s = 0; s < ${#labels[@]}; s++)); do
			printf 'warmup %s\n' "${labels[$s]}"
		done
		for ((round = 0; round < samples; round++)); do
			for ((s = 0; s < ${#labels[@]}; s++)); do
				printf 'round %s %s\n' "$round" "${labels[$s]}"
			done
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
		"$adb_bin" -s "$device" "$@" </dev/null
	else
		"$adb_bin" "$@" </dev/null
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

installed_file="$out/installed-packages.tsv"
: >"$installed_file"
for ((s = 0; s < ${#labels[@]}; s++)); do
	pkg="${components[$s]%%/*}"
	state="$(installed_remember "$installed_file" "$pkg" "${apks[$s]}")"
	if [[ "$state" == "conflict" ]]; then
		echo "Package $pkg is installed from two APKs" >&2
		exit 1
	fi
	if [[ "$state" == "same" ]]; then
		continue
	fi
	adb_cmd uninstall "$pkg" >/dev/null 2>&1 || true
	install_out="$(adb_cmd install -r "${apks[$s]}" 2>&1)" || true
	if ! grep -q "Success" <<<"$install_out"; then
		echo "$install_out" >&2
		echo "Install failed for ${labels[$s]}" >&2
		exit 1
	fi
	package_background "$pkg" "$out/package-$pkg.txt"
done

one_start() {
	local index="$1"
	local -a extra_args=()
	quiesce_stop_bench
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

echo "device=$device n=$samples repeat=$repeat series=${labels[*]}" >&2
uptime >&2 || true

block_n="$samples"
blocks="$repeat"
for ((s = 0; s < ${#labels[@]}; s++)); do
	: >"$out/${labels[$s]}-starts.txt"
	: >"$out/${labels[$s]}-totals.txt"
	: >"$out/${labels[$s]}-frames.txt"
	: >"$out/${labels[$s]}-loads.txt"
done
: >"$out/round-load.txt"
: >"$out/guest.txt"

for ((s = 0; s < ${#labels[@]}; s++)); do
	quiesce_add_package "${components[$s]%%/*}"
done
flat=0
for ((block = 0; block < blocks; block++)); do
	if [[ "$blocks" -gt 1 ]]; then
		echo "block $block" >&2
	fi
	for ((s = 0; s < ${#labels[@]}; s++)); do
		if [[ "$quiesce" -eq 1 ]]; then
			quiesce_before "${components[$s]%%/*}"
		fi
		adb_cmd logcat -c >/dev/null || true
		reset_frame "$s"
		warmup="$(one_start "$s")"
		if ! grep -q '^LaunchState: COLD$' <<<"$warmup"; then
			echo "$warmup" >&2
			echo "Warmup ${labels[$s]} block $block was not COLD" >&2
			exit 1
		fi
		echo "warmup ${labels[$s]} block $block discarded" >&2
	done
	for ((round = 0; round < block_n; round++)); do
		load="$(host_load)"
		printf '%s\n' "$load" >>"$out/round-load.txt"
		echo "block $block round $round load=$load" >&2
		for ((s = 0; s < ${#labels[@]}; s++)); do
			label="${labels[$s]}"
			quiesce_stop_bench
			guest_line="$(capture_guest "$out/$label-guest-$flat.txt")"
		printf '%s\n' "label=$label block=$block round=$round $guest_line" >>"$out/guest.txt"
		guest_value="$(printf '%s\n' "$guest_line" | awk 'NR == 1 { for (i = 1; i <= NF; i++) if ($i ~ /^guest_load=/) { split($i, a, "="); print a[2]; exit } }')"
		printf '%s\n' "$guest_value" >>"$out/$label-loads.txt"
		adb_cmd logcat -c >/dev/null || true
		reset_frame "$s"
		text="$(one_start "$s")"
		state="$(printf '%s\n' "$text" | awk '/^LaunchState: / { print $2 }')"
		total="$(printf '%s\n' "$text" | awk '/^TotalTime: / { print $2 }')"
		if [[ "$state" != "COLD" || -z "$total" ]]; then
			echo "$text" >&2
			echo "Sample $label block $block round $round was not a COLD TotalTime" >&2
			exit 1
		fi
		{
			echo "ROUND $flat"
			echo "block=$block"
			echo "load=$load"
			printf '%s\n' "$guest_line"
			printf '%s\n' "$text"
		} >>"$out/$label-starts.txt"
		echo "$total" >>"$out/$label-totals.txt"
		adb_cmd logcat -d -v epoch -s NativePoc:I Floor:I ActivityTaskManager:I >"$out/$label-log-$flat.txt" || true
		capture_frame "$s" "$out/$label-gfx-$flat.txt"
		echo "$label block=$block round=$round flat=$flat $total load=$load" >&2
		done
		flat=$((flat + 1))
	done
done
samples="$flat"

{
	printf '%-12s %4s %8s %8s %10s %10s %10s %14s\n' series n total_p50 total_p90 present_p50 ui_p50 draw_p50 guest_load_p50
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
		guest_p50="$(median_values <"$out/$label-loads.txt")"
		printf '%-12s %4s %8s %8s %10s %10s %10s %14s\n' "$label" "$samples" "$p50" "$p90" "$present_p50" "$ui_p50" "$draw_p50" "$guest_p50"
		phase_p50() {
			local pattern="$1"
			local -a values=()
			local round value
			for ((round = 0; round < samples; round++)); do
				value="$(awk -v pattern="$pattern" '
					index($0, "startup mark=activity ") { hit = "" }
					$0 ~ pattern && hit == "" {
						value = $0
						sub(/^.*since-process-ms=/, "", value)
						sub(/[^0-9].*$/, "", value)
						if (value ~ /^[0-9]+$/)
							hit = value
					}
					END {
						if (hit != "")
							print hit
					}
				' "$out/$label-log-$round.txt" || true)"
				[[ -n "$value" ]] && values+=("$value")
			done
			if [[ "${#values[@]}" -eq 0 ]]; then
				echo "-"
				return
			fi
			printf '%s\n' "${values[@]}" | percentile 0.50
		}
		runtime_p50="$(phase_p50 'phase=runtime ')"
		render_p50="$(phase_p50 'phase=render ')"
		attach_p50="$(phase_p50 'phase=attach ')"
		bind_p50="$(phase_p50 'phase=bind ')"
		draw_phase_p50="$(phase_p50 'phase=draw ')"
		list_p50="$(phase_p50 'list-first=')"
		# java-class, before-runtime, and after-runtime are logged before
		# startup mark=activity, so the first-after-mark rule never sees them.
		# Keep the value that immediately preceded that mark.
		prior_p50() {
			local pattern="$1"
			local -a values=()
			local round value
			for ((round = 0; round < samples; round++)); do
				value="$(awk -v pattern="$pattern" '
					$0 ~ pattern {
						value = $0
						sub(/^.*since-process-ms=/, "", value)
						sub(/[^0-9].*$/, "", value)
						if (value ~ /^[0-9]+$/)
							pending = value
					}
					index($0, "startup mark=activity ") {
						if (pending != "")
							hit = pending
						pending = ""
					}
					END {
						if (hit != "")
							print hit
					}
				' "$out/$label-log-$round.txt" || true)"
				[[ -n "$value" ]] && values+=("$value")
			done
			if [[ "${#values[@]}" -eq 0 ]]; then
				echo "-"
				return
			fi
			printf '%s\n' "${values[@]}" | percentile 0.50
		}
		{
			echo "series=$label n=$samples guest_load_p50=$guest_p50"
			echo "raw=$(tr '\n' ' ' <"$out/$label-totals.txt" | sed 's/ $//')"
			echo "p50 $p50 p90 $p90"
			echo "present_p50 $present_p50 ui_p50 $ui_p50 draw_p50 $draw_p50"
			echo "ui is DrawStart - PerformTraversalsStart on the first framestats row, milliseconds"
			echo "draw is SwapBuffers - IssueDrawCommandsStart on that row, milliseconds"
			echo "startup_since_process_p50 runtime=$runtime_p50 render=$render_p50 attach=$attach_p50 bind=$bind_p50 draw=$draw_phase_p50 list=$list_p50"
			before_p50="$(prior_p50 'phase=before-runtime ')"
			managed_p50="$(phase_p50 'phase=managed ')"
			after_p50="$(prior_p50 'phase=after-runtime ')"
			oncreate_p50="$(phase_p50 'phase=onCreate ')"
			layout_p50="$(phase_p50 'phase=layout ')"
			idle_p50="$(phase_p50 'phase=idle ')"
			flush_p50="$(phase_p50 'phase=flush ')"
			java_attach_p50="$(prior_p50 'phase=java-attach ')"
			java_p50="$(prior_p50 'phase=java-oncreate ')"
			java_end_p50="$(prior_p50 'phase=java-oncreate-end ')"
			jni_p50="$(prior_p50 'phase=jni-activity ')"
			enter_p50="$(prior_p50 'phase=activity-enter ')"
			base_p50="$(prior_p50 'phase=activity-base ')"
			direction_p50="$(phase_p50 'phase=direction ')"
			warm_p50="$(phase_p50 'phase=warm ')"
			hold_p50="$(phase_p50 'phase=hold ')"
			echo "startup_breakdown_p50 before=$before_p50 managed=$managed_p50 after=$after_p50 oncreate=$oncreate_p50 layout=$layout_p50 idle=$idle_p50 flush=$flush_p50"
			echo "startup_split_p50 java_attach=$java_attach_p50 java=$java_p50 java_end=$java_end_p50 jni=$jni_p50 enter=$enter_p50 base=$base_p50 direction=$direction_p50 warm=$warm_p50 hold=$hold_p50"
			attach_end_p50="$(prior_p50 'phase=java-attach-end ')"
			class_p50="$(prior_p50 'phase=java-class ')"
			so_reloc_p50="$(prior_p50 'phase=so-reloc ')"
			so_ctors_p50="$(prior_p50 'phase=so-ctors ')"
			echo "startup_load_p50 attach_end=$attach_end_p50 class=$class_p50 before=$before_p50 so_reloc=$so_reloc_p50 so_ctors=$so_ctors_p50 after=$after_p50 java=$java_p50 java_end=$java_end_p50"
			jcw_enter_p50="$(prior_p50 'phase=jcw-enter ')"
			jcw_register_p50="$(prior_p50 'phase=jcw-register ')"
			jcw_registered_p50="$(prior_p50 'phase=jcw-registered ')"
			jcw_super_p50="$(prior_p50 'phase=jcw-super ')"
			jcw_peer_p50="$(prior_p50 'phase=jcw-peer ')"
			thin_class_p50="$(prior_p50 'phase=thin-class ')"
			thin_jni_p50="$(prior_p50 'phase=thin-jni ')"
			thin_ctor_p50="$(prior_p50 'phase=thin-ctor ')"
			thin_oncreate_p50="$(prior_p50 'phase=thin-oncreate ')"
			jcw_natives="$(awk '/jcw-natives count=/ { sub(/^.*count=/, ""); print; exit }' "$out/$label-log-0.txt" 2>/dev/null || true)"
			echo "startup_jcw_p50 java_end=$java_end_p50 jcw_enter=$jcw_enter_p50 jcw_register=$jcw_register_p50 jcw_registered=$jcw_registered_p50 jcw_super=$jcw_super_p50 jni=$jni_p50 jcw_peer=$jcw_peer_p50 natives=${jcw_natives:-"-"}"
			echo "startup_thin_p50 java_end=$java_end_p50 thin_class=$thin_class_p50 thin_jni=$thin_jni_p50 thin_ctor=$thin_ctor_p50 thin_oncreate=$thin_oncreate_p50"
			echo "startup clock is SystemClock.elapsedRealtime. process t is Process.startElapsedRealtime. list is the first list-first= line."
			echo "method=am start -W -S LaunchState=COLD warmup=1 discarded per series interleaved=1 percentile=Hyndman-Fan-7"
			echo "load=$(tr '\n' ' ' <"$out/round-load.txt" | sed 's/ $//')"
			echo "guest rounds are in guest.txt. A guest_load at or above 2.0 is a warning. The sample stays."
			if [[ -f "$out/guest.txt" ]]; then
				cat "$out/guest.txt"
			fi
		} >"$out/$label-summary.txt"
		echo "startup_since_process_p50 $label runtime=$runtime_p50 render=$render_p50 attach=$attach_p50 bind=$bind_p50 draw=$draw_phase_p50 list=$list_p50"
		echo "startup_breakdown_p50 $label before=$before_p50 managed=$managed_p50 after=$after_p50 oncreate=$oncreate_p50 layout=$layout_p50 idle=$idle_p50 flush=$flush_p50"
		echo "startup_split_p50 $label java_attach=$java_attach_p50 java=$java_p50 java_end=$java_end_p50 jni=$jni_p50 enter=$enter_p50 base=$base_p50 direction=$direction_p50 warm=$warm_p50 hold=$hold_p50"
		echo "startup_load_p50 $label attach_end=$attach_end_p50 class=$class_p50 before=$before_p50 so_reloc=$so_reloc_p50 so_ctors=$so_ctors_p50 after=$after_p50 java=$java_p50 java_end=$java_end_p50"
		echo "startup_jcw_p50 $label java_end=$java_end_p50 jcw_enter=$jcw_enter_p50 jcw_register=$jcw_register_p50 jcw_registered=$jcw_registered_p50 jcw_super=$jcw_super_p50 jni=$jni_p50 jcw_peer=$jcw_peer_p50 natives=${jcw_natives:-"-"}"
		echo "startup_thin_p50 $label java_end=$java_end_p50 thin_class=$thin_class_p50 thin_jni=$thin_jni_p50 thin_ctor=$thin_ctor_p50 thin_oncreate=$thin_oncreate_p50"
	done
	feed_seen=0
	floor_seen_delta=0
	for ((s = 0; s < ${#labels[@]}; s++)); do
		if [[ "${labels[$s]}" == "feed" ]]; then
			feed_seen=1
		fi
		if [[ "${labels[$s]}" == "floor" ]]; then
			floor_seen_delta=1
		fi
	done
	if [[ "$feed_seen" -eq 1 && "$floor_seen_delta" -eq 1 ]]; then
		echo "block_delta feed p50 minus floor p50. median is Hyndman-Fan 7 of the block deltas. low and high are the bootstrap 2.5 and 97.5 percentiles, B=1000, Park-Miller seed 8946."
		block_deltas=()
		for ((block = 0; block < blocks; block++)); do
			start=$((block * block_n + 1))
			end=$((start + block_n - 1))
			feed_block="$(sed -n "${start},${end}p" "$out/feed-totals.txt" | percentile 0.50)"
			floor_block="$(sed -n "${start},${end}p" "$out/floor-totals.txt" | percentile 0.50)"
			delta="$(awk -v a="$feed_block" -v b="$floor_block" 'BEGIN { printf "%.1f", a - b }')"
			echo "block $block feed_p50=$feed_block floor_p50=$floor_block delta=$delta"
			block_deltas+=("$delta")
		done
		echo "block_delta $(printf '%s\n' "${block_deltas[@]}" | bootstrap_ci) B=1000 seed=8946"
	else
		echo "block_delta=- no feed and floor"
	fi
	if [[ "$floor_seen_delta" -eq 1 ]]; then
		for ((s = 0; s < ${#labels[@]}; s++)); do
			series="${labels[$s]}"
			if [[ "$series" == "floor" || "$series" == "feed" ]]; then
				continue
			fi
			echo "block_delta $series p50 minus floor p50. median is Hyndman-Fan 7 of the block deltas. low and high are the bootstrap 2.5 and 97.5 percentiles, B=1000, Park-Miller seed 8946."
			series_deltas=()
			for ((block = 0; block < blocks; block++)); do
				start=$((block * block_n + 1))
				end=$((start + block_n - 1))
				series_block="$(sed -n "${start},${end}p" "$out/$series-totals.txt" | percentile 0.50)"
				floor_block="$(sed -n "${start},${end}p" "$out/floor-totals.txt" | percentile 0.50)"
				delta="$(awk -v a="$series_block" -v b="$floor_block" 'BEGIN { printf "%.1f", a - b }')"
				echo "block $block ${series}_p50=$series_block floor_p50=$floor_block delta=$delta"
				series_deltas+=("$delta")
			done
			echo "block_delta $series $(printf '%s\n' "${series_deltas[@]}" | bootstrap_ci) B=1000 seed=8946"
		done
	fi
	floor_seen=0
	for ((s = 0; s < ${#labels[@]}; s++)); do
		if [[ "${labels[$s]}" == "floor" ]]; then
			floor_seen=1
		fi
	done
	if [[ "$floor_seen" -eq 1 ]]; then
		echo "contended floor total >1.5x its median; round total load; still included in p50 and p90"
		contended_rounds "$out/floor-totals.txt" "$out/round-load.txt"
	else
		echo "contended=- no floor series"
	fi
} | tee "$out/summary.txt"
