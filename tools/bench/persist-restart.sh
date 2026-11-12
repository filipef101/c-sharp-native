#!/usr/bin/env bash
# Remember a visit, force-stop, and recall it. The visit Release with
# NativePocPersist must already be installed. bash 3.2.
set -euo pipefail
# shellcheck source=portable.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/portable.sh"
serial="${ANDROID_SERIAL:-emulator-5554}"
while [[ $# -gt 0 ]]; do
	case "$1" in
		--serial) serial="${2:?}"; shift 2 ;;
		*) echo "unknown arg $1" >&2; exit 2 ;;
	esac
done
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
adb() { command adb -s "$serial" "$@"; }
component="com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity"

bounds() {
	local desc="$1"
	local adb_bin try line
	adb_bin="$(command -v adb)"
	try=0
	while [[ "$try" -lt 8 ]]; do
		if portable_timeout 20 "$adb_bin" -s "$serial" shell uiautomator dump /sdcard/persist-restart.xml >/dev/null; then
			adb pull /sdcard/persist-restart.xml /tmp/persist-restart.xml >/dev/null
			if line="$(python3 - "$desc" <<'PY'
import sys, xml.etree.ElementTree as ET
desc = sys.argv[1]
root = ET.parse("/tmp/persist-restart.xml").getroot()
for node in root.iter("node"):
    text = node.attrib.get("text", "")
    label = node.attrib.get("content-desc", "")
    if desc not in (text, label):
        continue
    raw = node.attrib.get("bounds", "")
    parts = raw.replace("][", ",").replace("[", "").replace("]", "").split(",")
    if len(parts) != 4:
        continue
    left, top, right, bottom = (int(part) for part in parts)
    print((left + right) // 2, (top + bottom) // 2)
    sys.exit(0)
sys.exit(1)
PY
)"; then
				printf '%s\n' "$line"
				return 0
			fi
		fi
		try=$((try + 1))
		sleep 1
	done
	echo "did not find $desc" >&2
	return 1
}

tap() {
	local desc="$1"
	local line x y
	line="$(bounds "$desc")"
	read -r x y <<<"$line"
	adb shell input tap "$x" "$y" >/dev/null
	echo "tapped $desc x=$x y=$y"
}

launch() {
	adb shell am force-stop com.scratch.nativepoc >/dev/null 2>&1 || true
	adb logcat -c || true
	adb shell am start -n "$component" --es nativepoc_root visit >/tmp/persist-restart-start.txt
	sleep 2
}

launch
tap Remember
sleep 1
adb logcat -d -s NativePoc:I > /tmp/persist-restart-save.txt
if ! grep -q 'persist-put key=visit.place' /tmp/persist-restart-save.txt; then
	echo "persist-put was not logged" >&2
	exit 1
fi
if ! grep -q 'persist-record key=visit.stop id=Pier' /tmp/persist-restart-save.txt; then
	echo "persist-record was not logged" >&2
	exit 1
fi
echo "saved"
adb shell am force-stop com.scratch.nativepoc
sleep 1
launch
tap Recall
sleep 1
adb logcat -d -s NativePoc:I > /tmp/persist-restart-load.txt
if ! grep -q 'persist-get key=visit.place' /tmp/persist-restart-load.txt; then
	echo "persist-get was not logged" >&2
	exit 1
fi
if ! grep -q 'persist-record-load key=visit.stop id=Pier' /tmp/persist-restart-load.txt; then
	echo "persist-record-load was not logged" >&2
	exit 1
fi
if ! bounds Pier >/dev/null; then
	echo "Pier was not on screen after force-stop" >&2
	exit 1
fi
echo "persist-restart ok"
