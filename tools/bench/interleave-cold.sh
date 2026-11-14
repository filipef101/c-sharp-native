#!/usr/bin/env bash
# Same-method interleaved cold start of two already-built apks.
# Install each once. No cmd package compile -m speed. No per-sample reinstall.
# Guest load must be below 2.0 before the warmup and before every sample.
# Pairs are A then B. One discarded warmup of each. n samples of each.
#
# Fully drawn is taken from this sample only. logcat is cleared after the
# guest gate and a mark is written before am start. Lines before that mark,
# including the other package, are ignored.
#
#   tools/bench/interleave-cold.sh --self-test
#   tools/bench/interleave-cold.sh --out DIR --n 20 \
#       --left-apk A.apk --left-pkg PKG --left-name NAME \
#       --right-apk B.apk --right-pkg PKG --right-name NAME
set -euo pipefail

fully_drawn_after_mark() {
	local pkg="$1"
	local mark="$2"
	awk -v pkg="$pkg" -v mark="$mark" '
		{
			at = index($0, mark)
			if (at > 0 && substr($0, at + length(mark), 1) !~ /[0-9]/) {
				seen = 1
				next
			}
		}
		seen && found == "" && index($0, "Fully drawn " pkg "/") { found = $0 }
		END { print found }
	'
}

fully_drawn_ms() {
	awk '
		function digits(s,    i, c, out) {
			out = ""
			for (i = 1; i <= length(s); i++) {
				c = substr(s, i, 1)
				if (c ~ /[0-9]/)
					out = out c
				else
					break
			}
			return out
		}
		{
			n = index($0, "+")
			if (n == 0)
				next
			rest = substr($0, n + 1)
			num = digits(rest)
			if (num == "")
				next
			after = substr(rest, length(num) + 1)
			if (substr(after, 1, 2) == "ms") {
				print num + 0
				exit
			}
			if (substr(after, 1, 1) == "s") {
				ms = digits(substr(after, 2))
				if (ms != "" && substr(after, 2 + length(ms), 2) == "ms") {
					print (num + 0) * 1000 + (ms + 0)
					exit
				}
			}
		}
	'
}

