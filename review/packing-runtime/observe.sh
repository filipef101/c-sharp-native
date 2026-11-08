#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/review/packing-runtime"
mkdir -p "$OUT"

export JAVA_HOME="${JAVA_HOME:-/usr/lib/jvm/java-21-openjdk-amd64}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

PKG=com.scratch.nativepoc
ACTIVITY=nativepoc.PackingActivity
PROJ="$ROOT/src/NativePoc.Android/NativePoc.Android.csproj"
RELEASE_APK="$ROOT/src/NativePoc.Android/bin/Release/net10.0-android/android-x64/publish/${PKG}-Signed.apk"
LABEL=setup

boot="$(adb shell getprop sys.boot_completed | tr -d '\r')"
if [ "$boot" != "1" ]; then
	echo "boot_completed=$boot" | tee "$OUT/boot-failed.txt"
	exit 2
fi
echo 1 > "$OUT/boot-completed.txt"

log_count() {
	local n
	n="$(adb logcat -d -s NativePoc:I | rg -c "$1" || true)"
	echo "${n:-0}"
}

last_deliveries() {
	adb logcat -d -s NativePoc:I | rg -o "switch-deliveries=[0-9]+" | tail -1 | cut -d= -f2 || true
}

save_logs() {
	adb logcat -d -s NativePoc:I > "$OUT/${LABEL}-nativepoc.log" || true
	adb logcat -d -s AndroidRuntime:E > "$OUT/${LABEL}-runtime.log" || true
}

fail() {
	echo "FAIL $LABEL: $*" | tee -a "$OUT/failures.txt"
	save_logs
	adb exec-out screencap -p > "$OUT/${LABEL}-fail.png" || true
	exit 1
}

dump_ui() {
	local dest="$1"
	local i
	for i in $(seq 1 8); do
		adb shell uiautomator dump /sdcard/uidump.xml >/dev/null 2>&1 || true
		adb shell cat /sdcard/uidump.xml > "$dest" 2>/dev/null || true
		if python3 -c 'import sys,xml.etree.ElementTree as ET; ET.parse(sys.argv[1])' "$dest" 2>/dev/null; then
			return 0
		fi
		sleep 0.4
	done
	fail "ui dump did not parse"
}

bounds_for() {
	local desc="$1"
	local xml="$2"
	python3 - "$desc" "$xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
desc, path = sys.argv[1], sys.argv[2]
root = ET.parse(path).getroot()
for node in root.iter("node"):
    if node.attrib.get("content-desc") != desc:
        continue
    parts = node.attrib.get("bounds", "").replace("][", ",").replace("[", "").replace("]", "").split(",")
    if len(parts) == 4:
        print(*parts)
        sys.exit(0)
sys.exit(2)
PY
}

has_text() {
	local xml="$1"
	local text="$2"
	python3 - "$xml" "$text" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
want = sys.argv[2]
for node in root.iter("node"):
    if node.attrib.get("text") == want:
        sys.exit(0)
sys.exit(1)
PY
}

tap_desc() {
	local desc="$1"
	local xml="$OUT/${LABEL}-ui.xml"
	dump_ui "$xml"
	local b
	b="$(bounds_for "$desc" "$xml")" || fail "no node $desc"
	read -r x1 y1 x2 y2 <<<"$b"
	adb shell input tap $(( (x1 + x2) / 2 )) $(( (y1 + y2) / 2 ))
}

wait_rendered() {
	local deliveries="$1"
	local i last
	for i in $(seq 1 25); do
		last="$(last_deliveries)"
		if [ "${last:-}" = "$deliveries" ]; then
			return 0
		fi
		if [ -n "${last:-}" ] && [ "$last" -gt "$deliveries" ]; then
			fail "switch-deliveries $last exceeded $deliveries"
		fi
		sleep 0.4
	done
	fail "switch-deliveries stayed at ${last:-none}, wanted $deliveries"
}

wait_count() {
	local needle="$1"
	local min="$2"
	local i n
	for i in $(seq 1 30); do
		n="$(log_count "$needle")"
		if [ "${n:-0}" -ge "$min" ]; then
			return 0
		fi
		sleep 0.4
	done
	fail "count $needle stayed $(log_count "$needle"), wanted >= $min"
}

