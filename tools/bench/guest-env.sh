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

median_values() {
	awk '
		{
			val = ""
			if ($0 ~ /^[0-9]+([.][0-9]+)?$/)
				val = $0
			else {
				for (i = 1; i <= NF; i++) {
					if ($i ~ /^guest_load=/) {
						split($i, parts, "=")
						val = parts[2]
					}
				}
			}
			if (val ~ /^[0-9]+([.][0-9]+)?$/)
				v[++n] = val + 0
		}
		END {
			if (n < 1) {
				print "-"
				exit
			}
			for (i = 2; i <= n; i++) {
				key = v[i]
				j = i - 1
				while (j >= 1 && v[j] > key) {
					v[j + 1] = v[j]
					j--
				}
				v[j + 1] = key
			}
			h = 1 + (n - 1) * 0.50
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

bench_packages=""

quiesce_add_package() {
	local pkg="$1"
	local existing
	[[ -z "$pkg" ]] && return 0
	while IFS= read -r existing; do
		[[ "$existing" == "$pkg" ]] && return 0
	done <<<"${bench_packages:-}"
	bench_packages="${bench_packages}${pkg}
"
}

quiesce_stop_bench() {
	local pkg alive tries
	[[ -n "${bench_packages:-}" ]] || return 0
	while IFS= read -r pkg; do
		[[ -z "$pkg" ]] && continue
		echo "quiesce: force-stop bench $pkg" >&2
		adb_cmd shell am force-stop "$pkg" </dev/null >/dev/null 2>&1 || true
	done <<<"$bench_packages"
	tries=0
	while [[ "$tries" -lt 10 ]]; do
		alive=""
		while IFS= read -r pkg; do
			[[ -z "$pkg" ]] && continue
			if adb_cmd shell pidof "$pkg" </dev/null 2>/dev/null | grep -q '[0-9]'; then
				alive="$pkg"
			fi
		done <<<"$bench_packages"
		if [[ -z "$alive" ]]; then
			echo "quiesce: bench packages stopped" >&2
			return 0
		fi
		tries=$((tries + 1))
		sleep 0.2
	done
	echo "quiesce: bench package still running: $alive" >&2
}

quiesce_names() {
	local self="$1"
	local bench="${2:-}"
	awk -v self="$self" -v bench="$bench" '
		function bench_pkg(name,    n, i, parts) {
			if (name == self)
				return 1
			n = split(bench, parts, ",")
			for (i = 1; i <= n; i++)
				if (parts[i] == name)
					return 1
			return 0
		}
		function system_pkg(name) {
			if (name == "" || bench_pkg(name))
				return 1
			if (index(name, ".") == 0)
				return 1
			if (name == "android" || index(name, "android.") == 1)
				return 1
			if (index(name, "com.android.") == 1)
				return 1
			if (index(name, "com.google.android.") == 1)
				return 1
			return 0
		}
		function remember(name) {
			if (system_pkg(name) || seen[name])
				return
			seen[name] = 1
			order[++n] = name
		}
		BEGIN { section = "" }
		$0 == "FOCUS" { section = "focus"; next }
		$0 == "TOP" { section = "top"; next }
		section == "focus" && /mCurrentFocus=/ {
			line = $0
			if (match(line, /[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+\//))
				remember(substr(line, RSTART, RLENGTH - 1))
		}
		section == "top" && $1 ~ /^[0-9]+$/ {
			cpu = $9 + 0
			if (cpu < 5)
				next
			name = $12
			sub(/:.*/, "", name)
			if (name ~ /^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$/)
				remember(name)
		}
		END {
			for (i = 1; i <= n; i++)
				print order[i]
		}
	'
}

quiesce_decide() {
	local mode="$1"
	local force="$2"
	local names
	names="$(awk 'NF { print }')"
	if [[ -z "$names" ]]; then
		echo ok
		return 0
	fi
	local flat
	flat="$(printf '%s\n' "$names" | paste -sd' ' -)"
	if [[ "$force" -eq 1 ]]; then
		echo "stop $flat"
		return 0
	fi
	if [[ "$mode" == "warn" ]]; then
		echo "warn $flat"
		return 0
	fi
	echo "refuse $flat"
}

quiesce_before() {
	local self="$1"
	local focus top names action pkg bench_csv
	quiesce_stop_bench
	bench_csv="$(printf '%s\n' "${bench_packages:-}" | paste -sd, -)"
	focus="$(adb_cmd shell dumpsys window | tr -d '\r' | awk '/mCurrentFocus=/')"
	top="$(adb_cmd shell top -n 1 -b | tr -d '\r' | awk 'NR <= 30')"
	names="$(printf '%s\n' FOCUS "$focus" TOP "$top" | quiesce_names "$self" "$bench_csv")"
	action="$(printf '%s\n' "$names" | quiesce_decide "${quiesce_mode:-refuse}" "${force_stop_others:-0}")"
	case "$action" in
		ok)
			echo "quiesce: no third-party foreground or top-cpu package" >&2
			;;
		stop\ *)
			echo "quiesce: force-stop ${action#stop }" >&2
			while IFS= read -r pkg; do
				[[ -z "$pkg" ]] && continue
				adb_cmd shell am force-stop "$pkg" </dev/null >/dev/null 2>&1 || true
			done <<<"$names"
			;;
		warn\ *)
			echo "warning: quiesce third-party ${action#warn }. Not stopping them. The series starts." >&2
			;;
		refuse\ *)
			echo "warning: quiesce third-party ${action#refuse }. Refusing to start. Pass --force-stop-others to stop them." >&2
			return 3
			;;
	esac
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
	med="$(printf '%s\n' 1.10 1.40 9.00 | median_values)"
	[[ "$med" == "1.4" ]] || { echo "median $med" >&2; fail=1; }
	guest_med="$(printf '%s\n' 'guest_load=1.0 dexopt=idle' 'warning: ignored' 'guest_load=3.0 dexopt=idle' | median_values)"
	[[ "$guest_med" == "2.0" ]] || { echo "guest median $guest_med" >&2; fail=1; }
	fixture=$'FOCUS\nmCurrentFocus=Window{abc u0 com.other.debug/com.other.debug.Main}\nmFocusedApp=ActivityRecord{u0 com.scratch.nativepoc/crc.Main}\nTOP\n  PID USER PR NI VIRT RES SHR S %CPU %MEM TIME+ ARGS\n  10 shell 20 0 10G 4M 3M R 7.6 0.1 0:00.00 top -n 1 -b\n  88 u0_a12 20 0 13G 90M 60M S 40.0 3.0 0:12.00 com.other.debug\n  90 system 20 0 13G 90M 60M S 80.0 3.0 0:12.00 system_server\n  91 u0 20 0 13G 90M 60M S 30.0 3.0 0:01.00 com.scratch.nativepoc'
	found="$(printf '%s\n' "$fixture" | quiesce_names com.scratch.nativepoc)"
	[[ "$found" == "com.other.debug" ]] || { echo "quiesce names $found" >&2; fail=1; }
	bench_found="$(printf '%s\n' "$fixture" | quiesce_names com.scratch.nativepoc com.other.debug)"
	[[ -z "$bench_found" ]] || { echo "quiesce bench names $bench_found" >&2; fail=1; }
	clear_fix=$'FOCUS\nmCurrentFocus=Window{abc u0 com.google.android.apps.nexuslauncher/com.google.android.apps.nexuslauncher.NexusLauncherActivity}\nTOP\n  90 system 20 0 13G 90M 60M S 80.0 3.0 0:12.00 system_server'
	clear_names="$(printf '%s\n' "$clear_fix" | quiesce_names com.scratch.nativepoc)"
	[[ -z "$clear_names" ]] || { echo "quiesce clear $clear_names" >&2; fail=1; }
	ok_action="$(printf '%s\n' "$clear_names" | quiesce_decide refuse 0)"
	refuse_action="$(printf '%s\n' "$found" | quiesce_decide refuse 0)"
	warn_action="$(printf '%s\n' "$found" | quiesce_decide warn 0)"
	stop_action="$(printf '%s\n' "$found" | quiesce_decide refuse 1)"
	[[ "$ok_action" == "ok" ]] || { echo "decide ok $ok_action" >&2; fail=1; }
	[[ "$refuse_action" == "refuse com.other.debug" ]] || { echo "decide refuse $refuse_action" >&2; fail=1; }
	[[ "$warn_action" == "warn com.other.debug" ]] || { echo "decide warn $warn_action" >&2; fail=1; }
	[[ "$stop_action" == "stop com.other.debug" ]] || { echo "decide stop $stop_action" >&2; fail=1; }
	if [[ "$fail" -ne 0 ]]; then
		exit 1
	fi
	echo "guest-env self-test ok"
fi
