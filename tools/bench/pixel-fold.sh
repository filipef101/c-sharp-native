#!/usr/bin/env bash
# Cold start and Feed scroll for a MacBook emulator whose AVD is named
# pixel_fold_mc. Boot that AVD yourself, then pass the serial from
# `adb devices`. This file does not start the emulator, does not build,
# and does not call another script. adb, awk, grep, tail, and sort are enough.
# --scenario hard also uses python3 to serve the row PNG. bash 3.2 is enough.
#
#   tools/bench/pixel-fold.sh --self-test
#   tools/bench/pixel-fold.sh cold --device <serial> --out ~/pixel-fold-cold --n 20 \
#       --left-apk counter-arm64.apk --left-pkg com.scratch.nativepoc --left-name counter \
#       --right-apk showcase-arm64.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
#   tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-scroll \
#       --apk showcase-arm64.apk \
#       --activity com.scratch.nativepoc.showcase/crc64ee53024f7ed0aa60.MainActivity \
#       --swipes 8 --rounds 5 \
#       --extra "--es nativepoc_root feed"
#   tools/bench/pixel-fold.sh scroll --device <serial> --out ~/pixel-fold-hard \
#       --apk showcase-arm64.apk \
#       --activity com.scratch.nativepoc.showcase/crc64ee53024f7ed0aa60.MainActivity \
#       --swipes 8 --rounds 5 --scenario hard \
#       --extra "--es nativepoc_root feed"
#   tools/bench/pixel-fold.sh shot --device <serial> --out shot.png [--display 0]
set -euo pipefail

usage() {
	cat <<'EOF'
tools/bench/pixel-fold.sh cold --device SERIAL --out DIR --n 20 \
    --left-apk APK --left-pkg PKG --left-name NAME \
    --right-apk APK --right-pkg PKG --right-name NAME
tools/bench/pixel-fold.sh scroll --device SERIAL --out DIR --apk APK \
    --activity COMPONENT [--swipes 8] [--rounds 5] [--extra "..."] \
    [--scenario easy|hard]
tools/bench/pixel-fold.sh shot --device SERIAL --out FILE.png [--display ID]
tools/bench/pixel-fold.sh --self-test

adb, plus python3 for the hard-scenario image. No repo build. Cold start installs each apk once and writes
DIR/results.txt, including the n that actually ran. Scroll writes a
framestats summary (janky percent, p50/p90/p99), DIR/summary.json, and
DIR/end.png. A screenshot passes -d, drops a leading display warning,
and tries the next display when the file is not a PNG. --scenario hard
flings faster over the 1000-row feed and serves /img/ on port 8094.
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

# Same percentile, plus p99, for framestats frame times.
frame_percentile() {
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
				print "p50=na p90=na p99=na"
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
			printf "p50=%.1f p90=%.1f p99=%.1f\n", pick(0.50), pick(0.90), pick(0.99)
		}
	'
}

lines_of() {
	local file="$1"
	if [ ! -f "$file" ]; then
		printf '0\n'
		return
	fi
	wc -l <"$file" | tr -d ' '
}

cold_header() {
	local requested="$1"
	local left_file="$2"
	local right_file="$3"
	local left_n right_n
	left_n="$(lines_of "$left_file")"
	right_n="$(lines_of "$right_file")"
	printf 'n_requested=%s n_run=%s right_n=%s\n' "$requested" "$left_n" "$right_n"
}

# Drop a text warning that some foldable images prepend to a PNG.
# The capture also passes -d so the warning is not required.
strip_png_warning() {
	local src="$1"
	local dest="$2"
	local hit offset
	hit="$(LC_ALL=C grep -a -b -o "$(printf '\211PNG')" "$src" | head -1 || true)"
	offset="${hit%%:*}"
	if [ -z "$hit" ] || [ -z "$offset" ]; then
		cp "$src" "$dest"
		return 1
	fi
	tail -c "+$((offset + 1))" "$src" >"$dest"
}

# A foldable `screencap -p -d 0` can write a few dozen bytes that are not
# a PNG. Magic plus a minimum size rejects that file.
png_ok() {
	local file="$1"
	local size magic
	size="$(wc -c <"$file" | tr -d ' ')"
	[ "$size" -ge 128 ] || return 1
	magic="$(od -An -t x1 -N 8 "$file" | tr -d ' \n')"
	[ "$magic" = "89504e470d0a1a0a" ]
}

