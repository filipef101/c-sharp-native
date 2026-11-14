#!/usr/bin/env bash
# Cold start and Feed scroll for a MacBook emulator whose AVD is named
# pixel_fold_mc. Boot that AVD yourself, then pass the serial from
# `adb devices`. This file does not start the emulator, does not build,
# and does not call another script. adb, awk, and sort are enough.
# bash 3.2 is enough.
#
#   tools/bench/pixel-fold.sh --self-test
#   tools/bench/pixel-fold.sh cold --device <serial> --out ~/pixel-fold-cold --n 20 \
#       --left-apk counter-arm64.apk --left-pkg com.scratch.nativepoc --left-name counter \
#       --right-apk showcase-arm64.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
#   tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-scroll \
#       --apk feed.apk \
#       --activity com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#       --swipes 8 --rounds 5 \
#       --extra "--es nativepoc_root feed"
set -euo pipefail

usage() {
	cat <<'EOF'
tools/bench/pixel-fold.sh cold --device SERIAL --out DIR --n 20 \
    --left-apk APK --left-pkg PKG --left-name NAME \
    --right-apk APK --right-pkg PKG --right-name NAME
tools/bench/pixel-fold.sh scroll --device SERIAL --out DIR --apk APK \
    --activity COMPONENT [--swipes 8] [--rounds 5] [--extra "..."]
tools/bench/pixel-fold.sh --self-test

adb only. No repo build. Cold start installs each apk once and writes
DIR/results.txt. Scroll appends raw framestats to that same file.
EOF
}

script_path() {
	# BASH_SOURCE works on bash 3.2. dirname/cd is the path of this file.
	cd "$(dirname "${BASH_SOURCE[0]}")" && pwd
}

here="$(script_path)"

fail() {
	echo "$1" >&2
	exit 1
}

# Hyndman-Fan type 7. One number per line on stdin. Prints p50 and p90.
percentile() {
	awk '
		function pick(p,    h, lo, hi, w) {
			if (n == 0)
				return "na"
			h = 1 + (n - 1) * p
			lo = int(h)
			hi = lo + 1
			if (hi > n)
				hi = n
			if (lo < 1)
				lo = 1
			w = h - lo
			return s[lo - 1] + w * (s[hi - 1] - s[lo - 1])
		}
		{ v[n++] = $1 + 0 }
		END {
			if (n == 0) {
				print "p50=na p90=na"
				exit
			}
			for (i = 1; i < n; i++) {
				key = v[i]
				j = i - 1
				while (j >= 0 && v[j] > key) {
					v[j + 1] = v[j]
					j--
				}
				v[j + 1] = key
			}
			for (i = 0; i < n; i++)
				s[i] = v[i]
			printf "p50=%.1f p90=%.1f\n", pick(0.50), pick(0.90)
		}
	'
}

self_test() {
	text="$(cat "$here/pixel-fold.sh")"
	body="${text%%self_test()*}"
	case "$body" in
		*interleave-cold.sh*) fail "pixel-fold still delegates the cold bench" ;;
		*scroll-bench.sh*) fail "pixel-fold still delegates the scroll bench" ;;
	esac
	got="$(printf '%s\n' 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 | percentile)"
	[ "$got" = "p50=10.5 p90=18.1" ] || fail "percentile drifted: $got"
	echo "pixel-fold self-test ok"
}

if [ "${1:-}" = "--self-test" ]; then
	self_test
	exit 0
fi

mode="${1:-}"
if [ "$mode" != "cold" ] && [ "$mode" != "scroll" ]; then
	usage >&2
	exit 2
fi
shift

device=""
out=""
n=20
left_apk=""
right_apk=""
left_pkg="com.scratch.nativepoc"
right_pkg="com.scratch.nativepoc.showcase"
left_name="counter"
right_name="showcase"
apk=""
activity=""
swipes=8
rounds=5
extra=""

