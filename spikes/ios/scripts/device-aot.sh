#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname "$0")/../../.." && pwd)
. "$root/spikes/ios/scripts/require-mac.sh"
require_mac
ensure_dotnet

mode=${1:-mono}
project="$root/spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj"
echo "This script compiles an ios-arm64 binary. It does not sign, install, or create an Apple account."
echo "EnableCodeSigning is false. A device launch stays unverified."
cd "$root"

case "$mode" in
	mono)
		echo "Release Mono build. UseInterpreter is false. Expect runtime class aot-only if a later signed run prints the flags."
		exec dotnet build "$project" \
			-c Release \
			-f net10.0-ios \
			-r ios-arm64 \
			-p:EnableCodeSigning=false \
			-p:UseInterpreter=false \
			-p:PublishAot=false
		;;
	native-aot)
		echo "NativeAOT publish. UseInterpreter has no effect in this mode."
		exec dotnet publish "$project" \
			-c Release \
			-f net10.0-ios \
			-r ios-arm64 \
			-p:EnableCodeSigning=false \
			-p:PublishAot=true
		;;
	*)
		echo "usage: device-aot.sh [mono|native-aot]" >&2
		exit 2
		;;
esac
