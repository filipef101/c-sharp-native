#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname "$0")/../../.." && pwd)

classify_runtime() {
	supported=$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')
	compiled=$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')
	if [ "$supported" = "true" ] && [ "$compiled" = "false" ]; then
		printf '%s\n' interpreter
		return 0
	fi
	if [ "$supported" = "true" ] && [ "$compiled" = "true" ]; then
		printf '%s\n' jit
		return 0
	fi
	if [ "$supported" = "false" ] && [ "$compiled" = "false" ]; then
		printf '%s\n' aot-only
		return 0
	fi
	echo "unsupported runtime flags supported=$1 compiled=$2" >&2
	return 1
}

expect_class() {
	got=$(classify_runtime "$1" "$2")
	if [ "$got" != "$3" ]; then
		echo "classify $1 $2 returned $got, wanted $3" >&2
		exit 1
	fi
}

self_test() {
	expect_class true false interpreter
	expect_class True False interpreter
	expect_class true true jit
	expect_class TRUE TRUE jit
	expect_class false false aot-only
	expect_class False False aot-only
	if classify_runtime false true >/dev/null 2>&1; then
		echo "compiled=true with supported=false must fail" >&2
		exit 1
	fi
}

check_files() {
	csproj="$root/spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj"
	ios="$root/spikes/ios/NativePoc.iOS"
	test -f "$csproj"
	test -f "$root/src/NativePoc.Core/NativePoc.Core.csproj"
	test -f "$root/src/NativePoc.Screen/NativePoc.Screen.csproj"
	grep -q '<TargetFramework>net10.0-ios</TargetFramework>' "$csproj"
	grep -q '<UseInterpreter>true</UseInterpreter>' "$csproj"
	grep -q '<UseInterpreter>false</UseInterpreter>' "$csproj"
	grep -q '<TrimMode>copy</TrimMode>' "$csproj"
	grep -q '<PublishAot>false</PublishAot>' "$csproj"
	grep -q 'NativePoc.Core.csproj' "$csproj"
	grep -q 'NativePoc.Screen.csproj' "$csproj"
	if grep -n 'MtouchInterpreter' "$csproj"; then
		echo "MtouchInterpreter overrides UseInterpreter" >&2
		exit 1
	fi
	if grep -n '<PublishAot>true</PublishAot>' "$csproj"; then
		echo "PublishAot must stay false in the spike project" >&2
		exit 1
	fi
	if grep -R -n --include='*.cs' --include='*.csproj' 'AssemblyLoadContext' "$root/spikes/ios"; then
		echo "the iOS spike must not load assemblies" >&2
		exit 1
	fi
	if grep -R -n -i --include='*.cs' --include='*.csproj' 'maui' "$root/spikes/ios"; then
		echo "the iOS spike must not reference MAUI" >&2
		exit 1
	fi
	if grep -n -i 'NativePoc.iOS' "$root/NativePoc.slnx"; then
		echo "NativePoc.slnx must not list the iOS spike" >&2
		exit 1
	fi
	for file in Main.cs AppDelegate.cs SceneDelegate.cs ScreenController.cs UIKitHost.cs PlatformBridge.cs HotReload.cs Info.plist; do
		test -f "$ios/$file"
	done
	grep -q 'Screen.Render' "$ios/ScreenController.cs"
	grep -q 'IElementHost<UIView>' "$ios/UIKitHost.cs"
	grep -q 'UIPasteboard' "$ios/PlatformBridge.cs"
	grep -q 'MetadataUpdater.IsSupported' "$ios/HotReload.cs"
	if git -C "$root" status --porcelain -- src samples tools README.md NativePoc.slnx global.json | grep -q .; then
		echo "Android-owned or repo-root files changed" >&2
		git -C "$root" status --porcelain -- src samples tools README.md NativePoc.slnx global.json >&2
		exit 1
	fi
}

if [ "${1:-}" = "--classify" ]; then
	classify_runtime "$2" "$3"
	exit $?
fi

self_test
check_files
printf '%s\n' "authored-check ok"