require_text() {
	local text="$1"
	dump_ui "$OUT/${LABEL}-ui.xml"
	has_text "$OUT/${LABEL}-ui.xml" "$text" || fail "missing text: $text"
}

forbid_text() {
	local text="$1"
	dump_ui "$OUT/${LABEL}-ui.xml"
	if has_text "$OUT/${LABEL}-ui.xml" "$text"; then
		fail "unexpected text: $text"
	fi
}

clear_and_type() {
	adb shell input keyevent 61
	sleep 0.3
	adb shell input keycombination 113 29
	sleep 0.2
	adb shell input keyevent 67
	sleep 0.3
	adb shell input text "$1"
	sleep 0.8
}

exercise() {
	LABEL="$1"
	adb shell input keyevent KEYCODE_WAKEUP >/dev/null || true
	adb shell wm dismiss-keyguard >/dev/null || true
	adb shell settings put system accelerometer_rotation 0 >/dev/null
	adb shell settings put system user_rotation 0 >/dev/null
	adb logcat -c
	adb shell am force-stop "$PKG" >/dev/null || true
	sleep 0.5
	adb shell am start -n "$PKG/$ACTIVITY" > "$OUT/${LABEL}-start.log"
	wait_rendered 0
	require_text "1 of 4 packed"
	require_text "Canvas tent"
	require_text "Camp stove"
	adb exec-out screencap -p > "$OUT/${LABEL}-seed.png"
	echo "$LABEL pid-seed=$(adb shell pidof $PKG | tr -d '\r')" | tee -a "$OUT/pids.txt"

	record_switch_tap() {
		local desc="$1"
		local name="$2"
		dump_ui "$OUT/${LABEL}-ui.xml"
		local before after deliveries haptic
		before="$(python3 - "$OUT/${LABEL}-ui.xml" "$desc" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
for node in root.iter("node"):
    if node.attrib.get("content-desc") == sys.argv[2]:
        print(node.attrib.get("checked", ""))
        break
PY
)"
		deliveries="$(last_deliveries)"
		tap_desc "$desc"
		sleep 1
		dump_ui "$OUT/${LABEL}-ui.xml"
		after="$(python3 - "$OUT/${LABEL}-ui.xml" "$desc" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
for node in root.iter("node"):
    if node.attrib.get("content-desc") == sys.argv[2]:
        print(node.attrib.get("checked", ""))
        break
PY
)"
		haptic="$(log_count 'haptic-')"
		echo "$LABEL switch-tap $name before=$before after=$after deliveries=$(last_deliveries) was=$deliveries haptic-lines=$haptic" | tee -a "$OUT/switch-taps.txt"
	}

	record_switch_tap "Packed Canvas tent" "canvas"
	record_switch_tap "Packed Camp stove" "stove"
	adb exec-out screencap -p > "$OUT/${LABEL}-toggled.png"

	local unsub_before
	unsub_before="$(log_count 'switch-unsubscribe')"
	tap_desc "Edit Canvas tent"
	wait_count "packing-rendered route=Editor" 1
	echo "$LABEL unsubscribe-on-edit before=$unsub_before after=$(log_count 'switch-unsubscribe')" | tee -a "$OUT/switch-taps.txt"
	require_text "Edit item"
	adb exec-out screencap -p > "$OUT/${LABEL}-editor.png"
	clear_and_type 'Trail%stent'
	record_switch_tap "Packed" "editor"
	tap_desc "Save item"
	wait_count "packing-rendered route=List" 2
	require_text "Trail tent"
	forbid_text "Canvas tent"

	tap_desc "Add item"
	wait_count "packing-rendered route=Editor" 2
	require_text "New item"
	tap_desc "Back"
	wait_count "packing-rendered route=List" 3
	require_text "1 of 4 packed"
	forbid_text "New item"

	tap_desc "Edit Ridge map"
	wait_count "packing-rendered route=Editor" 3
	tap_desc "Back"
	wait_count "packing-rendered route=List" 4
	require_text "Ridge map"
	require_text "Trail tent"

	local lists disposed_now
	lists="$(log_count 'packing-rendered route=List')"
	disposed_now="$(log_count 'native-services-disposed')"
	tap_desc "Edit Ridge map"
	wait_count "packing-rendered route=Editor" 4
	adb shell input keyevent 4
	sleep 1
	echo "$LABEL system-back-from-editor list=$(log_count 'packing-rendered route=List') was=$lists disposed=$(log_count 'native-services-disposed') was=$disposed_now pid=$(adb shell pidof $PKG | tr -d '\r')" | tee -a "$OUT/switch-taps.txt"
	adb shell am start -n "$PKG/$ACTIVITY" >/dev/null
	sleep 0.8
	dump_ui "$OUT/${LABEL}-ui.xml"
	if has_text "$OUT/${LABEL}-ui.xml" "Edit item" || has_text "$OUT/${LABEL}-ui.xml" "New item"; then
		tap_desc "Back"
		wait_count "packing-rendered route=List" $((lists + 1))
	fi
	require_text "Trail tent"

	tap_desc "Copy list"
	wait_count "clipboard-ok" 1
	adb shell cmd clipboard get > "$OUT/${LABEL}-clipboard.txt" 2>&1 || true
	adb exec-out screencap -p > "$OUT/${LABEL}-copied.png"

	local starts disposed
	starts="$(log_count 'packing-start')"
	disposed="$(log_count 'native-services-disposed')"
	adb shell settings put system user_rotation 1 >/dev/null
	wait_count "native-services-disposed" $((disposed + 1))
	wait_count "packing-start" $((starts + 1))
	require_text "Trail tent"
	require_text "1 of 4 packed"
	adb exec-out screencap -p > "$OUT/${LABEL}-rotated.png"
	echo "$LABEL pid-rotated=$(adb shell pidof $PKG | tr -d '\r')" | tee -a "$OUT/pids.txt"

	starts="$(log_count 'packing-start')"
	disposed="$(log_count 'native-services-disposed')"
	adb shell settings put system user_rotation 0 >/dev/null
	wait_count "native-services-disposed" $((disposed + 1))
	wait_count "packing-start" $((starts + 1))
	require_text "Trail tent"

	disposed="$(log_count 'native-services-disposed')"
	unsub_before="$(log_count 'switch-unsubscribe')"
	local pid_before
	pid_before="$(adb shell pidof $PKG | tr -d '\r')"
	adb shell input keyevent 4
	wait_count "native-services-disposed" $((disposed + 1))
	echo "$LABEL unsubscribe-on-finish before=$unsub_before after=$(log_count 'switch-unsubscribe')" | tee -a "$OUT/switch-taps.txt"

	local starts_before
	starts_before="$(log_count 'packing-start')"
	adb shell am start -n "$PKG/$ACTIVITY" >/dev/null
	wait_count "packing-start" $((starts_before + 1))
	local pid_relaunch
	pid_relaunch="$(adb shell pidof $PKG | tr -d '\r')"
	echo "$LABEL pid-relaunch=$pid_relaunch" | tee -a "$OUT/pids.txt"
	if [ "$pid_relaunch" = "$pid_before" ]; then
		require_text "Trail tent"
	else
		require_text "1 of 4 packed"
		forbid_text "Trail tent"
	fi

	starts_before="$(log_count 'packing-start')"
	adb shell am force-stop "$PKG"
	sleep 0.5
	adb shell am start -n "$PKG/$ACTIVITY" >/dev/null
	wait_count "packing-start" $((starts_before + 1))
	require_text "1 of 4 packed"
	require_text "Camp stove"
	require_text "Canvas tent"
	forbid_text "Trail tent"
	adb exec-out screencap -p > "$OUT/${LABEL}-force-stop.png"
	echo "$LABEL pid-reset=$(adb shell pidof $PKG | tr -d '\r')" | tee -a "$OUT/pids.txt"
	save_logs
	echo "PASS $LABEL" | tee -a "$OUT/results.txt"
}

if [ "${1:-}" != "installed" ]; then
echo "=== debug install ==="
LABEL=debug
dotnet build "$PROJ" -c Debug -f net10.0-android -r android-x64 -t:Install -p:AdbTarget=-e --nologo > "$OUT/debug-install.log" 2>&1
fi
exercise debug

echo "=== release install ==="
LABEL=release
if [ ! -f "$RELEASE_APK" ]; then
	fail "release apk missing"
fi
adb uninstall "$PKG" >/dev/null || true
adb install -r "$RELEASE_APK" > "$OUT/release-install.log" 2>&1
exercise release
echo OBSERVE_OK
