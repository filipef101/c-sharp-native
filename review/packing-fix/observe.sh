#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/review/packing-fix"
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

last_field() {
	local key="$1"
	adb logcat -d -s NativePoc:I | rg -o "${key}=[0-9]+" | tail -1 | cut -d= -f2 || true
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
    if node.attrib.get("content-desc") != desc and node.attrib.get("text") != desc:
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

checked_for() {
	local desc="$1"
	local xml="$2"
	python3 - "$desc" "$xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
desc, path = sys.argv[1], sys.argv[2]
root = ET.parse(path).getroot()
for node in root.iter("node"):
    if node.attrib.get("content-desc") == desc:
        print(node.attrib.get("checked", ""))
        sys.exit(0)
sys.exit(2)
PY
}

require_layout() {
	local xml="$OUT/${LABEL}-ui.xml"
	dump_ui "$xml"
	python3 - "$xml" <<'PY' || fail "content overlaps the action bar"
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()

def bounds(node):
    parts = node.attrib.get("bounds", "").replace("][", ",").replace("[", "").replace("]", "").split(",")
    return tuple(int(p) for p in parts)

bar_bottom = None
content_top = None
switch_top = None
for node in root.iter("node"):
    if node.attrib.get("resource-id") == "android:id/action_bar_container":
        bar_bottom = bounds(node)[3]
    if node.attrib.get("text") == "1 of 4 packed" or node.attrib.get("text", "").endswith(" packed"):
        top = bounds(node)[1]
        if content_top is None or top < content_top:
            content_top = top
    if node.attrib.get("content-desc") == "Packed Canvas tent":
        switch_top = bounds(node)[1]
if bar_bottom is None or content_top is None or switch_top is None:
    print(f"missing bar={bar_bottom} content={content_top} switch={switch_top}")
    sys.exit(1)
print(f"layout bar-bottom={bar_bottom} progress-top={content_top} canvas-switch-top={switch_top}")
if content_top < bar_bottom or switch_top < bar_bottom:
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

wait_deliveries() {
	local want="$1"
	local i got
	for i in $(seq 1 25); do
		got="$(last_field switch-deliveries)"
		if [ "${got:-}" = "$want" ]; then
			return 0
		fi
		sleep 0.4
	done
	fail "switch-deliveries stayed ${got:-none}, wanted $want"
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

require_checked() {
	local desc="$1"
	local want="$2"
	dump_ui "$OUT/${LABEL}-ui.xml"
	local got
	got="$(checked_for "$desc" "$OUT/${LABEL}-ui.xml")" || fail "no switch $desc"
	if [ "$got" != "$want" ]; then
		fail "$desc checked=$got wanted $want"
	fi
}

type_name() {
	local before
	before="$(last_field field-deliveries)"
	before="${before:-0}"
	tap_desc "Item name"
	sleep 0.3
	adb shell input keycombination 113 29
	sleep 0.2
	adb shell input keyevent 67
	sleep 0.2
	adb shell input text "$1"
	sleep 0.6
	local i got
	for i in $(seq 1 20); do
		got="$(last_field field-deliveries)"
		if [ "${got:-0}" -gt "$before" ]; then
			return 0
		fi
		sleep 0.3
	done
	fail "field-deliveries stayed ${got:-0} while typing $1"
}

exercise() {
	LABEL="$1"
	adb shell input keyevent KEYCODE_WAKEUP >/dev/null || true
	adb shell wm dismiss-keyguard >/dev/null || true
	adb shell settings put system accelerometer_rotation 0 >/dev/null
	adb shell settings put system user_rotation 0 >/dev/null
	adb logcat -c
	adb shell am force-stop "$PKG" >/dev/null || true
	sleep 0.4
	adb shell am start -n "$PKG/$ACTIVITY" > "$OUT/${LABEL}-start.log"
	wait_count "packing-rendered route=List" 1
	wait_count "content-inset" 1
	sleep 0.5
	wait_count "switch-subscribe" 4
	require_text "1 of 4 packed"
	require_text "Canvas tent"
	require_text "Camp stove"
	require_layout
	adb exec-out screencap -p > "$OUT/${LABEL}-seed.png"
	local pid
	pid="$(adb shell pidof $PKG | tr -d '\r')"
	echo "$LABEL pid-seed=$pid deliveries=$(last_field switch-deliveries) subscribes=$(log_count 'switch-subscribe')" | tee -a "$OUT/pids.txt"

	tap_desc "Packed Canvas tent"
	wait_deliveries 1
	require_text "2 of 4 packed"
	require_checked "Packed Canvas tent" true
	adb exec-out screencap -p > "$OUT/${LABEL}-toggled.png"
	local haptic
	haptic="$(log_count 'haptic-')"
	echo "$LABEL toggle-on deliveries=$(last_field switch-deliveries) haptic-lines=$haptic" | tee -a "$OUT/steps.txt"
	if [ "$haptic" = "0" ]; then
		echo "$LABEL haptic API line absent on toggle" | tee -a "$OUT/steps.txt"
	fi

	local unsub
	unsub="$(log_count 'switch-unsubscribe')"
	tap_desc "Edit Canvas tent"
	wait_count "packing-rendered route=Editor" 1
	if [ "$(log_count 'switch-unsubscribe')" -le "$unsub" ]; then
		fail "editor visit did not unsubscribe list switches"
	fi
	require_text "Edit item"
	tap_desc "Back"
	wait_count "packing-rendered route=List" 2
	require_text "Canvas tent"
	require_text "2 of 4 packed"
	require_checked "Packed Canvas tent" true
	if [ "$(last_field switch-deliveries)" != "1" ]; then
		fail "route visit changed switch-deliveries to $(last_field switch-deliveries)"
	fi

	tap_desc "Packed Canvas tent"
	wait_deliveries 2
	require_text "1 of 4 packed"
	require_checked "Packed Canvas tent" false
	echo "$LABEL toggle-off deliveries=$(last_field switch-deliveries)" | tee -a "$OUT/steps.txt"

	tap_desc "Add item"
	wait_count "packing-rendered route=Editor" 2
	require_text "New item"
	type_name Lantern
	tap_desc "Save item"
	wait_count "packing-rendered route=List" 3
	require_text "Lantern"
	require_text "1 of 5 packed"
	forbid_text "New item"
	adb exec-out screencap -p > "$OUT/${LABEL}-added.png"

	tap_desc "Edit Ridge map"
	wait_count "packing-rendered route=Editor" 3
	type_name Nope
	tap_desc "Back"
	wait_count "packing-rendered route=List" 4
	require_text "Ridge map"
	forbid_text "Nope"
	require_text "Lantern"

	local lists starts disposed
	lists="$(log_count 'packing-rendered route=List')"
	starts="$(log_count 'packing-start')"
	disposed="$(log_count 'native-services-disposed')"
	tap_desc "Edit Ridge map"
	wait_count "packing-rendered route=Editor" 4
	sleep 0.8
	adb shell input keyevent 4
	sleep 0.6
	dump_ui "$OUT/${LABEL}-ui.xml"
	if has_text "$OUT/${LABEL}-ui.xml" "Edit item"; then
		adb shell input keyevent 4
	fi
	wait_count "packing-rendered route=List" $((lists + 1))
	require_text "Ridge map"
	forbid_text "Nope"
	require_text "Lantern"
	if [ "$(log_count 'native-services-disposed')" != "$disposed" ]; then
		fail "system back from editor disposed services"
	fi
	if [ "$(log_count 'packing-start')" != "$starts" ]; then
		fail "system back from editor restarted the activity"
	fi
	if [ "$(adb shell pidof $PKG | tr -d '\r')" != "$pid" ]; then
		fail "system back from editor changed pid"
	fi
	adb exec-out screencap -p > "$OUT/${LABEL}-system-back.png"
	echo "$LABEL system-back-editor stayed pid=$pid disposed=$disposed" | tee -a "$OUT/steps.txt"

	tap_desc "Copy list"
	wait_count "clipboard-ok" 1
	local clip
	clip="$(adb logcat -d -s NativePoc:I | rg "clipboard-ok" | tail -1 || true)"
	printf '%s\n' "$clip" > "$OUT/${LABEL}-clipboard.txt"
	adb shell cmd clipboard get >> "$OUT/${LABEL}-clipboard.txt" 2>&1 || true
	adb exec-out screencap -p > "$OUT/${LABEL}-copied.png"
	echo "$LABEL $clip" | tee -a "$OUT/steps.txt"
	printf '%s\n' "$clip" | rg -F "text=Canvas tent|Camp stove [packed]|Ridge map|Water bladder|Lantern" >/dev/null || fail "clipboard text was not the list: $clip"

	starts="$(log_count 'packing-start')"
	disposed="$(log_count 'native-services-disposed')"
	adb shell settings put system user_rotation 1 >/dev/null
	wait_count "native-services-disposed" $((disposed + 1))
	wait_count "packing-start" $((starts + 1))
	wait_count "content-inset" 2
	sleep 0.5
	require_layout
	require_text "1 of 5 packed"
	local rotated
	rotated="$(adb logcat -d -s NativePoc:I | rg 'packing-rendered' | tail -1 || true)"
	echo "$LABEL rotated-row $rotated" | tee -a "$OUT/steps.txt"
	case "$rotated" in
		*"total=5"*"field-deliveries=12"*) ;;
		*) fail "rotation dropped the added item: $rotated" ;;
	esac
	require_checked "Packed Canvas tent" false
	require_checked "Packed Camp stove" true
	adb exec-out screencap -p > "$OUT/${LABEL}-rotated.png"
	local pid_rot
	pid_rot="$(adb shell pidof $PKG | tr -d '\r')"
	echo "$LABEL pid-rotated=$pid_rot" | tee -a "$OUT/pids.txt"
	if [ "$pid_rot" != "$pid" ]; then
		fail "rotation changed pid"
	fi

	local before
	before="$(last_field switch-deliveries)"
	tap_desc "Packed Canvas tent"
	wait_deliveries $((before + 1))
	require_text "2 of 5 packed"
	require_checked "Packed Canvas tent" true
	echo "$LABEL toggle-after-rotation deliveries=$(last_field switch-deliveries) was=$before" | tee -a "$OUT/steps.txt"

	starts="$(log_count 'packing-start')"
	disposed="$(log_count 'native-services-disposed')"
	adb shell settings put system user_rotation 0 >/dev/null
	wait_count "packing-start" $((starts + 1))
	require_text "Lantern"
	require_text "2 of 5 packed"

	unsub="$(log_count 'switch-unsubscribe')"
	disposed="$(log_count 'native-services-disposed')"
	adb shell input keyevent 4
	wait_count "native-services-disposed" $((disposed + 1))
	if [ "$(log_count 'switch-unsubscribe')" -le "$unsub" ]; then
		fail "teardown did not unsubscribe"
	fi
	echo "$LABEL teardown unsub=$(log_count 'switch-unsubscribe') was=$unsub disposed=$(log_count 'native-services-disposed')" | tee -a "$OUT/steps.txt"

	starts="$(log_count 'packing-start')"
	adb shell am start -n "$PKG/$ACTIVITY" >/dev/null
	wait_count "packing-start" $((starts + 1))
	local pid_re
	pid_re="$(adb shell pidof $PKG | tr -d '\r')"
	echo "$LABEL pid-relaunch=$pid_re" | tee -a "$OUT/pids.txt"
	if [ "$pid_re" = "$pid" ]; then
		require_text "Lantern"
		require_text "2 of 5 packed"
		echo "$LABEL relaunch kept process state" | tee -a "$OUT/steps.txt"
	else
		require_text "1 of 4 packed"
		require_text "Canvas tent"
		forbid_text "Lantern"
		echo "$LABEL relaunch started a new process and the seed" | tee -a "$OUT/steps.txt"
	fi

	starts="$(log_count 'packing-start')"
	adb shell am force-stop "$PKG"
	sleep 0.4
	adb shell am start -n "$PKG/$ACTIVITY" >/dev/null
	wait_count "packing-start" $((starts + 1))
	require_text "1 of 4 packed"
	require_text "Canvas tent"
	require_text "Camp stove"
	require_checked "Packed Camp stove" true
	forbid_text "Lantern"
	adb exec-out screencap -p > "$OUT/${LABEL}-force-stop.png"
	echo "$LABEL pid-reset=$(adb shell pidof $PKG | tr -d '\r')" | tee -a "$OUT/pids.txt"
	save_logs
	echo "PASS $LABEL" | tee -a "$OUT/results.txt"
}

mode="${1:-all}"
if [ "$mode" != "release" ]; then
if [ "$mode" != "installed" ]; then
	echo "=== debug install ==="
	dotnet build "$PROJ" -c Debug -f net10.0-android -r android-x64 -t:Install -p:AdbTarget=-e --nologo > "$OUT/debug-install.log" 2>&1
fi
exercise debug
fi

echo "=== release install ==="
if [ ! -f "$RELEASE_APK" ]; then
	fail "release apk missing"
fi
adb uninstall "$PKG" >/dev/null || true
adb install -r "$RELEASE_APK" > "$OUT/release-install.log" 2>&1
exercise release
echo OBSERVE_OK
