#!/usr/bin/env bash
# Cold start the way the Mac arm64 series was taken.
# One discarded warmup, then n samples of `am start -W -S`.
# Every sample must report LaunchState COLD.
# p50 and p90 are Hyndman-Fan type 7:
#   h = 1 + (n - 1) * p
#   n=20 p50 is the mean of the 10th and 11th sorted samples
#   n=20 p90 is the 18th plus 0.1 times the gap to the 19th
# Logcat startup marks are parsed after the samples.
#
#   tools/bench/cold-start.sh --self-test
#   tools/bench/cold-start.sh --device emulator-5554 --name packing --out /tmp/bench -- --es nativepoc_bench packing1000
#   tools/bench/cold-start.sh --floor --name floor --out /tmp/bench
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/guest-env.sh"

root="$(cd "$(dirname "$0")/../.." && pwd)"
device=""
samples=20
name="app"
out=""
component=""
floor=0
apk=""
self_test=0
am_extra=()

usage() {
	cat <<'EOF'
tools/bench/cold-start.sh [options] [-- am-start extras]

  --device <serial>   adb serial. Default: the only online device.
  --n <count>         samples after the discarded warmup. Default: 20.
  --name <label>      series name used in the output files.
  --out <dir>         where the raw start text, logcat, and summary go.
  --component <name>  activity component. Default: resolve the launcher.
  --apk <path>        adb install -r before the series.
  --floor             build tools/bench/floor and measure com.scratch.floor.
  --self-test         check Hyndman-Fan type 7 against the Mac n=20 series.
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--device) device="${2:?}"; shift 2 ;;
		--n) samples="${2:?}"; shift 2 ;;
		--name) name="${2:?}"; shift 2 ;;
		--out) out="${2:?}"; shift 2 ;;
		--component) component="${2:?}"; shift 2 ;;
		--apk) apk="${2:?}"; shift 2 ;;
		--floor) floor=1; shift ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		--) shift; am_extra+=("$@"); break ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

if ! [[ "$samples" =~ ^[1-9][0-9]*$ ]]; then
	echo "--n must be a positive integer" >&2
	exit 2
fi

