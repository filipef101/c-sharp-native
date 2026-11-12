#!/usr/bin/env bash
# Pack NativePoc into a local folder. This does not push to nuget.org.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$root/artifacts/packages}"
mkdir -p "$out"
pack() {
	dotnet pack "$1" -c Release -o "$out" --nologo
}
pack "$root/src/NativePoc.Core/NativePoc.Core.csproj"
pack "$root/src/NativePoc.Screen/NativePoc.Screen.csproj"
pack "$root/samples/NativePoc.Packing/NativePoc.Packing.csproj"
pack "$root/samples/NativePoc.Library/NativePoc.Library.csproj"
pack "$root/src/NativePoc.ReloadGen/NativePoc.ReloadGen.csproj"
pack "$root/src/NativePoc.Host/NativePoc.Host.csproj"
pack "$root/tools/devx/NativePoc.Devx.Cli/NativePoc.Devx.Cli.csproj"
pack "$root/templates/cnative-app.csproj"
echo "packed to $out"
echo "not pushed to nuget.org"