while [ $# -gt 0 ]; do
	case "$1" in
		--device) device="$2"; shift 2 ;;
		--out) out="$2"; shift 2 ;;
		--n) n="$2"; shift 2 ;;
		--left-apk) left_apk="$2"; shift 2 ;;
		--right-apk) right_apk="$2"; shift 2 ;;
		--left-pkg) left_pkg="$2"; shift 2 ;;
		--right-pkg) right_pkg="$2"; shift 2 ;;
		--left-name) left_name="$2"; shift 2 ;;
		--right-name) right_name="$2"; shift 2 ;;
		--apk) apk="$2"; shift 2 ;;
		--activity) activity="$2"; shift 2 ;;
		--swipes) swipes="$2"; shift 2 ;;
		--rounds) rounds="$2"; shift 2 ;;
		--extra) extra="$2"; shift 2 ;;
		*) fail "unknown argument: $1" ;;
	esac
done

[ -n "$device" ] || fail "pass --device (adb serial, not the AVD name)"
[ -n "$out" ] || fail "pass --out"

adb_bin="${ANDROID_HOME:-$HOME/android-sdk}/platform-tools/adb"
if [ ! -x "$adb_bin" ]; then
	adb_bin="$(command -v adb || true)"
fi
[ -n "$adb_bin" ] && [ -x "$adb_bin" ] || fail "adb not found"

adb_cmd() {
	"$adb_bin" -s "$device" "$@"
}

resolve_activity() {
	pkg="$1"
	resolved="$(adb_cmd shell cmd package resolve-activity --brief "$pkg" | tr -d '\r')"
	component="$(printf '%s\n' "$resolved" | awk '/\// && $0 !~ / / { line = $0 } END { print line }')"
	[ -n "$component" ] || fail "could not resolve $pkg"
	printf '%s\n' "$component"
}

install_apk() {
	pkg="$1"
	file="$2"
	adb_cmd shell am force-stop "$pkg" >/dev/null 2>&1 || true
	install_out="$(adb_cmd install -r -d "$file" 2>&1)" || true
	printf '%s\n' "$install_out" | grep -q "Success" || fail "install failed: $file"
}

wait_guest() {
	i=0
	while [ "$i" -lt 180 ]; do
		load="$(adb_cmd shell cat /proc/loadavg | awk '{ print $1 }' | tr -d '\r')"
		awk -v v="$load" 'BEGIN { exit !((v + 0) < 2.0) }' && {
			printf '%s\n' "$load"
			return 0
		}
		i=$((i + 1))
		sleep 1
	done
	fail "guest load stayed at or above 2.0"
}

fully_drawn_ms() {
	pkg="$1"
	mark="$2"
	adb_cmd shell logcat -d -v brief | tr -d '\r' | awk -v pkg="$pkg" -v mark="$mark" '
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
			at = index($0, mark)
			if (at > 0 && substr($0, at + length(mark), 1) !~ /[0-9]/) {
				seen = 1
				next
			}
		}
		seen && found == "" && index($0, "Fully drawn " pkg "/") { found = $0 }
		END {
			if (found == "")
				exit
			n = index(found, "+")
			if (n == 0)
				exit
			rest = substr(found, n + 1)
			num = digits(rest)
			after = substr(rest, length(num) + 1)
			if (substr(after, 1, 2) == "ms") {
				print num + 0
				exit
			}
			if (substr(after, 1, 1) == "s") {
				ms = digits(substr(after, 2))
				if (ms != "" && substr(after, 2 + length(ms), 2) == "ms")
					print (num + 0) * 1000 + (ms + 0)
			}
		}
	'
}

mkdir -p "$out"
results="$out/results.txt"

