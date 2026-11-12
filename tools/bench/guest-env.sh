#!/usr/bin/env bash
# Guest load, CPU, background dexopt, and emulator GPU for the benches.
# Source this from mac-bench.sh, cold-start.sh, and scroll-bench.sh.
# adb_cmd must already be a function. A 1-minute loadavg at or above 2.0
# prints a warning and the sample stays in the series.
#
#   tools/bench/guest-env.sh --self-test

guest_load_limit="2.0"

parse_guest_load() {
	awk 'NR == 1 { print $1; exit }'
}

classify_gpu() {
	awk '
		index($0, "GLES:") {
			if ($0 ~ /SwiftShader/)
				kind = "swiftshader"
			else
				kind = "host"
		}
		END {
			if (kind == "")
				kind = "unknown"
			print kind
		}
	'
}

dexopt_running() {
	awk '
		/dex2oat/ || /odrefresh/ || /dexoptanalyzer/ { found = 1 }
		END { print (found ? "running" : "idle") }
	'
}

dexopt_ready() {
	awk '
		BEGIN { ready = "absent" }
		/JOB #/ && /BackgroundDexoptJobService/ { grab = 1; next }
		grab && /^  JOB #/ { exit }
		grab && /Ready:/ {
			if ($0 ~ /Ready:[[:space:]]*true/)
				ready = "true"
			else
				ready = "false"
			exit
		}
		END { print ready }
	'
}

top_busy() {
	awk '
		$1 ~ /^[0-9]+$/ {
			cmd = ""
			for (i = 12; i <= NF; i++)
				cmd = cmd (cmd == "" ? "" : " ") $i
			if (cmd ~ /^(top|sh|grep|head|awk|dumpsys|ps)( |$)/)
				next
			if (cmd == "")
				next
			print $1, $9, cmd
			exit
		}
	'
}

guest_warning() {
	local load="$1"
	awk -v load="$load" -v limit="$guest_load_limit" 'BEGIN {
		if (load ~ /^[0-9]+([.][0-9]+)?$/ && (load + 0) >= (limit + 0))
			printf "warning: guest load %s is high (>= %s). Same-run relatives are usable. Absolutes from this round are not.\n", load, limit
	}'
}

capture_guest() {
	local dest="$1"
	local loadavg top surface ps jobs load gpu dex ready busy warn
	# awk reads each stream to the end. head/exit would SIGPIPE adb, and the
	# benches run with pipefail, which would abort the round.
	loadavg="$(adb_cmd shell cat /proc/loadavg | tr -d '\r')"
	top="$(adb_cmd shell top -n 1 -b | tr -d '\r' | awk 'NR <= 18')"
	surface="$(adb_cmd shell dumpsys SurfaceFlinger | tr -d '\r' | awk '/GLES:/ { if (!seen++) print }')"
	ps="$(adb_cmd shell ps -A | tr -d '\r')"
	jobs="$(adb_cmd shell dumpsys jobscheduler | tr -d '\r' | awk '
		stop { next }
		/JOB #/ && /BackgroundDexoptJobService/ { grab = 1 }
		grab { print }
		grab && /Ready:/ { stop = 1 }
	')"
	load="$(printf '%s\n' "$loadavg" | parse_guest_load)"
	gpu="$(printf '%s\n' "$surface" | classify_gpu)"
	dex="$(printf '%s\n' "$ps" | dexopt_running)"
	ready="$(printf '%s\n' "$jobs" | dexopt_ready)"
	busy="$(printf '%s\n' "$top" | top_busy)"
	{
		echo "loadavg=$loadavg"
		echo "guest_load=$load"
		echo "gpu=$gpu"
		echo "gles=${surface:-none}"
		echo "dexopt=$dex"
		echo "dexopt_ready=$ready"
		echo "top_busy=${busy:-none}"
		echo "TOP"
		printf '%s\n' "$top"
		echo "DEXOPT_JOB"
		printf '%s\n' "$jobs"
	} >"$dest"
	warn="$(guest_warning "$load")"
	printf 'guest_load=%s dexopt=%s dexopt_ready=%s gpu=%s top_busy=%s\n' \
		"$load" "$dex" "$ready" "$gpu" "${busy:-none}"
	if [[ -n "$warn" ]]; then
		printf '%s\n' "$warn"
	fi
}