display_ids() {
	local ids
	ids="$(adb_cmd shell dumpsys display | tr -d '\r' | awk '
		/mDisplayId=/ {
			line = $0
			sub(/.*mDisplayId=/, "", line)
			sub(/[^0-9].*/, "", line)
			if (line != "" && !seen[line]++)
				print line
		}
	')"
	if [ -z "$ids" ]; then
		printf '0\n'
		return
	fi
	printf '%s\n' "$ids"
}

try_capture() {
	local dest="$1"
	local display="$2"
	local raw
	raw="$(mktemp)"
	if [ -n "$display" ]; then
		adb_cmd exec-out screencap -p -d "$display" >"$raw" || true
	else
		adb_cmd exec-out screencap -p >"$raw" || true
	fi
	if ! strip_png_warning "$raw" "$dest"; then
		rm -f "$raw"
		return 1
	fi
	rm -f "$raw"
	png_ok "$dest"
}

# Try the requested display, then every other id, then a capture with no -d.
capture_png() {
	local dest="$1"
	local prefer="$2"
	local id tried=""
	if [ -n "$prefer" ]; then
		if try_capture "$dest" "$prefer"; then
			echo "shot display=$prefer file=$dest"
			return 0
		fi
		tried=" $prefer "
	fi
	while IFS= read -r id; do
		[ -n "$id" ] || continue
		case "$tried" in
			*" $id "*) continue ;;
		esac
		tried="$tried$id "
		if try_capture "$dest" "$id"; then
			echo "shot display=$id file=$dest"
			return 0
		fi
	done <<EOF
$(display_ids)
EOF
	if try_capture "$dest" ""; then
		echo "shot display=default file=$dest"
		return 0
	fi
	fail "screencap was not a PNG"
}

summary_field() {
	local text="$1"
	local key="$2"
	printf '%s\n' "$text" | tr ' ' '\n' | awk -F= -v key="$key" '$1 == key { print $2; exit }'
}

write_summary_json() {
	local text="$1"
	local dest="$2"
	local scenario="$3"
	local shot="$4"
	local display="$5"
	local rows="${6:-}"
	local applies="${7:-}"
	local fails="${8:-}"
	local hits="${9:-}"
	local frames janky pct p50 p90 p99
	frames="$(summary_field "$text" frames)"
	janky="$(summary_field "$text" janky)"
	pct="$(summary_field "$text" janky_pct)"
	p50="$(summary_field "$text" p50)"
	p90="$(summary_field "$text" p90)"
	p99="$(summary_field "$text" p99)"
	if [ -n "$rows" ]; then
		printf '{"scenario":"%s","frames":%s,"janky":%s,"janky_pct":%s,"p50":%s,"p90":%s,"p99":%s,"screenshot":"%s","display":"%s","rows":%s,"image_bitmap":%s,"image_fail":%s,"image_hits":%s}\n' \
			"$scenario" "$frames" "$janky" "$pct" "$p50" "$p90" "$p99" "$shot" "$display" "$rows" "$applies" "$fails" "$hits" >"$dest"
		return
	fi
	printf '{"scenario":"%s","frames":%s,"janky":%s,"janky_pct":%s,"p50":%s,"p90":%s,"p99":%s,"screenshot":"%s","display":"%s"}\n' \
		"$scenario" "$frames" "$janky" "$pct" "$p50" "$p90" "$p99" "$shot" "$display" >"$dest"
}

start_feed_images() {
	local py
	py="$(mktemp)"
	cat >"$py" <<'PY'
import struct, zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

def chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

def png(width, height):
    rows = []
    for y in range(height):
        row = bytearray(1 + width * 3)
        for x in range(width):
            row[1 + x * 3] = (x * 3 + y) & 255
            row[2 + x * 3] = 90
            row[3 + x * 3] = 40
        rows.append(bytes(row))
    raw = b"".join(rows)
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 1)) + chunk(b"IEND", b"")

body = png(640, 240)
feed = b'{"items":[{"id":"0000","title":"Row 0000"}]}'
hits = 0

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        global hits
        if self.path.startswith("/img/"):
            hits += 1
            with open("/tmp/pixel-fold-img.count", "w") as handle:
                handle.write(str(hits))
            self.send_response(200)
            self.send_header("Content-Type", "image/png")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if self.path.startswith("/feed"):
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(feed)))
            self.end_headers()
            self.wfile.write(feed)
            return
        self.send_response(404)
        self.end_headers()

    def log_message(self, fmt, *args):
        return

