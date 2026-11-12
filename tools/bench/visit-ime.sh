#!/usr/bin/env bash
# Drive the visit form: IME next moves to the next field, the action button
# submits, the focused field stays above the keyboard, and an empty place is
# announced. The visit build must already be installed.
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

bounds() {
	local desc="$1"
	local adb_bin
	adb_bin="$(command -v adb)"
	# uiautomator dump can sit forever. macOS has no timeout(1); portable_timeout
	# uses it when it exists and otherwise kills the child from the shell.
	if ! portable_timeout 20 "$adb_bin" -s "$serial" shell uiautomator dump /sdcard/visit-ime.xml >/dev/null; then
		echo "uiautomator dump failed or passed 20s" >&2
		exit 1
	fi
	adb pull /sdcard/visit-ime.xml /tmp/visit-ime.xml >/dev/null
	python3 - "$desc" <<'PY'
import sys, xml.etree.ElementTree as ET
desc = sys.argv[1]
root = ET.parse("/tmp/visit-ime.xml").getroot()
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
    print((left + right) // 2, (top + bottom) // 2, bottom)
    sys.exit(0)
sys.exit(1)
PY
}

tap_field() {
	local desc="$1"
	local line
	line="$(bounds "$desc")"
	read -r x y bottom <<<"$line"
	adb shell input tap "$x" "$y" >/dev/null
	echo "tapped $desc x=$x y=$y bottom=$bottom"
}

adb shell ime enable com.google.android.inputmethod.latin/com.android.inputmethod.latin.LatinIME >/dev/null
adb shell ime set com.google.android.inputmethod.latin/com.android.inputmethod.latin.LatinIME >/dev/null
adb logcat -c
adb shell am force-stop com.scratch.nativepoc || true
adb shell am start -n com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity --es nativepoc_root visit >/tmp/visit-ime-start.txt
sleep 2
tap_field "When"
sleep 0.5
adb shell input keyevent 66
sleep 0.8
tap_field "Place"
sleep 0.4
adb shell input text Cafe
sleep 0.4
adb shell input keyevent 66
sleep 1
adb logcat -d -s NativePoc:I > /tmp/visit-ime-log.txt
python3 - <<'PY'
from pathlib import Path
text = Path("/tmp/visit-ime-log.txt").read_text(errors="replace")
needles = [
    "field-next from=place to=when",
    "field-submit key=when",
    "a11y-announce text=Place is required",
]
missing = [needle for needle in needles if needle not in text]
above = [line for line in text.splitlines() if line.startswith("field-scroll ") or " field-scroll " in line]
above = [line for line in above if "above=1" in line]
print(text)
if missing or not above:
    print("MISSING " + ", ".join(missing + ([] if above else ["field-scroll above=1"])))
    raise SystemExit(1)
print("visit-ime ok")
PY