if [ "$mode" = "scroll" ]; then
	[ -n "$apk" ] || fail "scroll needs --apk"
	[ -n "$activity" ] || fail "scroll needs --activity"
	pkg="${activity%%/*}"
	install_apk "$pkg" "$apk"
	# shellcheck disable=SC2086
	adb_cmd shell am start -n "$activity" $extra >/dev/null
	sleep 2
	: >"$results"
	r=1
	while [ "$r" -le "$rounds" ]; do
		adb_cmd shell dumpsys gfxinfo "$pkg" reset >/dev/null || true
		s=1
		while [ "$s" -le "$swipes" ]; do
			adb_cmd shell input swipe 540 1800 540 600 80
			s=$((s + 1))
		done
		sleep 1
		{
			echo "round=$r"
			adb_cmd shell dumpsys gfxinfo "$pkg" framestats
		} >>"$results"
		r=$((r + 1))
	done
	echo "wrote $results"
	exit 0
fi

[ -n "$left_apk" ] || fail "cold needs --left-apk"
[ -n "$right_apk" ] || fail "cold needs --right-apk"

install_apk "$left_pkg" "$left_apk"
install_apk "$right_pkg" "$right_apk"
left_component="$(resolve_activity "$left_pkg")"
right_component="$(resolve_activity "$right_pkg")"

one_start() {
	name="$1"
	pkg="$2"
	component="$3"
	index="$4"
	wait_guest >/dev/null
	mark="nativepoc-sample ${name} ${index}"
	adb_cmd logcat -c >/dev/null || true
	adb_cmd shell log -t NativePocSample "$mark" >/dev/null
	text="$(adb_cmd shell am start -W -S -n "$component")"
	state="$(printf '%s\n' "$text" | awk '/^LaunchState: / { print $2 }')"
	total="$(printf '%s\n' "$text" | awk '/^TotalTime: / { print $2 }')"
	[ "$state" = "COLD" ] && [ -n "$total" ] || fail "$name $index was not COLD"
	drawn=""
	attempt=1
	while [ "$attempt" -le 15 ]; do
		drawn="$(fully_drawn_ms "$pkg" "$mark" || true)"
		[ -n "$drawn" ] && break
		sleep 0.2
		attempt=$((attempt + 1))
	done
	printf '%s\n' "$total" >>"$out/${name}-total.txt"
	printf '%s\n' "${drawn:-}" >>"$out/${name}-drawn.txt"
	echo "$name $index total=$total drawn=${drawn:-none}"
}

# Discard one warmup of each side.
wait_guest >/dev/null
adb_cmd shell am start -W -S -n "$left_component" >/dev/null
wait_guest >/dev/null
adb_cmd shell am start -W -S -n "$right_component" >/dev/null
: >"$out/${left_name}-total.txt"
: >"$out/${left_name}-drawn.txt"
: >"$out/${right_name}-total.txt"
: >"$out/${right_name}-drawn.txt"

i=0
while [ "$i" -lt "$n" ]; do
	one_start "$left_name" "$left_pkg" "$left_component" "$i"
	one_start "$right_name" "$right_pkg" "$right_component" "$i"
	i=$((i + 1))
done

{
	echo "avd=pixel_fold_mc device=$device n=$n"
	echo "left=$left_name pkg=$left_pkg apk=$left_apk"
	echo "right=$right_name pkg=$right_pkg apk=$right_apk"
	echo "metric=fully-drawn and TotalTime Hyndman-Fan type 7"
	echo "$left_name totals $(tr '\n' ' ' < "$out/${left_name}-total.txt")"
	echo "$left_name totaltime $(percentile < "$out/${left_name}-total.txt")"
	echo "$left_name fully_drawn $(percentile < "$out/${left_name}-drawn.txt")"
	echo "$right_name totals $(tr '\n' ' ' < "$out/${right_name}-total.txt")"
	echo "$right_name totaltime $(percentile < "$out/${right_name}-total.txt")"
	echo "$right_name fully_drawn $(percentile < "$out/${right_name}-drawn.txt")"
} | tee "$results"