ThreadingHTTPServer(("127.0.0.1", 8094), Handler).serve_forever()
PY
	python3 "$py" >/tmp/pixel-fold-img.log 2>&1 &
	image_pid=$!
	sleep 0.4
	if ! kill -0 "$image_pid" 2>/dev/null; then
		echo "image server did not stay up" >&2
		image_pid=""
		return 0
	fi
	adb_cmd reverse tcp:8094 tcp:8094 >/dev/null || true
	echo "image server pid=$image_pid reverse=tcp:8094 rows=1000 png=640x240"
}

stop_feed_images() {
	if [ -n "${image_pid:-}" ]; then
		kill "$image_pid" 2>/dev/null || true
		wait "$image_pid" 2>/dev/null || true
		image_pid=""
	fi
}

summarize_framestats() {
	local raw="$1"
	local times total_line frames janky pct
	times="$(mktemp)"
	total_line="$(awk -v times="$times" '
		/^round=/ {
			seen_total = 0
			seen_janky = 0
			in_profile = 0
			got_profile = 0
			next
		}
		/^---PROFILEDATA---/ {
			if (in_profile) {
				in_profile = 0
				got_profile = 1
			} else if (!got_profile)
				in_profile = 1
			next
		}
		in_profile && $0 ~ /^[01],/ {
			n = split($0, f, ",")
			intended = f[3] + 0
			completed = f[17] + 0
			if (intended > 0 && completed > intended) {
				ms = (completed - intended) / 1000000
				if (ms > 2000)
					skipped++
				else
					print ms >> times
			}
			next
		}
		index($0, "Total frames rendered:") == 1 && !seen_total {
			seen_total = 1
			frames += $4
		}
		index($0, "Janky frames:") == 1 && !seen_janky {
			seen_janky = 1
			janky += $3
		}
		END { printf "%d %d %d\n", frames + 0, janky + 0, skipped + 0 }
	' "$raw")"
	frames="${total_line%% *}"
	rest="${total_line#* }"
	janky="${rest%% *}"
	skipped="${rest#* }"
	if [ "$frames" -gt 0 ]; then
		pct="$(awk -v j="$janky" -v f="$frames" 'BEGIN { printf "%.1f", (100.0 * j) / f }')"
	else
		pct="na"
	fi
	printf 'frames=%s janky=%s janky_pct=%s\n' "$frames" "$janky" "$pct"
	printf 'frame_ms %s skipped=%s\n' "$(frame_percentile <"$times")" "$skipped"
	rm -f "$times"
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
	got="$(printf '%s\n' 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 | frame_percentile)"
	[ "$got" = "p50=10.5 p90=18.1 p99=19.8" ] || fail "frame percentile drifted: $got"

	# install_apk captures adb in a subshell, so the stub counts in a file.
	install_state="$(mktemp)"
	: >"$install_state"
	adb_cmd() {
		case "$*" in
			*uninstall*)
				printf 'uninstall\n' >>"$install_state"
				printf 'Success\n'
				;;
			*"install "*)
				local prior
				prior="$(grep -c '^install$' "$install_state" || true)"
				printf 'install\n' >>"$install_state"
				if [ "$prior" -eq 0 ]; then
					printf '%s\n' 'Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]'
					return 1
				fi
				printf 'Success\n'
				;;
			*)
				printf '%s\n' '0.10 0.10 0.10'
				;;
		esac
	}
	i=0
	pair=4
	seen=""
	while [ "$i" -lt 4 ]; do
		wait_guest >/dev/null
		seen="$seen $i"
		i=$((i + 1))
	done
	[ "$seen" = " 0 1 2 3" ] || fail "loop index clobbered:$seen"
	[ "$pair" = 4 ] || fail "wait_guest clobbered pair ($pair)"

	log="$(install_apk com.example /tmp/pixel-fold-none.apk 2>&1)" || fail "retry install failed: $log"
	case "$log" in
		*"uninstall and retry once"*) ;;
		*) fail "signature retry was not logged: $log" ;;
	esac
	[ "$(grep -c '^install$' "$install_state")" = 2 ] || fail "install ran $(grep -c '^install$' "$install_state") times"
	[ "$(grep -c '^uninstall$' "$install_state")" = 1 ] || fail "uninstall ran $(grep -c '^uninstall$' "$install_state") times"
	rm -f "$install_state"

	local src dest magic
	src="$(mktemp)"
	dest="$(mktemp)"
	{
		printf '%s\n' '[Warning] Multiple displays were found, using display 0'
		printf '\211PNG\r\n\032\nBODY'
	} >"$src"
	strip_png_warning "$src" "$dest"
	magic="$(od -An -t x1 -N 8 "$dest" | tr -d ' \n')"
	[ "$magic" = "89504e470d0a1a0a" ] || fail "screencap warning was not stripped: $magic"
	rm -f "$src" "$dest"

	src="$(mktemp)"
	dest="$(mktemp)"
	leftf="$(mktemp)"
	rightf="$(mktemp)"
	printf '1\n2\n3\n' >"$leftf"
	printf '4\n5\n6\n7\n' >"$rightf"
	got="$(cold_header 20 "$leftf" "$rightf")"
	[ "$got" = "n_requested=20 n_run=3 right_n=4" ] || fail "n_run drifted: $got"
	rm -f "$src" "$dest" "$leftf" "$rightf"

	src="$(mktemp)"
	cat >"$src" <<'EOF'
