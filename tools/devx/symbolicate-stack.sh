#!/usr/bin/env bash
# Map a Release NativeAOT stack onto file:line.
# The log line is "at Type.Method() + 0xoffset" or "BaseAddress+0xoffset"
# or a tombstone "pc" in libNativePoc.Android.so. The .dbg beside the build
# has the DWARF. The APK does not.
#
#   tools/devx/symbolicate-stack.sh --self-test
#   tools/devx/symbolicate-stack.sh --dbg path/NativePoc.Android.so.dbg --stack crash.txt
#   tools/devx/symbolicate-stack.sh --dbg path/NativePoc.Android.so.dbg <<'EOF'
#   at NativePoc.CrashReport.ThrowNow() + 0x27
#   EOF
set -euo pipefail

dbg=""
stack=""
self_test=0

usage() {
	cat <<'EOF'
tools/devx/symbolicate-stack.sh --dbg <NativePoc.Android.so.dbg> [--stack <file>]
  Frames come from --stack or from stdin.
  tools/devx/symbolicate-stack.sh --self-test
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--dbg) dbg="${2:?}"; shift 2 ;;
		--stack) stack="${2:?}"; shift 2 ;;
		--self-test) self_test=1; shift ;;
		--help|-h) usage; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
	esac
done

symbolizer() {
	if command -v llvm-addr2line >/dev/null 2>&1; then
		echo llvm-addr2line
	elif command -v addr2line >/dev/null 2>&1; then
		echo addr2line
	else
		echo ""
	fi
}

nm_tool() {
	if command -v llvm-nm >/dev/null 2>&1; then
		echo llvm-nm
	elif command -v nm >/dev/null 2>&1; then
		echo nm
	else
		echo ""
	fi
}

native_needle() {
	local frame="$1"
	local method type
	if [[ "$frame" == *..ctor ]]; then
		method="_ctor"
		type="${frame%..ctor}"
	else
		method="${frame##*.}"
		type="${frame%.*}"
	fi
	type="${type//./_}"
	type="${type//+/_}"
	printf '%s__%s\n' "$type" "$method"
}

hex_add() {
	local base="$1"
	local off="$2"
	python3 -c 'import sys; print(format(int(sys.argv[1], 16) + int(sys.argv[2], 16), "x"))' "$base" "$off"
}

hex_lt() {
	python3 -c 'import sys; raise SystemExit(0 if int(sys.argv[1], 16) < int(sys.argv[2], 16) else 1)' "$1" "$2"
}

lookup_symbol() {
	local file="$1"
	local needle="$2"
	local nm_bin
	nm_bin="$(nm_tool)"
	if [[ -z "$nm_bin" ]]; then
		echo "nm not found" >&2
		return 1
	fi
	"$nm_bin" -S --defined-only "$file" | awk -v needle="$needle" '
		{
			name = $NF
			if (name == needle || substr(name, length(name) - length(needle) + 1) == needle) {
				addr = $1
				size = "0"
				if (NF >= 4 && $2 ~ /^[0-9A-Fa-f]+$/)
					size = $2
				print addr, size, name
				exit
			}
		}
	'
}

resolve_pc() {
	local file="$1"
	local pc="$2"
	local tool line
	tool="$(symbolizer)"
	if [[ -z "$tool" ]]; then
		echo "addr2line not found" >&2
		return 1
	fi
	line="$("$tool" -e "$file" -f -C -i "0x$pc" | tr -d '\r')"
	printf '%s\n' "$line"
}

