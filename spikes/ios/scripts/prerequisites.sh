#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname "$0")/../../.." && pwd)
. "$root/spikes/ios/scripts/require-mac.sh"
require_mac
ensure_dotnet

if ! command -v xcodebuild >/dev/null 2>&1; then
	echo "xcodebuild is missing. Install Xcode 26 or newer." >&2
	exit 1
fi

xcode_version=$(xcodebuild -version | awk 'NR==1 { print $2 }')
xcode_major=$(printf '%s' "$xcode_version" | cut -d. -f1)
echo "Xcode $xcode_version"
if [ "$xcode_major" -lt 26 ]; then
	echo "Xcode $xcode_version is below 26.0. .NET 10 for iOS requires Xcode 26 or newer." >&2
	exit 1
fi

echo "dotnet $(dotnet --version)"
cd "$root"
if ! dotnet workload list | awk 'NR > 1 { print $1 }' | grep -qx ios; then
	echo "The ios workload is not installed. On this Mac, run: dotnet workload install ios" >&2
	exit 1
fi

echo "prerequisites ok"