round=1
Total frames rendered: 4
Janky frames: 1 (25.00%)
Total frames rendered: 99
Janky frames: 99 (99.00%)
---PROFILEDATA---
Flags,FrameTimelineVsyncId,IntendedVsync,Vsync,InputEventId,HandleInputStart,AnimationStart,PerformTraversalsStart,DrawStart,FrameDeadline,FrameInterval,FrameStartTime,SyncQueued,SyncStart,IssueDrawCommandsStart,SwapBuffers,FrameCompleted
0,1,1000000000,1000000000,0,0,0,0,0,0,0,0,0,0,0,0,1008000000
0,1,2000000000,2000000000,0,0,0,0,0,0,0,0,0,0,0,0,2016000000
0,1,3000000000,3000000000,0,0,0,0,0,0,0,0,0,0,0,0,3024000000
0,1,4000000000,4000000000,0,0,0,0,0,0,0,0,0,0,0,0,4040000000
0,1,1000,1000,0,0,0,0,0,0,0,0,0,0,0,0,9000000000000
---PROFILEDATA---
EOF
	got="$(summarize_framestats "$src")"
	got_stats="$got"
	case "$got" in
		*"frames=4 janky=1 janky_pct=25.0"*|*"frame_ms p50=20.0 p90=35.2 p99=39.5"*) ;;
		*) fail "framestats summary drifted: $got" ;;
	esac
	printf '%s\n' "$got" | grep -q 'frames=4 janky=1 janky_pct=25.0' || fail "janky summary drifted: $got"
	printf '%s\n' "$got" | grep -q 'frame_ms p50=20.0 p90=35.2 p99=39.5 skipped=1' || fail "frame summary drifted: $got"

	tiny="$(mktemp)"
	printf '%79s' 'x' >"$tiny"
	[ "$(wc -c <"$tiny" | tr -d ' ')" = 79 ] || fail "79-byte fixture drifted"
	png_ok "$tiny" && fail "a 79-byte screencap was accepted"
	rm -f "$tiny"

	adb_cmd() {
		case "$*" in
			*dumpsys*display*)
				printf '%s\n' 'mDisplayId=0' 'mDisplayId=2'
				;;
			*screencap*-d\ 0*)
				printf '%79s' 'x'
				;;
			*screencap*-d\ 2*)
				printf '%s\n' '[Warning] Multiple displays were found, using display 0'
				printf '\211PNG\r\n\032\n'
				dd if=/dev/zero bs=160 count=1 2>/dev/null
				;;
			*)
				printf '%s\n' '0.10 0.10 0.10'
				;;
		esac
	}
	shot="$(mktemp)"
	got="$(capture_png "$shot" 0)" || fail "display fallback did not find a PNG"
	case "$got" in
		"shot display=2 file=$shot") ;;
		*) fail "display fallback drifted: $got" ;;
	esac
	png_ok "$shot" || fail "fallback screenshot is not a PNG"
	rm -f "$shot"

	json="$(mktemp)"
	write_summary_json "$got_stats" "$json" hard end.png 2
	grep -q '"scenario":"hard"' "$json" || fail "summary json scenario drifted: $(cat "$json")"
	grep -q '"frames":4' "$json" || fail "summary json frames drifted: $(cat "$json")"
	grep -q '"janky":1' "$json" || fail "summary json janky drifted: $(cat "$json")"
	grep -q '"janky_pct":25.0' "$json" || fail "summary json pct drifted: $(cat "$json")"
	grep -q '"p50":20.0' "$json" || fail "summary json p50 drifted: $(cat "$json")"
	grep -q '"p90":35.2' "$json" || fail "summary json p90 drifted: $(cat "$json")"
	grep -q '"p99":39.5' "$json" || fail "summary json p99 drifted: $(cat "$json")"
	grep -q '"screenshot":"end.png"' "$json" || fail "summary json shot drifted: $(cat "$json")"
	grep -q '"display":"2"' "$json" || fail "summary json display drifted: $(cat "$json")"
	rm -f "$json" "$src"
	echo "pixel-fold self-test ok"
}

