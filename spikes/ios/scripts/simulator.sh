#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname "$0")/../../.." && pwd)
. "$root/spikes/ios/scripts/require-mac.sh"
require_mac
ensure_dotnet

project="$root/spikes/ios/NativePoc.iOS/NativePoc.iOS.csproj"
echo "Debug run on the arm64 simulator. An x64 simulator result does not count."
echo "Pass log lines are NativePoc runtime IsDynamicCodeSupported=True IsDynamicCodeCompiled=False"
echo "sh spikes/ios/scripts/check-authored.sh --classify True False"
echo "That pair classifies as interpreter."
cd "$root"
exec dotnet run --project "$project" -c Debug -f net10.0-ios -r iossimulator-arm64
