#!/bin/sh
set -e
cd "$(dirname "$0")/../../.."
test -f spikes/ios/NativePoc.iOS/UIKitHost.cs
test -f spikes/ios/NativePoc.iOS/ScreenController.cs
test -f spikes/ios/NativePoc.iOS/PlatformBridge.cs
test -f spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj
if grep -q 'NativePoc.iOS/NativePoc.iOS.csproj' NativePoc.slnx; then
	echo "The UIKit project is in the Linux solution." >&2
	exit 1
fi
echo "authored UIKitHost ScreenController PlatformBridge"
echo "tested IosSurface and record programs on net10.0"
echo "never-compiled net10.0-ios"
echo "authored-check ok"