adb_cmd() {
	"$adb_bin" -s "$device" "$@"
}

resolve_activity() {
	local pkg="$1"
	local resolved component
	resolved="$(adb_cmd shell cmd package resolve-activity --brief "$pkg" | tr -d '\r')"
	component="$(printf '%s\n' "$resolved" | awk '/\// && $0 !~ / / { line = $0 } END { print line }')"
	[ -n "$component" ] || fail "could not resolve $pkg"
	printf '%s\n' "$component"
}

install_apk() {
	local pkg="$1"
	local file="$2"
	local install_out
	adb_cmd shell am force-stop "$pkg" >/dev/null 2>&1 || true
	install_out="$(adb_cmd install -r -d "$file" 2>&1)" || true
	if printf '%s\n' "$install_out" | grep -q "Success"; then
		return 0
	fi
	if printf '%s\n' "$install_out" | grep -q "INSTALL_FAILED_UPDATE_INCOMPATIBLE"; then
		echo "install signature mismatch for $pkg; uninstall and retry once" >&2
		adb_cmd uninstall "$pkg" >/dev/null 2>&1 || adb_cmd shell pm uninstall "$pkg" >/dev/null 2>&1 || true
		install_out="$(adb_cmd install -r -d "$file" 2>&1)" || true
		printf '%s\n' "$install_out" | grep -q "Success" || fail "install failed after uninstall: $file"
		return 0
	fi
	fail "install failed: $file"
}

wait_guest() {
	local waited=0
	local load
	while [ "$waited" -lt 180 ]; do
		load="$(adb_cmd shell cat /proc/loadavg | awk '{ print $1 }' | tr -d '\r')"
		awk -v v="$load" 'BEGIN { exit !((v + 0) < 2.0) }' && {
			printf '%s\n' "$load"
			return 0
		}
		waited=$((waited + 1))
		sleep 1
	done
	fail "guest load stayed at or above 2.0"
}

fully_drawn_ms() {
	local pkg="$1"
	local mark="$2"
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

if [ "${1:-}" = "--self-test" ]; then
	self_test
	exit 0
fi

mode="${1:-}"
if [ "$mode" != "cold" ] && [ "$mode" != "scroll" ] && [ "$mode" != "shot" ]; then
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
display=""
scenario="easy"
image_pid=""

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
		--display) display="$2"; shift 2 ;;
		--scenario) scenario="$2"; shift 2 ;;
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

if [ "$mode" = "shot" ]; then
	capture_png "$out" "$display"
	exit 0
fi

mkdir -p "$out"
results="$out/results.txt"

