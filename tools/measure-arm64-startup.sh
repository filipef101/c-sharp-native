#!/bin/sh
# Cold-start TotalTime for the four android-arm64 Release APKs.
#
# Place this script in the same directory as:
#   maui-baseline-android-arm64-release.apk
#   nativepoc-profiled-aot-android-arm64-release.apk
#   nativepoc-full-aot-android-arm64-release.apk
#   nativepoc-nativeaot-android-arm64-release.apk
#
# Usage, from that directory, with one device attached:
#   ./measure.sh
# More than one device: ADB_SERIAL=<serial> ./measure.sh
# ADB=/path/to/adb overrides the adb on PATH.
#
# Each series is n=20. No sample is dropped. A missing TotalTime, a
# LaunchState other than COLD, or a failed install stops the script.
# The command is `adb shell am start -W -S -n <package>/<activity>`.
# -S force-stops the package before the start, so each sample is a cold start.
#
# p50 and p90 are Hyndman-Fan type 7 (Excel PERCENTILE.INC).
# h = 1 + (n-1)*p, then the value is interpolated between the floor and
# ceiling of h in the sorted sample, 1-based. For n=20, p50 has h=10.5
# and is the mean of the 10th and 11th sorted TotalTime values. p90 has
# h=18.1 and is the 18th plus 0.1 times the gap to the 19th.
#
# The three NativePoc APKs share com.scratch.nativepoc. Each series
# installs its own APK before the 20 starts, which replaces the previous one.
# This script uses adb, sh, awk, sort, and sha256sum or shasum. It does not
# use dotnet.

set -eu

ADB=${ADB:-adb}
N=20

root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

sha256_file() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | awk '{ print $1 }'
	elif command -v shasum >/dev/null 2>&1; then
		shasum -a 256 "$1" | awk '{ print $1 }'
	else
		echo "sha256sum or shasum is required" >&2
		exit 1
	fi
}

adb_cmd() {
	if [ -n "${ADB_SERIAL:-}" ]; then
		"$ADB" -s "$ADB_SERIAL" "$@"
	else
		"$ADB" "$@"
	fi
}

require_device() {
	attached=$("$ADB" devices | tr -d '\r' | awk 'NR>1 && $2=="device" { print $1 }')
	count=$(printf '%s\n' "$attached" | awk 'NF { n++ } END { print n+0 }')
	if [ "$count" -eq 0 ]; then
		echo "no adb device in state device" >&2
		exit 1
	fi
	if [ "$count" -gt 1 ] && [ -z "${ADB_SERIAL:-}" ]; then
		echo "more than one device; set ADB_SERIAL" >&2
		printf '%s\n' "$attached" >&2
		exit 1
	fi
	if [ -n "${ADB_SERIAL:-}" ]; then
		found=0
		for serial in $attached; do
			if [ "$serial" = "$ADB_SERIAL" ]; then
				found=1
			fi
		done
		if [ "$found" -ne 1 ]; then
			echo "ADB_SERIAL $ADB_SERIAL is not in state device" >&2
			printf '%s\n' "$attached" >&2
			exit 1
		fi
	fi
}

one_total() {
	text=$1
	printf '%s\n' "$text" | awk '
		/^TotalTime:/ { count++; value = $2 }
		END {
			if (count != 1) exit 1
			if (value !~ /^[0-9]+$/) exit 1
			print value
		}
	'
}

one_launch_state() {
	text=$1
	printf '%s\n' "$text" | awk '
		/^LaunchState:/ { count++; value = $2 }
		END {
			if (count != 1) exit 1
			print value
		}
	'
}

percentile() {
	p_num=$1
	p_den=$2
	file=$3
	sort -n "$file" | awk -v p_num="$p_num" -v p_den="$p_den" '
		{ a[NR] = $1 + 0 }
		END {
			n = NR
			if (n < 1) exit 1
			num = p_den + (n - 1) * p_num
			lo = int(num / p_den)
			frac = num % p_den
			if (lo < 1) lo = 1
			hi = lo
			if (frac > 0) hi = lo + 1
			if (hi > n) hi = n
			value = a[lo] + (a[hi] - a[lo]) * frac / p_den
			printf "%.1f\n", value
		}
	'
}

