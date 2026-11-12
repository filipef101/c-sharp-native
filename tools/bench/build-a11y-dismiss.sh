#!/usr/bin/env bash
# Instrumentation APK that performs the feed Dismiss action. No third-party jars.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$root/tools/bench/a11y-dismiss"
: "${ANDROID_HOME:?ANDROID_HOME is required}"
bt="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
android_jar=""
for candidate in "$ANDROID_HOME/platforms/android-36" "$ANDROID_HOME/platforms/android-35" "$ANDROID_HOME/platforms/android-34"; do
	if [[ -f "$candidate/android.jar" ]]; then
		android_jar="$candidate/android.jar"
		break
	fi
done
if [[ -z "$android_jar" ]]; then
	echo "No android.jar under $ANDROID_HOME/platforms" >&2
	exit 1
fi
out="${1:-/tmp/nativepoc-bench/a11y-dismiss.apk}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$(dirname "$out")" "$work/classes" "$work/compiled"
"$bt/aapt2" compile -o "$work/compiled" "$src/res/xml/dismiss_service.xml"
"$bt/aapt2" link -I "$android_jar" --manifest "$src/AndroidManifest.xml" -o "$work/base.apk" "$work/compiled/"*.flat
javac --release 8 -Xlint:-options -classpath "$android_jar" -d "$work/classes" "$src/src/DismissProbe.java"
"$bt/d8" --min-api 24 --lib "$android_jar" --output "$work" "$work/classes/nativepoc/dismiss/DismissProbe.class"
(cd "$work" && zip -q -j "$work/base.apk" classes.dex)
"$bt/zipalign" -f -p 4 "$work/base.apk" "$work/aligned.apk"
# Same cert as the debug-signed feed, or instrumentation cannot bind to it.
# dotnet android signs with the Xamarin debug keystore when that file exists.
keystore="${HOME}/.android/debug.keystore"
xamarin="${HOME}/.local/share/Xamarin/Mono for Android/debug.keystore"
if [[ -f "$xamarin" ]]; then
	keystore="$xamarin"
fi
"$bt/apksigner" sign --ks "$keystore" --ks-pass pass:android --key-pass pass:android \
	--ks-key-alias androiddebugkey --out "$out" "$work/aligned.apk"
"$bt/apksigner" verify "$out" >/dev/null
echo "a11y-dismiss apk=$out bytes=$(wc -c < "$out" | tr -d ' ')"
