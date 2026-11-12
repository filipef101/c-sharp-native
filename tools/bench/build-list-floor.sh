#!/usr/bin/env bash
# Java RecyclerView floor. One dex, no native code, so the same APK runs on
# x86_64 and arm64. The empty cold-start floor stays in tools/bench/floor.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$root/tools/bench/list-floor"
: "${ANDROID_HOME:?ANDROID_HOME is required}"
if [[ ! -d "$ANDROID_HOME/build-tools" ]]; then
	echo "build-tools not found under $ANDROID_HOME" >&2
	exit 1
fi
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

bash "$root/tools/bench/fetch-androidx.sh" >/dev/null
libs="${NATIVEPOC_AAR_CACHE:-$src/.cache}"
out="${1:-/tmp/nativepoc-bench/list-floor.apk}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$(dirname "$out")" "$work/classes" "$work/flat" "$work/gen"

unzip -q -o "$libs/recyclerview-1.2.1.aar" -d "$work/recyclerview"
unzip -q -o "$libs/core-1.3.2.aar" -d "$work/core"
unzip -q -o "$libs/customview-1.0.0.aar" -d "$work/customview"
"$bt/aapt2" compile --dir "$work/recyclerview/res" -o "$work/flat/recyclerview.zip"
"$bt/aapt2" compile --dir "$work/core/res" -o "$work/flat/core.zip"
"$bt/aapt2" link -I "$android_jar" --manifest "$src/AndroidManifest.xml" \
	-o "$work/base.apk" --java "$work/gen" \
	-R "$work/flat/recyclerview.zip" -R "$work/flat/core.zip" --auto-add-overlay

python3 "$src/gen_library_r.py" "$work/gen/com/scratch/listfloor/R.java" "$work/recyclerview/R.txt" androidx.recyclerview "$work/gen"
python3 "$src/gen_library_r.py" "$work/gen/com/scratch/listfloor/R.java" "$work/core/R.txt" androidx.core "$work/gen"

classpath="$android_jar:$work/recyclerview/classes.jar:$work/core/classes.jar:$work/customview/classes.jar:$libs/collection-1.0.0.jar:$libs/annotation-1.1.0.jar"
javac --release 8 -Xlint:-options -classpath "$classpath" -d "$work/classes" \
	"$src/src/ListFloorActivity.java" \
	"$work/gen/androidx/recyclerview/R.java" \
	"$work/gen/androidx/core/R.java"
classes=()
while IFS= read -r class; do
	classes+=("$class")
done < <(find "$work/classes" -name '*.class' | sort)
"$bt/d8" --min-api 24 --lib "$android_jar" --output "$work" \
	"${classes[@]}" \
	"$work/recyclerview/classes.jar" \
	"$work/core/classes.jar" \
	"$work/customview/classes.jar" \
	"$libs/collection-1.0.0.jar" \
	"$libs/annotation-1.1.0.jar"
if [[ ! -f "$work/classes.dex" ]]; then
	echo "d8 did not write classes.dex" >&2
	exit 1
fi
(cd "$work" && zip -q -j "$work/base.apk" classes.dex)
"$bt/zipalign" -f -p 4 "$work/base.apk" "$work/aligned.apk"
keystore="$work/debug.keystore"
keytool -genkeypair -keystore "$keystore" -storepass android -keypass android \
	-alias floor -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=floor" >/dev/null
"$bt/apksigner" sign --ks "$keystore" --ks-pass pass:android --key-pass pass:android \
	--ks-key-alias floor --out "$out" "$work/aligned.apk"
"$bt/apksigner" verify "$out" >/dev/null
echo "list-floor apk=$out bytes=$(wc -c < "$out" | tr -d ' ')"
