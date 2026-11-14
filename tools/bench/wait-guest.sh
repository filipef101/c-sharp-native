#!/usr/bin/env bash
# Wait until the guest 1-minute loadavg is below a limit, then print it.
#
#   tools/bench/wait-guest.sh --device emulator-5554 --below 2.0 --timeout 120
#   tools/bench/wait-guest.sh --bootstrap     # one sample per stdin line
#   tools/bench/wait-guest.sh --self-test
#
# stdout on success: guest_load=<load> waited_ms=<ms>
# stdout on timeout: the same line plus timeout=1, and the exit status is 1.
# --bootstrap prints one line:
#   bootstrap B=1000 seed=8946 rng=python-random p50=… p50_lo=… p50_hi=… p90=… p90_lo=… p90_hi=…
# Percentiles are Hyndman-Fan type 7. The interval is the 2.5 and 97.5
# percentiles of 1000 resamples. The generator is Python random.Random(8946).
set -euo pipefail

below="2.0"
timeout_s=120
device=""
self_test=0
bootstrap=0
poll_s=2

usage() {
	cat <<'EOF'
tools/bench/wait-guest.sh --device <serial> [--below 2.0] [--timeout 120]
tools/bench/wait-guest.sh --bootstrap
tools/bench/wait-guest.sh --self-test
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--device) device="${2:?}"; shift 2 ;;
		--below) below="${2:?}"; shift 2 ;;
		--timeout) timeout_s="${2:?}"; shift 2 ;;
		--bootstrap) bootstrap=1; shift ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

now_ms() {
	date +%s%3N
}

under_limit() {
	awk -v load="$1" -v limit="$2" 'BEGIN {
		if (load == "") exit 1
		exit !((load + 0) < (limit + 0))
	}'
}

bootstrap_ci() {
	python3 -c "$(cat <<'PY'
import random, sys
vals = []
for line in sys.stdin:
    line = line.strip()
    if line:
        vals.append(float(line))
if not vals:
    sys.exit(1)

def hf(xs, p):
    s = sorted(xs)
    n = len(s)
    h = 1 + (n - 1) * p
    lo = int(h)
    frac = h - lo
    hi = lo + 1 if frac > 0 else lo
    if lo < 1:
        lo = 1
    if hi < 1:
        hi = 1
    if lo > n:
        lo = n
    if hi > n:
        hi = n
    return s[lo - 1] + frac * (s[hi - 1] - s[lo - 1])

rng = random.Random(8946)
n = len(vals)
p50s = []
p90s = []
for _ in range(1000):
    sample = [vals[rng.randrange(n)] for _ in range(n)]
    p50s.append(hf(sample, 0.50))
    p90s.append(hf(sample, 0.90))
p50 = hf(vals, 0.50)
p90 = hf(vals, 0.90)
print(
    "bootstrap B=1000 seed=8946 rng=python-random"
    " p50=%.1f p50_lo=%.1f p50_hi=%.1f p90=%.1f p90_lo=%.1f p90_hi=%.1f"
    % (p50, hf(p50s, 0.025), hf(p50s, 0.975), p90, hf(p90s, 0.025), hf(p90s, 0.975))
)
PY
)"
}

if [[ "$bootstrap" -eq 1 ]]; then
	bootstrap_ci
	exit 0
fi

read_load() {
	if [[ -n "${WAIT_GUEST_FAKE+x}" ]]; then
		local first rest
		first="${WAIT_GUEST_FAKE%% *}"
		if [[ -z "$first" ]]; then
			LOAD_NOW="4.0"
			return
		fi
		if [[ "$WAIT_GUEST_FAKE" == *" "* ]]; then
			rest="${WAIT_GUEST_FAKE#* }"
		else
			rest=""
		fi
		WAIT_GUEST_FAKE="$rest"
		LOAD_NOW="$first"
		return
	fi
	LOAD_NOW="$(adb -s "$device" shell cat /proc/loadavg | tr -d '\r' | awk '{ print $1 }')"
}

wait_once() {
	local start_ms now waited load
	start_ms="$(now_ms)"
	local deadline=$(( $(date +%s) + timeout_s ))
	while true; do
		read_load
		load="$LOAD_NOW"
		now="$(date +%s)"
		if under_limit "$load" "$below"; then
			waited=$(( $(now_ms) - start_ms ))
			echo "guest_load=$load waited_ms=$waited"
			return 0
		fi
		if (( now >= deadline )); then
			waited=$(( $(now_ms) - start_ms ))
			echo "guest_load=$load waited_ms=$waited timeout=1"
			return 1
		fi
		sleep "$poll_s"
	done
}

if [[ "$self_test" -eq 1 ]]; then
	under_limit 1.9 2.0
	if under_limit 2.0 2.0; then
		echo "2.0 should not pass" >&2
		exit 1
	fi
	if under_limit 4.0 2.0; then
		echo "4.0 should not pass" >&2
		exit 1
	fi
	series="800 447 683 575 736 497 419 1059 871 816 842 444 485 463 444 449 474 400 462 406"
	line1="$(printf '%s\n' $series | bootstrap_ci)"
	line2="$(printf '%s\n' $series | bootstrap_ci)"
	[[ "$line1" == "$line2" ]] || { echo "bootstrap drifted" >&2; echo "$line1"; echo "$line2"; exit 1; }
	[[ "$line1" == *"p50=479.5"* ]] || { echo "p50 $line1" >&2; exit 1; }
	[[ "$line1" == *"p90=844.9"* ]] || { echo "p90 $line1" >&2; exit 1; }
	python3 - "$line1" <<'PY'
import sys
text = sys.argv[1]
parts = {}
for item in text.split():
    if "=" in item:
        key, value = item.split("=", 1)
        try:
            parts[key] = float(value)
        except ValueError:
            pass
assert parts["p50_lo"] <= parts["p50"] <= parts["p50_hi"], parts
assert parts["p90_lo"] <= parts["p90"] <= parts["p90_hi"], parts
assert parts["p50_lo"] < parts["p50_hi"]
PY
	WAIT_GUEST_FAKE="3.2 2.0 1.5"
	timeout_s=5
	poll_s=0
	got="$(wait_once)"
	[[ "$got" == guest_load=1.5* ]] || { echo "fake wait $got" >&2; exit 1; }
	WAIT_GUEST_FAKE="4.0"
	timeout_s=0
	if got="$(wait_once)"; then
		echo "timeout should fail: $got" >&2
		exit 1
	fi
	[[ "$got" == *"timeout=1"* ]] || { echo "timeout line $got" >&2; exit 1; }
	echo "self-test guest gate below 2.0 and bootstrap B=1000 seed 8946"
	exit 0
fi

if [[ -z "$device" ]]; then
	echo "--device is required" >&2
	exit 2
fi
if ! [[ "$timeout_s" =~ ^[0-9]+$ ]]; then
	echo "--timeout must be a whole number of seconds" >&2
	exit 2
fi

wait_once