measure_one() {
	apk_name=$1
	package=$2
	activity=$3
	want_sha=$4
	apk=$root/$apk_name
	component=$package/$activity
	if [ ! -f "$apk" ]; then
		echo "missing $apk" >&2
		exit 1
	fi
	have_sha=$(sha256_file "$apk") || exit 1
	if [ "$have_sha" != "$want_sha" ]; then
		echo "sha256 mismatch for $apk_name" >&2
		echo "have $have_sha" >&2
		echo "want $want_sha" >&2
		exit 1
	fi
	echo "== $apk_name =="
	echo "package: $package"
	echo "activity: $activity"
	status=0
	install_out=$(adb_cmd install -r -d "$apk") || status=$?
	if [ "$status" -ne 0 ]; then
		echo "install failed for $apk_name" >&2
		printf '%s\n' "$install_out" >&2
		exit 1
	fi
	samples=$(mktemp "${TMPDIR:-/tmp}/measure.XXXXXX") || exit 1
	i=1
	while [ "$i" -le "$N" ]; do
		status=0
		start_out=$(adb_cmd shell am start -W -S -n "$component") || status=$?
		start_out=$(printf '%s\n' "$start_out" | tr -d '\r')
		if [ "$status" -ne 0 ]; then
			echo "am start failed on sample $i of $apk_name" >&2
			printf '%s\n' "$start_out" >&2
			exit 1
		fi
		total=$(one_total "$start_out") || {
			echo "TotalTime missing on sample $i of $apk_name" >&2
			printf '%s\n' "$start_out" >&2
			exit 1
		}
		state=$(one_launch_state "$start_out") || {
			echo "LaunchState missing on sample $i of $apk_name" >&2
			printf '%s\n' "$start_out" >&2
			exit 1
		}
		if [ "$state" != "COLD" ]; then
			echo "sample $i of $apk_name is $state, expected COLD" >&2
			printf '%s\n' "$start_out" >&2
			exit 1
		fi
		printf '%s\n' "$total" >> "$samples"
		echo "sample $i/$N TotalTime $total"
		i=$((i + 1))
	done
	got=$(awk 'END { print NR+0 }' "$samples")
	if [ "$got" -ne "$N" ]; then
		echo "expected $N samples, got $got" >&2
		exit 1
	fi
	sorted=$(sort -n "$samples" | awk '{ printf "%s%s", sep, $1; sep=" " } END { printf "\n" }')
	min=$(sort -n "$samples" | awk 'NR==1 { print $1 }')
	max=$(sort -n "$samples" | awk 'END { print $1 }')
	p50=$(percentile 1 2 "$samples") || exit 1
	p90=$(percentile 9 10 "$samples") || exit 1
	echo "n: $got"
	echo "TotalTime: $(awk '{ printf "%s%s", sep, $1; sep=" " } END { printf "\n" }' "$samples")"
	echo "sorted: $sorted"
	echo "min: $min"
	echo "max: $max"
	echo "p50: $p50"
	echo "p90: $p90"
	rm -f "$samples"
}

require_device

measure_one \
	maui-baseline-android-arm64-release.apk \
	com.companyname.mauibaseline \
	crc64d72af147769f1039.MainActivity \
	72f71f151fb6f661329f22e09b26c70d1e48130872531157439dc189b475f8bc

measure_one \
	nativepoc-profiled-aot-android-arm64-release.apk \
	com.scratch.nativepoc \
	crc64ee53024f7ed0aa60.MainActivity \
	69a43923cc1b9106b8bc65e4246403be06cf1095c4e183976a307162bc267cb2

measure_one \
	nativepoc-full-aot-android-arm64-release.apk \
	com.scratch.nativepoc \
	crc64ee53024f7ed0aa60.MainActivity \
	28ead001f58aba1f001a3f4fb348283f14885ef970fe542840ddd8d88e631401

measure_one \
	nativepoc-nativeaot-android-arm64-release.apk \
	com.scratch.nativepoc \
	crc64ee53024f7ed0aa60.MainActivity \
	6d13987efec45db1a517e1f801f61ab25976f4cd01f1d2759e4d883515563739