percentile() {
	# args: p sample...
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

sorted_line() {
	awk '
		{ v[NR] = $1 }
		END {
			n = NR
			for (i = 2; i <= n; i++) {
				key = v[i]
				j = i - 1
				while (j >= 1 && v[j] > key) {
					v[j + 1] = v[j]
					j--
				}
				v[j + 1] = key
			}
			for (i = 1; i <= n; i++) {
				printf "%s%s", v[i], (i == n ? "" : " ")
			}
			printf "\n"
		}
	'
}

if [[ "$self_test" -eq 1 ]]; then
	counter=(800 447 683 575 736 497 419 1059 871 816 842 444 485 463 444 449 474 400 462 406)
	packing=(447 364 408 362 382 449 369 361 594 918 486 470 428 577 544 457 457 469 514 846)
	c50="$(printf '%s\n' "${counter[@]}" | percentile 0.50)"
	c90="$(printf '%s\n' "${counter[@]}" | percentile 0.90)"
	p50="$(printf '%s\n' "${packing[@]}" | percentile 0.50)"
	p90="$(printf '%s\n' "${packing[@]}" | percentile 0.90)"
	fail=0
	[[ "$c50" == "479.5" ]] || { echo "counter p50 $c50" >&2; fail=1; }
	[[ "$c90" == "844.9" ]] || { echo "counter p90 $c90" >&2; fail=1; }
	[[ "$p50" == "457.0" ]] || { echo "packing p50 $p50" >&2; fail=1; }
	[[ "$p90" == "619.2" ]] || { echo "packing p90 $p90" >&2; fail=1; }
	bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/guest-env.sh" --self-test >/dev/null
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "self-test p50/p90 Hyndman-Fan 7 matches the Mac n=20 series"
	exit 0
fi

if [[ -n "${ANDROID_HOME:-}" && -x "$ANDROID_HOME/platform-tools/adb" ]]; then
	adb_bin="$ANDROID_HOME/platform-tools/adb"
else
	adb_bin="$(command -v adb)"
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

if [[ "$floor" -eq 1 ]]; then
	bash "$root/tools/bench/build-floor.sh"
	apk="$root/tools/bench/floor/build/floor.apk"
	component="com.scratch.floor/.FloorActivity"
	adb_cmd uninstall com.scratch.floor >/dev/null 2>&1 || true
fi

if [[ -n "$apk" ]]; then
	install_out="$(adb_cmd install -r "$apk" 2>&1)" || true
	if ! grep -q "Success" <<<"$install_out"; then
		echo "$install_out" >&2
		echo "Install failed" >&2
		exit 1
	fi
fi

if [[ -z "$component" ]]; then
	resolved="$(adb_cmd shell cmd package resolve-activity --brief com.scratch.nativepoc)"
	component="$(printf '%s\n' "$resolved" | awk '/\// && $0 !~ / / { line = $0 } END { print line }')"
	if [[ -z "$component" ]]; then
		echo "Could not resolve the launcher activity" >&2
		echo "$resolved" >&2
		exit 1
	fi
fi

if [[ -z "$out" ]]; then
	out="$root/tools/bench/out/$name"
fi
mkdir -p "$out"
package_background "${component%%/*}" "$out/package.txt"

one_start() {
	adb_cmd shell am start -W -S -n "$component" "${am_extra[@]}"
}

echo "series=$name device=$device component=$component n=$samples" >&2
uptime >&2 || true
adb_cmd logcat -c >/dev/null
warmup="$(one_start)"
if ! grep -q '^LaunchState: COLD$' <<<"$warmup"; then
	echo "$warmup" >&2
	echo "Warmup was not COLD" >&2
	exit 1
fi
adb_cmd logcat -c >/dev/null

raw="$out/$name-starts.txt"
{
	echo "component=$component"
	echo "extra=${am_extra[*]:-}"
	echo "WARMUP discarded"
	printf '%s\n' "$warmup"
} >"$raw"

totals=()
for ((i = 0; i < samples; i++)); do
	text="$(one_start)"
	state="$(printf '%s\n' "$text" | awk '/^LaunchState: / { print $2 }')"
	total="$(printf '%s\n' "$text" | awk '/^TotalTime: / { print $2 }')"
	if [[ "$state" != "COLD" || -z "$total" ]]; then
		echo "$text" >&2
		echo "Sample $i was not a COLD TotalTime" >&2
		exit 1
	fi
	totals+=("$total")
	guest_line="$(capture_guest "$out/$name-guest-$i.txt")"
	printf '%s\n' "$guest_line" >>"$out/guest.txt"
	{
		echo "SAMPLE $i"
		printf '%s\n' "$guest_line"
		printf '%s\n' "$text"
	} >>"$raw"
	echo "$name $i $total $guest_line" >&2
done

adb_cmd logcat -d -v epoch >"$out/$name-logcat.txt"
ordered="$(printf '%s\n' "${totals[@]}" | sorted_line)"
p50="$(printf '%s\n' "${totals[@]}" | percentile 0.50)"
p90="$(printf '%s\n' "${totals[@]}" | percentile 0.90)"
{
	echo "$name n=$samples sorted=$ordered"
	echo "p50 $p50 p90 $p90"
	echo "method=am start -W -S LaunchState=COLD warmup=1 discarded percentile=Hyndman-Fan-7"
	echo "guest samples are in guest.txt. A guest_load at or above 2.0 is a warning. The sample stays."
	if [[ -f "$out/guest.txt" ]]; then
		cat "$out/guest.txt"
	fi
} | tee "$out/$name-summary.txt"

awk -v expected="$samples" -v dest="$out/$name-marks.txt" '
function grab(text, key,    at, rest, i, ch, value) {
	at = index(text, key)
	if (at == 0) return ""
	rest = substr(text, at + length(key))
	value = ""
	for (i = 1; i <= length(rest); i++) {
		ch = substr(rest, i, 1)
		if (ch ~ /[0-9A-Za-z-]/) value = value ch
		else break
	}
	return value
}
function clock(text,    sec, ms, at, rest, i, ch, num) {
	sec = 0
	ms = 0
	at = index(text, "+")
	if (at == 0) return -1
	rest = substr(text, at + 1)
	num = ""
	for (i = 1; i <= length(rest); i++) {
		ch = substr(rest, i, 1)
		if (ch ~ /[0-9]/) num = num ch
		else if (ch == "s") { sec = num + 0; num = "" }
		else if (ch == "m") { ms = num + 0; break }
		else break
	}
	return sec * 1000 + ms
}
function pct(n, p,    h, lo, frac, hi, i, key, j) {
	for (i = 2; i <= n; i++) {
		key = buf[i]
		j = i - 1
		while (j >= 1 && buf[j] > key) {
			buf[j + 1] = buf[j]
			j--
		}
		buf[j + 1] = key
	}
	h = 1 + (n - 1) * p
	lo = int(h)
	frac = h - lo
	hi = (frac > 0) ? lo + 1 : lo
	if (lo < 1) lo = 1
	if (hi > n) hi = n
	if (lo > n) lo = n
	return buf[lo] + frac * (buf[hi] - buf[lo])
}
function note(key, value) {
	cur[key] = value + 0
	have = 1
}
function flush_sample(    k) {
	n++
	for (k in cur) slot[n, k] = cur[k]
	delete cur
	have = 0
}
{
	if (index($0, "startup mark=") > 0) {
		note(grab($0, "mark="), grab($0, "since-process-ms="))
		next
	}
	if (index($0, "startup phase=") > 0) {
		note("phase-" grab($0, "phase="), grab($0, "ms="))
		next
	}
	if (index($0, "startup window rows=") > 0) {
		note("rows", grab($0, "rows="))
		note("built", grab($0, "built="))
		note("views", grab($0, "views="))
		next
	}
	if (index($0, "Displayed ") > 0 && have) {
		note("displayed", clock($0))
		flush_sample()
		next
	}
	if (index($0, "Fully drawn ") > 0 && n > 0) {
		slot[n, "fully-drawn"] = clock($0)
		next
	}
	if (index($0, "IsDynamicCodeSupported=False") > 0) dynamic++
}
END {
	keys[1] = "managed-main"
	keys[2] = "activity"
	keys[3] = "first-render"
	keys[4] = "first-present"
	keys[5] = "activity-return"
	keys[6] = "fully-drawn-mark"
	keys[7] = "displayed"
	keys[8] = "fully-drawn"
	keys[9] = "phase-eval"
	keys[10] = "phase-mount"
	keys[11] = "phase-nav"
	keys[12] = "report-fully-drawn"
	out = "mark-samples " n "\n"
	for (ki = 1; ki <= 12; ki++) {
		key = keys[ki]
		count = 0
		for (i = 1; i <= n; i++) {
			if ((i SUBSEP key) in slot) {
				count++
				buf[count] = slot[i, key]
			}
		}
		if (count == 0) continue
		if (count < expected) {
			out = out key " n=" count " incomplete\n"
			continue
		}
		p50 = pct(count, 0.50)
		p90 = pct(count, 0.90)
		joined = ""
		for (i = 1; i <= count; i++) joined = joined (i == 1 ? "" : " ") buf[i]
		out = out sprintf("%s n=%d sorted=%s p50 %.1f p90 %.1f\n", key, count, joined, p50, p90)
	}
	gapn = 0
	for (i = 1; i <= n; i++) {
		if ((i SUBSEP "first-render") in slot && (i SUBSEP "displayed") in slot) {
			gapn++
			buf[gapn] = slot[i, "displayed"] - slot[i, "first-render"]
		}
	}
	if (gapn == n && n > 0) {
		p50 = pct(gapn, 0.50)
		p90 = pct(gapn, 0.90)
		joined = ""
		for (i = 1; i <= gapn; i++) joined = joined (i == 1 ? "" : " ") buf[i]
		out = out sprintf("first-render-to-displayed n=%d sorted=%s p50 %.1f p90 %.1f\n", gapn, joined, p50, p90)
	}
	if (n > 0 && (1 SUBSEP "rows") in slot) {
		out = out "window n=" n " rows=" slot[1, "rows"] " built=" slot[1, "built"] " views=" slot[1, "views"] "\n"
	}
	out = out "IsDynamicCodeSupported=False count=" dynamic + 0 "\n"
	printf "%s", out > dest
	printf "%s", out
}
' "$out/$name-logcat.txt"
