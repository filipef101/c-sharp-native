#!/usr/bin/env bash
# Build the empty Java floor APK. Both FloorActivity and the anonymous
# OnPreDrawListener (FloorActivity$1) go into d8. Output is not committed.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$root/tools/bench/floor"
out="$src/build"
: "${ANDROID_HOME:?ANDROID_HOME is required}"

if [[ ! -d "$ANDROID_HOME/build-tools" ]]; then
	echo "build-tools not found under $ANDROID_HOME" >&2
	exit 1
fi
bt="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
platform=""
for candidate in "$ANDROID_HOME/platforms/android-36" "$ANDROID_HOME/platforms/android-35" "$ANDROID_HOME/platforms/android-34"; do
	if [[ -f "$candidate/android.jar" ]]; then
		platform="$candidate"
		break
	fi
done
if [[ -z "$platform" ]]; then
	echo "No android.jar under $ANDROID_HOME/platforms" >&2
	exit 1
fi

rm -rf "$out"
mkdir -p "$out/classes"
javac --release 8 -Xlint:-options -classpath "$platform/android.jar" -d "$out/classes" "$src/src/FloorActivity.java"
classes=()
while IFS= read -r class; do
	classes+=("$class")
done < <(find "$out/classes" -name '*.class' | sort)
if [[ "${#classes[@]}" -lt 2 ]]; then
	echo "d8 needs FloorActivity and FloorActivity\$1; found ${#classes[@]}" >&2
	exit 1
fi
"$bt/d8" --min-api 24 --output "$out" "${classes[@]}"
if [[ ! -f "$out/classes.dex" ]]; then
	echo "d8 did not write classes.dex" >&2
	exit 1
fi

unsigned="$out/unsigned.apk"
aligned="$out/aligned.apk"
apk="$out/floor.apk"
"$bt/aapt" package -f -M "$src/AndroidManifest.xml" -I "$platform/android.jar" -F "$unsigned"
(cd "$out" && zip -q -j "$unsigned" classes.dex)
"$bt/zipalign" -f -p 4 "$unsigned" "$aligned"

keystore="$out/debug.keystore"
if [[ ! -f "$keystore" ]]; then
	keytool -genkeypair -keystore "$keystore" -storepass android -keypass android \
		-alias floor -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=floor" >/dev/null
fi
"$bt/apksigner" sign --ks "$keystore" --ks-pass pass:android --key-pass pass:android \
	--ks-key-alias floor --out "$apk" "$aligned"
"$bt/apksigner" verify "$apk" >/dev/null
bytes="$(wc -c < "$apk" | tr -d ' ')"
echo "floor apk $apk bytes=$bytes"
