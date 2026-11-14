#!/usr/bin/env bash
# One local gate for Linux and macOS. bash 3.2.
# Runs the core, devx, packing, and iOS managed-host tests, cnative inspect
# on every sample screen and the counter, the bench self-tests, then a
# build-only Release publish of the showcase for android-x64 and android-arm64.
# Prints PASS or FAIL
# for each step. Exits 0 only when every step passed.
#
#   tools/check.sh
set -u
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
fail=0
pass=0

run() {
	name="$1"
	shift
	echo "== $name"
	if "$@"; then
		echo "PASS $name"
		pass=$((pass + 1))
	else
		echo "FAIL $name"
		fail=$((fail + 1))
	fi
}

run core dotnet test tests/NativePoc.Core.Tests/NativePoc.Core.Tests.csproj -c Release --nologo
run devx dotnet test tools/devx/NativePoc.Devx.Cli.Tests/NativePoc.Devx.Cli.Tests.csproj -c Release --nologo
run packing dotnet test tests/NativePoc.Packing.Tests/NativePoc.Packing.Tests.csproj -c Release --nologo
run ios dotnet test spikes/ios/NativePoc.iOS.Tests/NativePoc.iOS.Tests.csproj -c Release --nologo

cli="tools/devx/NativePoc.Devx.Cli/NativePoc.Devx.Cli.csproj"
dll="tools/devx/NativePoc.Devx.Cli/bin/Release/net10.0/cnative.dll"
run cli-build dotnet build "$cli" -c Release --nologo -v q
run catalog-check dotnet exec "$dll" catalog-check

inspect_samples() {
	local file status=0
	if [ ! -f "$dll" ]; then
		echo "cnative.dll was not built" >&2
		return 1
	fi
	while IFS= read -r file; do
		[ -n "$file" ] || continue
		echo "-- inspect $file"
		if ! dotnet exec "$dll" inspect "$file"; then
			echo "FAIL inspect $file" >&2
			status=1
		fi
	done < <(find samples src/NativePoc.Screen -name '*Screen.cs' -type f | sort)
	return "$status"
}

run inspect inspect_samples
run mac-bench bash tools/bench/mac-bench.sh --self-test
run scroll-bench bash tools/bench/scroll-bench.sh --self-test
run cold-start bash tools/bench/cold-start.sh --self-test
run guest-env bash tools/bench/guest-env.sh --self-test
run portable bash tools/bench/portable.sh --self-test
run first-frame python3 tools/bench/first-frame.py

publish_showcase() {
	rid="$1"
	echo "-- publish showcase $rid"
	dotnet publish src/NativePoc.Android/NativePoc.Android.csproj -c Release -f net10.0-android -r "$rid" \
		-p:NativePocShowcase=true -p:NativePocNav=true -p:NativePocPersist=true \
		-p:NativePocTransition=true -p:NativePocStartupMarks=true \
		-p:NativePocHttp=true \
		--nologo -nodeReuse:false
	apk="src/NativePoc.Android/bin/Release/net10.0-android/$rid/publish/com.scratch.nativepoc.showcase-Signed.apk"
	if [ ! -f "$apk" ]; then
		echo "missing $apk" >&2
		return 1
	fi
	bytes=$(wc -c < "$apk" | tr -d ' ')
	echo "showcase $rid $bytes $apk"
	if [ "$bytes" -lt 1000000 ]; then
		echo "showcase apk is too small" >&2
		return 1
	fi
	python3 tools/dex-has-provider.py "$apk"
}

publish_showcase_x64() {
	rid="android-x64"
	obj="src/NativePoc.Android/obj/Release/net10.0-android/$rid"
	publish_dir="src/NativePoc.Android/bin/Release/net10.0-android/$rid/publish"
	apk="$publish_dir/com.scratch.nativepoc.showcase-Signed.apk"
	clean="$obj/clean-meta.apk"
	manifest="$obj/android/AndroidManifest.xml"
	rm -rf "$obj/android/bin"
	rm -f "$publish_dir"/*.apk "$publish_dir"/*.aab "$publish_dir"/*.apks
	if ! publish_showcase "$rid"; then
		return 1
	fi
	cp "$apk" "$clean"
	if [ ! -f "$manifest" ]; then
		echo "missing $manifest" >&2
		return 1
	fi
	touch "$manifest"
	if ! publish_showcase "$rid"; then
		return 1
	fi
	python3 tools/meta-inf-same.py "$clean" "$apk"
}

run showcase-x64 publish_showcase_x64
run showcase-arm64 publish_showcase android-arm64

echo
echo "passed=$pass failed=$fail"
if [ "$fail" -eq 0 ]; then
	echo "check: pass"
	exit 0
fi
echo "check: fail"
exit 1
