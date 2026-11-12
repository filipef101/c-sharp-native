#!/bin/sh
# Skip ILC when sources, package assets, and publish flags are unchanged.
# check exits 0 either way. A match touches the output files so the
# incremental ILLink / ILC / link targets see a fresh output. write stores
# the hash of those inputs next to the object file.
set -eu

mode=${1:?}
stamp=${2:?}
flags=${3:?}
list=${4:?}
shift 4

if ! command -v sha256sum >/dev/null 2>&1 && ! command -v shasum >/dev/null 2>&1; then
	echo "ilc-stamp: no sha256 tool" >&2
	exit 1
fi

digest() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum
	else
		shasum -a 256
	fi
}

file_digest() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1"
	else
		shasum -a 256 "$1"
	fi
}

hash_inputs() {
	{
		echo "flags"
		cat "$flags"
		echo "files"
		if [ -f "$list" ]; then
			sort -u "$list" | while IFS= read -r path; do
				[ -n "$path" ] || continue
				if [ -f "$path" ]; then
					file_digest "$path"
				else
					printf 'missing %s\n' "$path"
				fi
			done
		fi
	} | digest | awk '{print $1}'
}

hash=$(hash_inputs)

if [ "$mode" = "write" ]; then
	mkdir -p "$(dirname "$stamp")"
	if [ -f "$stamp" ] && [ "$(cat "$stamp")" = "$hash" ]; then
		echo "ilc-stamp: stamp unchanged"
		exit 0
	fi
	printf '%s\n' "$hash" > "$stamp"
	echo "ilc-stamp: wrote stamp"
	exit 0
fi

if [ "$mode" != "check" ]; then
	echo "ilc-stamp: unknown mode $mode" >&2
	exit 1
fi

if [ -f "$stamp" ] && [ "$(cat "$stamp")" = "$hash" ]; then
	for output in "$@"; do
		if [ -n "$output" ] && [ -e "$output" ]; then
			touch "$output"
		fi
	done
	echo "ilc-stamp: skip"
	exit 0
fi

echo "ilc-stamp: compile"
exit 0
