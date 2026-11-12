#!/usr/bin/env bash
# One local gate for Linux and macOS. bash 3.2.
# Runs the core, devx, and packing tests, cnative inspect on every sample
# screen and the counter, then the bench self-tests. Prints PASS or FAIL
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

cli="tools/devx/NativePoc.Devx.Cli/NativePoc.Devx.Cli.csproj"
dll="tools/devx/NativePoc.Devx.Cli/bin/Release/net10.0/cnative.dll"
run cli-build dotnet build "$cli" -c Release --nologo -v q

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

echo
echo "passed=$pass failed=$fail"
if [ "$fail" -eq 0 ]; then
	echo "check: pass"
	exit 0
fi
echo "check: fail"
exit 1