file_line() {
	local text="$1"
	printf '%s\n' "$text" | awk '
		/:[0-9]+/ && $0 !~ /^\?\?:/ {
			n = split($0, parts, ":")
			if (parts[n] ~ /^[0-9]+$/) {
				file = parts[1]
				for (i = 2; i < n; i++)
					file = file ":" parts[i]
				sub(/^.*\//, "", file)
				print file ":" parts[n]
				exit
			}
		}
	'
}

symbolicate_text() {
	local file="$1"
	local text="$2"
	local found=0
	local lined=0
	while IFS= read -r line || [[ -n "$line" ]]; do
		local frame="" offset="" pc="" kind=""
		if [[ "$line" =~ at[[:space:]]([A-Za-z0-9_+.]+)[[:space:]]*\(\)[[:space:]]*\+[[:space:]]*0x([0-9A-Fa-f]+) ]]; then
			frame="${BASH_REMATCH[1]}"
			offset="${BASH_REMATCH[2]}"
			kind="method"
		elif [[ "$line" =~ (\<BaseAddress\>|BaseAddress)\+0x([0-9A-Fa-f]+) ]]; then
			offset="${BASH_REMATCH[2]}"
			kind="base"
		elif [[ "$line" == *libNativePoc.Android.so* && "$line" =~ pc[[:space:]]+([0-9A-Fa-f]+) ]]; then
			offset="${BASH_REMATCH[1]}"
			kind="pc"
		else
			continue
		fi
		found=1
		echo "frame $line"
		if [[ "$kind" == "method" ]]; then
			local needle hit addr size name target note
			needle="$(native_needle "$frame")"
			hit="$(lookup_symbol "$file" "$needle" || true)"
			if [[ -z "$hit" ]]; then
				echo "  unresolved needle=$needle"
				continue
			fi
			addr="${hit%% *}"
			size="$(printf '%s\n' "$hit" | awk '{ print $2 }')"
			name="$(printf '%s\n' "$hit" | awk '{ print $3 }')"
			note=""
			if [[ "$size" != "0" ]] && ! hex_lt "$offset" "$size"; then
				if hex_lt "0" "$size"; then
					offset="$(python3 -c 'import sys; print(format(int(sys.argv[1], 16) - 1, "x"))' "$size")"
					note=" clamped"
				fi
			fi
			target="$(hex_add "$addr" "$offset")"
			echo "  symbol=$name addr=0x$addr offset=0x$offset pc=0x$target$note"
			pc="$target"
		else
			pc="$(printf '%s' "$offset" | tr 'A-F' 'a-f')"
			echo "  pc=0x$pc"
		fi
		local resolved where
		resolved="$(resolve_pc "$file" "$pc")"
		printf '%s\n' "$resolved" | sed 's/^/  /'
		where="$(file_line "$resolved")"
		if [[ -n "$where" ]]; then
			echo "  file:line $where"
			lined=1
		else
			echo "  file:line none"
		fi
	done <<<"$text"
	if [[ "$found" -eq 0 ]]; then
		echo "no stack frame" >&2
		return 1
	fi
	if [[ "$lined" -eq 0 ]]; then
		return 1
	fi
	return 0
}

self_test() {
	local cc tmp src lib out needle
	if command -v clang >/dev/null 2>&1; then
		cc="clang"
	else
		cc="$(find "${ANDROID_HOME:-/home/ubuntu/android-sdk}/ndk" -path '*/bin/clang' -type f 2>/dev/null | head -n 1 || true)"
	fi
	if [[ -z "$cc" ]]; then
		echo "clang not found" >&2
		exit 1
	fi
	tmp="$(mktemp -d)"
	src="$tmp/CrashReport.c"
	lib="$tmp/libNativePoc.Android.so"
	cat >"$src" <<'EOF'
void NativePoc_Android_NativePoc_CrashReport__ThrowNow(void) {
	volatile int marker = 1;
	marker += 1;
}
EOF
	"$cc" -shared -fPIC -g -O0 -o "$lib" "$src"
	needle="$(native_needle "NativePoc.CrashReport.ThrowNow")"
	[[ "$needle" == "NativePoc_CrashReport__ThrowNow" ]] || { echo "needle $needle" >&2; exit 1; }
	[[ "$(native_needle "NativePoc.CrashReport.ThrowSoon.Run")" == "NativePoc_CrashReport_ThrowSoon__Run" ]] || { echo "nested needle" >&2; exit 1; }
	[[ "$(native_needle "NativePoc.CrashReport.ThrowSoon..ctor")" == "NativePoc_CrashReport_ThrowSoon___ctor" ]] || { echo "ctor needle" >&2; exit 1; }
	out="$(symbolicate_text "$lib" 'at NativePoc.CrashReport.ThrowNow() + 0x0')"
	printf '%s\n' "$out"
	printf '%s\n' "$out" | grep -q 'file:line CrashReport.c:' || { echo "missing file:line" >&2; exit 1; }
	base="$(lookup_symbol "$lib" "$needle" | awk '{ print $1 }')"
	out2="$(symbolicate_text "$lib" "libNativePoc.Android!<BaseAddress>+0x$base")"
	printf '%s\n' "$out2" | grep -q 'file:line CrashReport.c:' || { echo "base missing file:line" >&2; printf '%s\n' "$out2" >&2; exit 1; }
	rm -rf "$tmp"
	echo "symbolicate self-test ok"
}

if [[ "$self_test" -eq 1 ]]; then
	self_test
	exit 0
fi

if [[ -z "$dbg" || ! -f "$dbg" ]]; then
	echo "Pass --dbg pointing at NativePoc.Android.so.dbg" >&2
	usage >&2
	exit 2
fi

if [[ -n "$stack" ]]; then
	text="$(cat "$stack")"
else
	text="$(cat)"
fi

symbolicate_text "$dbg" "$text"