if [[ "${1:-}" == "--self-test" ]]; then
	fail=0
	fixture=$'I/ActivityManager(1): Fully drawn com.scratch.nativepoc/crc.MainActivity: +879ms\nI/NativePocSample(1): nativepoc-sample counter 1\nI/ActivityManager(1): Fully drawn com.scratch.nativepoc.showcase/crc.MainActivity: +584ms\nI/ActivityManager(1): Fully drawn com.scratch.nativepoc/crc.MainActivity: +1s40ms\nI/NativePocSample(1): nativepoc-sample counter 10\nI/ActivityManager(1): Fully drawn com.scratch.nativepoc/crc.MainActivity: +700ms'
	counter_line="$(printf '%s\n' "$fixture" | fully_drawn_after_mark com.scratch.nativepoc "nativepoc-sample counter 1")"
	counter_ms="$(printf '%s\n' "$counter_line" | fully_drawn_ms)"
	showcase_line="$(printf '%s\n' "$fixture" | fully_drawn_after_mark com.scratch.nativepoc.showcase "nativepoc-sample counter 1")"
	showcase_ms="$(printf '%s\n' "$showcase_line" | fully_drawn_ms)"
	tenth_line="$(printf '%s\n' "$fixture" | fully_drawn_after_mark com.scratch.nativepoc "nativepoc-sample counter 10")"
	tenth_ms="$(printf '%s\n' "$tenth_line" | fully_drawn_ms)"
	[[ "$counter_ms" == "1040" ]] || { echo "counter fully drawn $counter_ms" >&2; fail=1; }
	[[ "$showcase_ms" == "584" ]] || { echo "showcase fully drawn $showcase_ms" >&2; fail=1; }
	[[ "$tenth_ms" == "700" ]] || { echo "tenth fully drawn $tenth_ms" >&2; fail=1; }
	plain="$(printf '%s\n' 'Fully drawn com.scratch.nativepoc/crc.MainActivity: +640ms' | fully_drawn_ms)"
	[[ "$plain" == "640" ]] || { echo "plain fully drawn $plain" >&2; fail=1; }
	stale=$'I/ActivityManager(1): Fully drawn com.scratch.nativepoc/crc.MainActivity: +879ms\nI/NativePocSample(1): nativepoc-sample counter 2'
	stale_line="$(printf '%s\n' "$stale" | fully_drawn_after_mark com.scratch.nativepoc "nativepoc-sample counter 2")"
	[[ -z "$stale_line" ]] || { echo "stale fully drawn leaked: $stale_line" >&2; fail=1; }
	empty_line="$(printf '' | fully_drawn_after_mark com.scratch.nativepoc "nativepoc-sample counter 0")"
	[[ -z "$empty_line" ]] || { echo "empty fully drawn leaked: $empty_line" >&2; fail=1; }
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "interleave-cold self-test ok"
	exit 0
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
device="emulator-5554"
out=""
n=20
timeout_s=180
left_apk=""
right_apk=""
left_pkg="com.scratch.nativepoc"
right_pkg="com.scratch.nativepoc.showcase"
left_name="counter"
right_name="showcase"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--device) device="$2"; shift 2 ;;
		--out) out="$2"; shift 2 ;;
		--n) n="$2"; shift 2 ;;
		--timeout) timeout_s="$2"; shift 2 ;;
		--left-apk) left_apk="$2"; shift 2 ;;
		--right-apk) right_apk="$2"; shift 2 ;;
		--left-pkg) left_pkg="$2"; shift 2 ;;
		--right-pkg) right_pkg="$2"; shift 2 ;;
		--left-name) left_name="$2"; shift 2 ;;
		--right-name) right_name="$2"; shift 2 ;;
		*) echo "unknown arg $1" >&2; exit 2 ;;
	esac
done

if [[ -z "$out" || -z "$left_apk" || -z "$right_apk" ]]; then
	echo "usage: tools/bench/interleave-cold.sh --out DIR --left-apk A --right-apk B" >&2
	exit 2
fi

adb_bin="${ANDROID_HOME:-}/platform-tools/adb"
if [[ ! -x "$adb_bin" ]]; then
	adb_bin="$(command -v adb)"
fi

adb_cmd() {
	"$adb_bin" -s "$device" "$@" </dev/null
}

source "$root/tools/bench/guest-env.sh"

mkdir -p "$out"

resolve_component() {
	local pkg="$1"
	local resolved component
	resolved="$(adb_cmd shell cmd package resolve-activity --brief "$pkg" | tr -d '\r')"
	component="$(printf '%s\n' "$resolved" | awk '/\// && $0 !~ / / { line = $0 } END { print line }')"
	if [[ -z "$component" ]]; then
		echo "Could not resolve $pkg" >&2
		printf '%s\n' "$resolved" >&2
		exit 1
	fi
	printf '%s\n' "$component"
}

install_one() {
	local apk="$1"
	local install_out
	install_out="$(adb_cmd install -r "$apk" 2>&1)" || true
	printf '%s\n' "$install_out"
	if ! grep -q "Success" <<<"$install_out"; then
		echo "Install failed: $apk" >&2
		exit 1
	fi
}

dex_status() {
	local pkg="$1"
	adb_cmd shell dumpsys package "$pkg" | tr -d '\r' | awk '
		/Dexopt state:/ { on = 1 }
		on && /status=/ { print; if (++c == 4) exit }
		on && /^$/ { if (c > 0) exit }
	' || true
}

flag_load() {
	awk -v v="$1" 'BEGIN { exit !((v + 0) > 2.0) }'
}

one_start() {
	local component="$1"
	adb_cmd shell am start -W -S -n "$component"
}

