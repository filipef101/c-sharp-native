#!/usr/bin/env bash
# Perform the feed row Dismiss custom action through an accessibility service, then Undo.
# Requires a feed APK that contains nativepoc.KeyedList and the feed root.
# Instrumentation inside the NativeAOT process never reaches onStart, so the
# probe is a separate service with canRetrieveWindowContent.
# A pixel_8 is arm64-v8a. If the given APK has no lib folder for the device
# ABI, a sibling whose name swaps android-x64 and android-arm64 is used.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
serial="${ANDROID_SERIAL:-emulator-5554}"
feed=""
while [[ $# -gt 0 ]]; do
	case "$1" in
		--serial) serial="${2:?}"; shift 2 ;;
		--apk) feed="${2:?}"; shift 2 ;;
		*) echo "unknown arg $1" >&2; exit 2 ;;
	esac
done
if [[ -z "$feed" || ! -f "$feed" ]]; then
	echo "a11y-dismiss.sh --apk <feed.apk> [--serial emulator-5554]" >&2
	exit 2
fi
feed="$(cd "$(dirname "$feed")" && pwd)/$(basename "$feed")"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
device_abi="$(adb -s "$serial" shell getprop ro.product.cpu.abi | tr -d '\r')"
case "$device_abi" in
	arm64-v8a) lib_dir="lib/arm64-v8a/" ; other="android-x64" ; mine="android-arm64" ;;
	x86_64) lib_dir="lib/x86_64/" ; other="android-arm64" ; mine="android-x64" ;;
	*) lib_dir="" ; other="" ; mine="" ;;
esac
if [[ -n "$lib_dir" ]] && ! python3 - "$feed" "$lib_dir" <<'PY'
import sys, zipfile
path, needle = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(path) as archive:
    sys.exit(0 if any(name.startswith(needle) for name in archive.namelist()) else 1)
PY
then
	base="$(basename "$feed")"
	alt="$(dirname "$feed")/${base/$other/$mine}"
	if [[ "$base" == *"$other"* && -f "$alt" ]] && python3 - "$alt" "$lib_dir" <<'PY'
import sys, zipfile
path, needle = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(path) as archive:
    sys.exit(0 if any(name.startswith(needle) for name in archive.namelist()) else 1)
PY
	then
		echo "a11y-dismiss: device $device_abi, using $alt"
		feed="$alt"
	else
		echo "a11y-dismiss: $feed has no $lib_dir and $serial is $device_abi" >&2
		exit 2
	fi
fi
probe="/tmp/nativepoc-bench/a11y-dismiss.apk"
bash "$root/tools/bench/build-a11y-dismiss.sh" "$probe"
adb -s "$serial" install -r -d "$feed" >/tmp/a11y-dismiss-feed-install.txt
adb -s "$serial" uninstall nativepoc.dismiss >/tmp/a11y-dismiss-probe-uninstall.txt 2>&1 || true
adb -s "$serial" install -r -d "$probe" >/tmp/a11y-dismiss-probe-install.txt
adb -s "$serial" shell am force-stop com.scratch.nativepoc || true
prev_services="$(adb -s "$serial" shell settings get secure enabled_accessibility_services | tr -d '\r')"
prev_enabled="$(adb -s "$serial" shell settings get secure accessibility_enabled | tr -d '\r')"
restore_a11y() {
	if [[ "$prev_services" == "null" || -z "$prev_services" ]]; then
		adb -s "$serial" shell settings delete secure enabled_accessibility_services >/dev/null 2>&1 || true
	else
		adb -s "$serial" shell settings put secure enabled_accessibility_services "$prev_services" >/dev/null
	fi
	if [[ "$prev_enabled" == "null" || -z "$prev_enabled" ]]; then
		adb -s "$serial" shell settings put secure accessibility_enabled 0 >/dev/null
	else
		adb -s "$serial" shell settings put secure accessibility_enabled "$prev_enabled" >/dev/null
	fi
}
trap restore_a11y EXIT
adb -s "$serial" shell settings put secure enabled_accessibility_services nativepoc.dismiss/nativepoc.dismiss.DismissProbe >/dev/null
adb -s "$serial" shell settings put secure accessibility_enabled 1 >/dev/null
adb -s "$serial" logcat -c
adb -s "$serial" shell am start -W -n com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity --es nativepoc_root feed > /tmp/a11y-dismiss-start.txt
for _ in $(seq 1 30); do
	if adb -s "$serial" logcat -d -s NativePoc:I | grep -q 'a11y-probe performed='; then
		break
	fi
	sleep 0.5
done
adb -s "$serial" logcat -d -s NativePoc:I > /tmp/a11y-dismiss-logcat.txt
echo "---- log ----"
grep -E 'a11y-probe|a11y-dismiss|a11y-announce|a11y-undo|list-dismiss|list-undo' /tmp/a11y-dismiss-logcat.txt || true
probe_line=$(grep 'a11y-probe performed=' /tmp/a11y-dismiss-logcat.txt | tail -1 || true)
ok=1
for need in 'performed=1' 'gone=1' 'next=1' 'undone=1' 'restored=1'; do
	if [[ "$probe_line" != *"$need"* ]]; then
		echo "missing $need in: $probe_line" >&2
		ok=0
	fi
done
if ! grep -q 'a11y-dismiss pos=' /tmp/a11y-dismiss-logcat.txt; then
	echo "missing a11y-dismiss pos=" >&2
	ok=0
fi
if ! grep -q 'list-dismiss pos=' /tmp/a11y-dismiss-logcat.txt; then
	echo "missing list-dismiss pos=" >&2
	ok=0
fi
if ! grep -q 'a11y-announce text=Dismissed' /tmp/a11y-dismiss-logcat.txt; then
	echo "missing dismiss announcement" >&2
	ok=0
fi
if ! grep -q 'list-undo' /tmp/a11y-dismiss-logcat.txt; then
	echo "missing list-undo" >&2
	ok=0
fi
if [[ "$probe_line" != *'before=• Row 0000>• Row 0001'* ]]; then
	echo "focus order before the dismiss is not row 0000 then 0001" >&2
	ok=0
fi
if [[ "$probe_line" != *'after=• Row 0001>• Row 0002'* ]]; then
	echo "focus order after the dismiss is not row 0001 then 0002" >&2
	ok=0
fi
if [[ "$probe_line" == *'after=• Row 0000'* ]]; then
	echo "dismissed row still leads the list" >&2
	ok=0
fi
if [[ "$probe_line" != *'back=• Row 0000>• Row 0001'* ]]; then
	echo "focus order after undo is not row 0000 then 0001" >&2
	ok=0
fi
if [[ "$ok" != 1 ]]; then
	exit 1
fi
echo "a11y-dismiss: ok"
