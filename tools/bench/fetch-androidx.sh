#!/usr/bin/env bash
# Download the pinned AndroidX jars into a gitignored cache.
# Each line of deps.sha256 is: sha256  filename  url
# A file already in the cache is kept only when its sha256 matches the pin.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
pin="$root/tools/bench/list-floor/deps.sha256"
cache="${NATIVEPOC_AAR_CACHE:-$root/tools/bench/list-floor/.cache}"
if [[ ! -f "$pin" ]]; then
	echo "Missing pin file $pin" >&2
	exit 1
fi
mkdir -p "$cache"
while IFS= read -r line || [[ -n "$line" ]]; do
	case "$line" in
		''|\#*) continue ;;
	esac
	sum="${line%% *}"
	rest="${line#* }"
	rest="${rest#"${rest%%[! ]*}"}"
	name="${rest%% *}"
	url="${rest#* }"
	url="${url#"${url%%[! ]*}"}"
	if [[ -z "$sum" || -z "$name" || -z "$url" || "$url" == "$name" ]]; then
		echo "Bad deps.sha256 line: $line" >&2
		exit 1
	fi
	dest="$cache/$name"
	if [[ -f "$dest" ]]; then
		got="$(sha256sum "$dest" | awk '{print $1}')"
		if [[ "$got" == "$sum" ]]; then
			continue
		fi
		echo "cached $name sha256 $got does not match pin $sum; fetching again" >&2
		rm -f "$dest"
	fi
	tmp="$(mktemp "$cache/.partial.XXXXXX")"
	if ! curl -fsSL --retry 3 --retry-delay 1 -o "$tmp" "$url"; then
		rm -f "$tmp"
		echo "fetch failed for $name from $url" >&2
		exit 1
	fi
	got="$(sha256sum "$tmp" | awk '{print $1}')"
	if [[ "$got" != "$sum" ]]; then
		rm -f "$tmp"
		echo "sha256 mismatch for $name: expected $sum got $got" >&2
		exit 1
	fi
	mv "$tmp" "$dest"
done <"$pin"
echo "androidx-cache=$cache"