accept_guest() {
	local line
	if ! line="$(bash "$root/tools/bench/wait-guest.sh" --device "$device" --below 2.0 --timeout "$timeout_s")"; then
		printf '%s\n' "$line" >&2
		echo "guest load stayed at or above 2.0" >&2
		exit 1
	fi
	printf '%s\n' "$line"
}

{
	echo "method=install-once no-speed-compile guest-gate-below-2.0 interleaved-pairs am-start-W-S LaunchState=COLD warmup=1-each-discarded n=$n"
	echo "metric=TotalTime fully-drawn=logcat-after-per-sample-mark"
	echo "boot:"
	adb_cmd shell uptime || true
	adb_cmd shell cat /proc/uptime || true
	echo "host:"
	uptime || true
} >"$out/method.txt"

install_one "$left_apk" >"$out/install-left.txt"
install_one "$right_apk" >"$out/install-right.txt"
left_component="$(resolve_component "$left_pkg")"
right_component="$(resolve_component "$right_pkg")"
{
	echo "left=$left_name apk=$left_apk component=$left_component"
	echo "right=$right_name apk=$right_apk component=$right_component"
	echo "dexopt-after-install left:"
	dex_status "$left_pkg"
	echo "dexopt-after-install right:"
	dex_status "$right_pkg"
} | tee -a "$out/method.txt"

raw="$out/starts.txt"
: >"$raw"
: >"$out/guest.txt"

declare -a left_totals=()
declare -a right_totals=()
declare -a left_flags=()
declare -a right_flags=()
declare -a left_posts=()
declare -a right_posts=()
declare -a left_drawn=()
declare -a right_drawn=()

read_fully_drawn() {
	local pkg="$1"
	local mark="$2"
	local drawn="" buf attempt
	for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
		buf="$(adb_cmd shell logcat -d -v brief | tr -d '\r' || true)"
		drawn="$(printf '%s\n' "$buf" | fully_drawn_after_mark "$pkg" "$mark")"
		if [[ -n "$drawn" ]]; then
			printf '%s\n' "$drawn"
			return 0
		fi
		sleep 0.1
	done
	return 0
}

record_sample() {
	local name="$1"
	local index="$2"
	local component="$3"
	local pkg="$4"
	local gate text state total guest_line first post flag drawn drawn_ms mark
	gate="$(accept_guest)"
	mark="nativepoc-sample ${name} ${index}"
	adb_cmd logcat -c >/dev/null || true
	adb_cmd shell log -t NativePocSample "$mark" >/dev/null
	text="$(one_start "$component")"
	state="$(printf '%s\n' "$text" | awk '/^LaunchState: / { print $2 }')"
	total="$(printf '%s\n' "$text" | awk '/^TotalTime: / { print $2 }')"
	if [[ "$state" != "COLD" || -z "$total" ]]; then
		echo "$text" >&2
		echo "Sample $name $index was not a COLD TotalTime" >&2
		exit 1
	fi
	drawn="$(read_fully_drawn "$pkg" "$mark")"
	drawn_ms="$(printf '%s\n' "$drawn" | fully_drawn_ms)"
	guest_line="$(capture_guest "$out/${name}-guest-${index}.txt")"
	printf '%s\n' "$guest_line" >>"$out/guest.txt"
	first="${guest_line%%$'\n'*}"
	post="${first#guest_load=}"
	post="${post%% *}"
	flag=0
	if [[ -n "$post" ]] && flag_load "$post"; then
		flag=1
	fi
	{
		echo "SAMPLE $name $index"
		echo "mark=$mark"
		printf '%s\n' "$gate"
		printf '%s\n' "$guest_line"
		echo "post_guest_load=${post:-none} flagged=$flag"
		echo "fully_drawn=${drawn:-none}"
		echo "fully_drawn_ms=${drawn_ms:-none}"
		printf '%s\n' "$text"
	} >>"$raw"
	echo "$name $index $total drawn=${drawn_ms:-none} gate=$gate post=$post flagged=$flag" >&2
	if [[ "$name" == "$left_name" ]]; then
		left_totals+=("$total")
		left_flags+=("$flag")
		left_posts+=("$post")
		left_drawn+=("${drawn_ms:-}")
	else
		right_totals+=("$total")
		right_flags+=("$flag")
		right_posts+=("$post")
		right_drawn+=("${drawn_ms:-}")
	fi
}

