#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EVIDENCE="$ROOT/docs/compatibility/evidence"
ARTIFACTS="$ROOT/docs/compatibility/artifacts"
mkdir -p "$EVIDENCE" "$ARTIFACTS"

export JAVA_HOME="${JAVA_HOME:-/usr/lib/jvm/java-21-openjdk-amd64}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

PKG=com.scratch.sparklineprobe
PROJ="$ROOT/compatibility/Sparkline.Probe/Sparkline.Probe.csproj"
RELEASE_APK="$ROOT/compatibility/Sparkline.Probe/bin/Release/net10.0-android/android-x64/publish/${PKG}-Signed.apk"

boot="$(adb shell getprop sys.boot_completed | tr -d '\r')"
if [ "$boot" != "1" ]; then
	echo "emulator boot_completed=$boot" >&2
	exit 1
fi
adb shell input keyevent KEYCODE_WAKEUP >/dev/null
adb shell wm dismiss-keyguard >/dev/null || true
adb logcat -G 16M >/dev/null || true

bounds_for() {
	local desc="$1"
	local i
	for i in $(seq 1 12); do
		adb shell uiautomator dump /sdcard/uidump.xml >/dev/null 2>&1 || true
		adb shell cat /sdcard/uidump.xml > "$EVIDENCE/uidump.xml" 2>/dev/null || true
		if python3 - "$desc" "$EVIDENCE/uidump.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
desc, path = sys.argv[1], sys.argv[2]
try:
    root = ET.parse(path).getroot()
except ET.ParseError:
    sys.exit(2)
for node in root.iter("node"):
    if node.attrib.get("content-desc") != desc:
        continue
    bounds = node.attrib.get("bounds", "")
    parts = bounds.replace("][", ",").replace("[", "").replace("]", "").split(",")
    if len(parts) != 4:
        continue
    print(*parts)
    sys.exit(0)
sys.exit(2)
PY
		then
			return 0
		fi
		sleep 0.5
	done
	echo "no bounds for $desc" >&2
	return 1
}

slow_scrub() {
	local x1="$1" y1="$2" x2="$3" y2="$4" shot="${5:-}"
	adb shell input motionevent DOWN "$x1" "$y1"
	sleep 0.7
	adb shell input motionevent MOVE "$x1" "$y1"
	sleep 0.25
	adb shell input motionevent MOVE "$x2" "$y2"
	sleep 0.45
	if [ -n "$shot" ]; then
		adb exec-out screencap -p > "$shot"
	fi
	adb shell input motionevent UP "$x2" "$y2"
}

log_count() {
	adb logcat -d -s SparkProbe:I | rg -c "$1" || true
}

wait_count() {
	local needle="$1"
	local min="$2"
	local tries="${3:-30}"
	local i n
	for i in $(seq 1 "$tries"); do
		n="$(log_count "$needle")"
		if [ "${n:-0}" -ge "$min" ]; then
			return 0
		fi
		sleep 0.5
	done
	echo "count $needle stayed at $(log_count "$needle"), wanted >= $min" >&2
	adb logcat -d -s SparkProbe:I AndroidRuntime:E | tail -100 >&2
	return 1
}

exercise() {
	local label="$1"
	wait_count "create id=" 1 40
	sleep 1
	local spark recreate unsubscribe
	spark="$(bounds_for sparkline)"
	recreate="$(bounds_for recreate)"
	echo "$label spark $spark" | tee -a "$EVIDENCE/coords.txt"
	echo "$label recreate $recreate" | tee -a "$EVIDENCE/coords.txt"
	read -r sx1 sy1 sx2 sy2 <<<"$spark"
	local midy=$(( (sy1 + sy2) / 2 ))
	local x1=$(( sx1 + (sx2 - sx1) / 5 ))
	local x2=$(( sx1 + (sx2 - sx1) * 4 / 5 ))
	local emits_before raw_before
	emits_before="$(log_count 'emit index=')"
	raw_before="$(log_count 'raw index=')"
	slow_scrub "$x1" "$midy" "$x2" "$midy" "$EVIDENCE/${label}-scrub.png"
	wait_count "emit index=" $((emits_before + 1)) 20
	local creates destroys
	creates="$(log_count 'create id=')"
	destroys="$(log_count 'destroy id=')"
	read -r rx1 ry1 rx2 ry2 <<<"$recreate"
	adb shell input tap $(( (rx1 + rx2) / 2 )) $(( (ry1 + ry2) / 2 ))
	wait_count "destroy id=" $((destroys + 1)) 20
	wait_count "create id=" $((creates + 1)) 20
	sleep 1
	spark="$(bounds_for sparkline)"
	read -r sx1 sy1 sx2 sy2 <<<"$spark"
	midy=$(( (sy1 + sy2) / 2 ))
	x1=$(( sx1 + (sx2 - sx1) / 5 ))
	x2=$(( sx1 + (sx2 - sx1) * 4 / 5 ))
	emits_before="$(log_count 'emit index=')"
	slow_scrub "$x1" "$midy" "$x2" "$midy"
	wait_count "emit index=" $((emits_before + 1)) 20
	unsubscribe="$(bounds_for unsubscribe)"
	read -r ux1 uy1 ux2 uy2 <<<"$unsubscribe"
	adb shell input tap $(( (ux1 + ux2) / 2 )) $(( (uy1 + uy2) / 2 ))
	wait_count "unsubscribe id=" 1 20
	adb exec-out screencap -p > "$EVIDENCE/${label}-unsubscribed.png"
	local before after
	before="$(log_count 'raw index=')"
	slow_scrub "$x1" "$midy" "$x2" "$midy"
	sleep 1
	after="$(log_count 'raw index=')"
	echo "$label raw-index-lines before=$before after=$after scrub-raw-start=$raw_before" | tee -a "$EVIDENCE/coords.txt"
	if [ "$before" != "$after" ]; then
		echo "$label still received raw scrubs after unsubscribe" >&2
		return 1
	fi
	adb logcat -d -s SparkProbe:I > "$EVIDENCE/${label}-sparkprobe.log"
	adb logcat -d AndroidRuntime:E > "$EVIDENCE/${label}-runtime.log" || true
}

if [ "${1:-}" != "release-only" ]; then
echo "=== debug run ==="
adb uninstall "$PKG" >/dev/null || true
adb logcat -c
dotnet build "$PROJ" -c Debug -f net10.0-android -r android-x64 -t:Run -p:AdbTarget=-e --nologo > "$EVIDENCE/debug-run.log" 2>&1
exercise debug
fi

echo "=== release install ==="
adb uninstall "$PKG" >/dev/null || true
adb install -r "$RELEASE_APK" > "$EVIDENCE/release-install.log" 2>&1
adb logcat -c
COMPONENT="$(adb shell cmd package resolve-activity --brief "$PKG" | tr -d '\r' | tail -1)"
echo "component=$COMPONENT" | tee "$EVIDENCE/release-component.txt"
adb shell am start -n "$COMPONENT" > "$EVIDENCE/release-start.log" 2>&1
exercise release

cp "$RELEASE_APK" "$ARTIFACTS/sparkline-probe-nativeaot-android-x64.apk"
sha256sum "$RELEASE_APK" | tee "$ARTIFACTS/sparkline-probe-nativeaot-android-x64.apk.sha256"
unzip -l "$RELEASE_APK" | awk '/lib\/|classes.dex/' > "$EVIDENCE/release-apk-libs.txt"
echo OBSERVE_OK