if [ "$mode" = "scroll" ]; then
	[ -n "$apk" ] || fail "scroll needs --apk"
	[ -n "$activity" ] || fail "scroll needs --activity"
	[ "$scenario" = "easy" ] || [ "$scenario" = "hard" ] || fail "scenario is easy or hard"
	pkg="${activity%%/*}"
	trap stop_feed_images EXIT
	if [ "$scenario" = "hard" ]; then
		start_feed_images
	fi
	install_apk "$pkg" "$apk"
	# -S so the extra is the cold root. A warm start would push onto home.
	# shellcheck disable=SC2086
	adb_cmd shell am start -S -n "$activity" $extra >/dev/null
	sleep 2
	adb_cmd logcat -c >/dev/null || true
	raw="$out/framestats-raw.txt"
	image_log="$out/image-log.txt"
	: >"$raw"
	: >"$image_log"
	rm -f /tmp/pixel-fold-img.count
	r=1
	while [ "$r" -le "$rounds" ]; do
		adb_cmd shell dumpsys gfxinfo "$pkg" reset >/dev/null || true
		s=1
		while [ "$s" -le "$swipes" ]; do
			if [ "$scenario" = "hard" ]; then
				if [ $((s % 2)) -eq 1 ]; then
					adb_cmd shell input swipe 540 2100 540 400 30
				else
					adb_cmd shell input swipe 540 400 540 2100 30
				fi
			else
				adb_cmd shell input swipe 540 1800 540 600 80
			fi
			s=$((s + 1))
		done
		sleep 1
		{
			echo "round=$r"
			adb_cmd shell dumpsys gfxinfo "$pkg" framestats
		} >>"$raw"
		# The main log buffer rolls over once images start decoding.
		adb_cmd logcat -d -s NativePoc:I >>"$image_log" || true
		adb_cmd logcat -c >/dev/null || true
		r=$((r + 1))
	done
	summary="$(summarize_framestats "$raw")"
	shot_line="$(capture_png "$out/end.png" "$display")"
	shot_display="${shot_line#*display=}"
	shot_display="${shot_display%% *}"
	adb_cmd logcat -d -s NativePoc:I >>"$image_log" || true
	applies="$(grep -c 'image-bitmap' "$image_log" || true)"
	fails="$(grep -c 'image-fail' "$image_log" || true)"
	hits=0
	if [ -f /tmp/pixel-fold-img.count ]; then
		hits="$(tr -d ' \n' </tmp/pixel-fold-img.count)"
	fi
	{
		echo "avd=pixel_fold_mc device=$device rounds=$rounds swipes=$swipes scenario=$scenario"
		echo "pkg=$pkg activity=$activity"
		if [ "$scenario" = "hard" ]; then
			echo "rows=1000 fling_ms=30 png=640x240 image_bitmap=${applies:-0} image_fail=${fails:-0} image_hits=${hits:-0}"
		fi
		printf '%s\n' "$summary"
		echo "screenshot=$out/end.png"
		echo "$shot_line"
		echo "raw=$raw"
	} | tee "$results"
	if [ "$scenario" = "hard" ]; then
		write_summary_json "$summary" "$out/summary.json" "$scenario" end.png "$shot_display" 1000 "${applies:-0}" "${fails:-0}" "${hits:-0}"
	else
		write_summary_json "$summary" "$out/summary.json" "$scenario" end.png "$shot_display"
	fi
	stop_feed_images
	exit 0
fi

[ -n "$left_apk" ] || fail "cold needs --left-apk"
[ -n "$right_apk" ] || fail "cold needs --right-apk"

install_apk "$left_pkg" "$left_apk"
install_apk "$right_pkg" "$right_apk"
left_component="$(resolve_activity "$left_pkg")"
right_component="$(resolve_activity "$right_pkg")"

one_start() {
	local name="$1"
	local pkg="$2"
	local component="$3"
	local index="$4"
	local text state total drawn attempt
	wait_guest >/dev/null
	local mark="nativepoc-sample ${name} ${index}"
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

pair=0
while [ "$pair" -lt "$n" ]; do
	one_start "$left_name" "$left_pkg" "$left_component" "$pair"
	one_start "$right_name" "$right_pkg" "$right_component" "$pair"
	pair=$((pair + 1))
done

{
	echo "avd=pixel_fold_mc device=$device"
	cold_header "$n" "$out/${left_name}-total.txt" "$out/${right_name}-total.txt"
	echo "left=$left_name pkg=$left_pkg apk=$left_apk"
	echo "right=$right_name pkg=$right_pkg apk=$right_apk"
	echo "metric=fully-drawn and TotalTime Hyndman-Fan type 7"
	echo "$left_name totals $(tr '\n' ' ' <"$out/${left_name}-total.txt")"
	echo "$left_name totaltime $(percentile <"$out/${left_name}-total.txt")"
	echo "$left_name fully_drawn $(percentile <"$out/${left_name}-drawn.txt")"
	echo "$right_name totals $(tr '\n' ' ' <"$out/${right_name}-total.txt")"
	echo "$right_name totaltime $(percentile <"$out/${right_name}-total.txt")"
	echo "$right_name fully_drawn $(percentile <"$out/${right_name}-drawn.txt")"
} | tee "$results"