echo "WARMUP left $(accept_guest)" | tee -a "$raw"
warm="$(one_start "$left_component")"
printf '%s\n' "$warm" >>"$raw"
grep -q '^LaunchState: COLD$' <<<"$warm"
echo "WARMUP right $(accept_guest)" | tee -a "$raw"
warm="$(one_start "$right_component")"
printf '%s\n' "$warm" >>"$raw"
grep -q '^LaunchState: COLD$' <<<"$warm"
echo "WARMUP discarded" >>"$raw"

for ((i = 0; i < n; i++)); do
	record_sample "$left_name" "$i" "$left_component" "$left_pkg"
	record_sample "$right_name" "$i" "$right_component" "$right_pkg"
done

adb_cmd logcat -d -v epoch >"$out/logcat.txt" || true
{
	echo "dexopt-after-samples left:"
	dex_status "$left_pkg"
	echo "dexopt-after-samples right:"
	dex_status "$right_pkg"
} >>"$out/method.txt"

bootstrap_drawn() {
	local -a values=("$@")
	local kept=()
	local item
	for item in "${values[@]}"; do
		if [[ -n "$item" ]]; then
			kept+=("$item")
		fi
	done
	if [[ ${#kept[@]} -eq 0 ]]; then
		echo "p50=na n=0"
		return 0
	fi
	printf '%s\n' "${kept[@]}" | bash "$root/tools/bench/wait-guest.sh" --bootstrap
}

summarize() {
	local name="$1"
	shift
	local -a totals=("$@")
	local -a flags=()
	local -a posts=()
	local -a drawn=()
	if [[ "$name" == "$left_name" ]]; then
		flags=("${left_flags[@]}")
		posts=("${left_posts[@]}")
		drawn=("${left_drawn[@]}")
	else
		flags=("${right_flags[@]}")
		posts=("${right_posts[@]}")
		drawn=("${right_drawn[@]}")
	fi
	local kept=()
	local flagged_n=0
	local i
	for ((i = 0; i < ${#totals[@]}; i++)); do
		if [[ "${flags[$i]}" == "1" ]]; then
			flagged_n=$((flagged_n + 1))
		else
			kept+=("${totals[$i]}")
		fi
	done
	local ci ci_kept drawn_ci
	ci="$(printf '%s\n' "${totals[@]}" | bash "$root/tools/bench/wait-guest.sh" --bootstrap)"
	if [[ ${#kept[@]} -gt 0 ]]; then
		ci_kept="$(printf '%s\n' "${kept[@]}" | bash "$root/tools/bench/wait-guest.sh" --bootstrap)"
	else
		ci_kept="p50=na n=0"
	fi
	drawn_ci="$(bootstrap_drawn "${drawn[@]}")"
	{
		echo "series=$name n=${#totals[@]} flagged=$flagged_n unflagged=${#kept[@]} threshold=2.0"
		echo "totals=${totals[*]}"
		echo "fully_drawn_ms=${drawn[*]}"
		echo "post=${posts[*]}"
		echo "flags=${flags[*]}"
		echo "p50_all $ci"
		echo "p50_unflagged $ci_kept"
		echo "fully_drawn $drawn_ci"
	}
}

{
	echo "method=install-once no-speed-compile guest-gate<2.0 before each sample post-sample load flagged when > 2.0 pairs A then B one boot fully-drawn from logcat after a per-sample mark"
	echo "A=$left_name package=$left_pkg component=$left_component"
	echo "B=$right_name package=$right_pkg component=$right_component"
	summarize "$left_name" "${left_totals[@]}"
	summarize "$right_name" "${right_totals[@]}"
} | tee "$out/summary.txt"

echo "interleave done" >&2