package_background() {
	local pkg="$1"
	local dest="$2"
	local dump procs
	dump="$(adb_cmd shell dumpsys package "$pkg" | tr -d '\r')"
	procs="$(adb_cmd shell ps -A | tr -d '\r' | grep -F "$pkg" || true)"
	{
		echo "package=$pkg"
		printf '%s\n' "$dump" | awk '
			/Activity Resolver Table:/ { kind = "activity" }
			/Receiver Resolver Table:/ { kind = "receiver" }
			/Service Resolver Table:/ { kind = "service" }
			/Provider Resolver Table:/ { kind = "provider" }
			/Registered ContentProviders:/ { kind = "provider" }
			/ContentProvider Authorities:/ { kind = "" }
			/Domain verification status:/ { kind = "" }
			/Packages:/ { kind = "" }
			kind != "" && $0 ~ /[A-Za-z0-9_.$]+\/[A-Za-z0-9_.$]+/ { print kind, $0 }
		'
		if ! printf '%s\n' "$dump" | awk '/Service Resolver Table:/{found=1} END{exit !found}'; then
			echo "service none"
		fi
		if ! printf '%s\n' "$dump" | awk '/Receiver Resolver Table:/{found=1} END{exit !found}'; then
			echo "receiver none"
		fi
		echo "processes:"
		if [[ -n "$procs" ]]; then
			printf '%s\n' "$procs"
		else
			echo none
		fi
	} >"$dest"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
	set -euo pipefail
	if [[ "${1:-}" != "--self-test" ]]; then
		echo "usage: tools/bench/guest-env.sh --self-test" >&2
		exit 2
	fi
	fail=0
	load="$(printf '%s\n' '1.13 0.80 0.40 2/100 9' | parse_guest_load)"
	[[ "$load" == "1.13" ]] || { echo "load $load" >&2; fail=1; }
	swift="$(printf '%s\n' 'GLES: Google, Android Emulator OpenGL ES Translator (Google SwiftShader), OpenGL ES 3.0' | classify_gpu)"
	host="$(printf '%s\n' 'GLES: Android Emulator OpenGL ES Translator (Host GPU)' | classify_gpu)"
	none="$(printf '%s\n' 'no renderer' | classify_gpu)"
	[[ "$swift" == "swiftshader" ]] || { echo "gpu $swift" >&2; fail=1; }
	[[ "$host" == "host" ]] || { echo "gpu $host" >&2; fail=1; }
	[[ "$none" == "unknown" ]] || { echo "gpu $none" >&2; fail=1; }
	idle="$(printf '%s\n' 'root 1 init' 'u0_a1 com.scratch.nativepoc' | dexopt_running)"
	busydex="$(printf '%s\n' 'root 88 dex2oat64' | dexopt_running)"
	[[ "$idle" == "idle" ]] || { echo "dex $idle" >&2; fail=1; }
	[[ "$busydex" == "running" ]] || { echo "dex $busydex" >&2; fail=1; }
	ready="$(printf '%s\n' '  JOB #1000/1: android/com.android.server.art.BackgroundDexoptJobService' '    Ready: false (job=false)' | dexopt_ready)"
	[[ "$ready" == "false" ]] || { echo "ready $ready" >&2; fail=1; }
	top_line="$(printf '%s\n' '  PID USER PR NI VIRT RES SHR S %CPU %MEM TIME+ ARGS' '  10 shell 20 0 10G 4M 3M R 7.6 0.1 0:00.00 top -n 1 -b' '  88 u0_a12 20 0 13G 90M 60M S 40.0 3.0 0:12.00 com.scratch.nativepoc' | top_busy)"
	[[ "$top_line" == "88 40.0 com.scratch.nativepoc" ]] || { echo "top $top_line" >&2; fail=1; }
	quiet="$(guest_warning 1.13)"
	loud="$(guest_warning 2.0)"
	[[ -z "$quiet" ]] || { echo "quiet warning $quiet" >&2; fail=1; }
	[[ "$loud" == "warning: guest load 2.0 is high (>= 2.0). Same-run relatives are usable. Absolutes from this round are not." ]] || { echo "loud $loud" >&2; fail=1; }
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "guest-env self-test ok"
fi
