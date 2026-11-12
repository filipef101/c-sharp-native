#!/usr/bin/env bash
# Helpers the bench scripts share. bash 3.2. No associative arrays.
# sha256 uses sha256sum or shasum. A time bound does not call timeout(1),
# which macOS does not ship. Source this file; --self-test runs the checks.
#
#   tools/bench/portable.sh --self-test

portable_sha256() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | awk '{ print $1 }'
		return
	fi
	if command -v shasum >/dev/null 2>&1; then
		shasum -a 256 "$1" | awk '{ print $1 }'
		return
	fi
	echo "sha256sum or shasum is required" >&2
	return 1
}

# portable_timeout SECONDS COMMAND...
# COMMAND must be an executable, not a shell function or builtin.
# Exit 124 when the bound is reached.
portable_timeout() {
	local seconds="$1"
	shift
	if command -v timeout >/dev/null 2>&1; then
		timeout "$seconds" "$@"
		return
	fi
	if command -v gtimeout >/dev/null 2>&1; then
		gtimeout "$seconds" "$@"
		return
	fi
	"$@" &
	local pid=$!
	local waited=0
	while kill -0 "$pid" 2>/dev/null; do
		if [ "$waited" -ge "$seconds" ]; then
			kill "$pid" 2>/dev/null || true
			wait "$pid" 2>/dev/null || true
			return 124
		fi
		sleep 1
		waited=$((waited + 1))
	done
	wait "$pid"
}

# installed_remember FILE PKG APK
# Prints new, same, or conflict. A new pair is appended as pkg<TAB>apk.
installed_remember() {
	local file="$1"
	local pkg="$2"
	local apk="$3"
	local prev
	prev="$(awk -F '\t' -v pkg="$pkg" '$1 == pkg { print $2; exit }' "$file")"
	if [ -n "$prev" ]; then
		if [ "$prev" != "$apk" ]; then
			echo conflict
			return 0
		fi
		echo same
		return 0
	fi
	printf '%s\t%s\n' "$pkg" "$apk" >>"$file"
	echo new
}

case "$0" in
	*portable.sh) _portable_main=1 ;;
	*) _portable_main=0 ;;
esac
if [ "$_portable_main" -eq 1 ] && [ "${1:-}" = "--self-test" ]; then
	set -euo pipefail
	fail=0
	dir="$(mktemp -d)"
	printf 'hello\n' >"$dir/hello"
	got="$(portable_sha256 "$dir/hello")"
	want="5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03"
	if [ "$got" != "$want" ]; then
		echo "sha256 $got" >&2
		fail=1
	fi
	: >"$dir/installed.tsv"
	[ "$(installed_remember "$dir/installed.tsv" com.example /tmp/a.apk)" = new ] || { echo "remember new" >&2; fail=1; }
	[ "$(installed_remember "$dir/installed.tsv" com.example /tmp/a.apk)" = same ] || { echo "remember same" >&2; fail=1; }
	[ "$(installed_remember "$dir/installed.tsv" com.example /tmp/b.apk)" = conflict ] || { echo "remember conflict" >&2; fail=1; }
	[ "$(installed_remember "$dir/installed.tsv" com.other /tmp/b.apk)" = new ] || { echo "remember other" >&2; fail=1; }
	if portable_timeout 1 sleep 5; then
		echo "timeout did not stop sleep" >&2
		fail=1
	else
		status=$?
		if [ "$status" -ne 124 ]; then
			echo "timeout status $status" >&2
			fail=1
		fi
	fi
	portable_timeout 5 true || { echo "timeout true" >&2; fail=1; }
	rm -rf "$dir"
	if [ "$fail" -ne 0 ]; then
		exit 1
	fi
	echo "portable self-test ok"
	exit 0
fi
unset _portable_main
