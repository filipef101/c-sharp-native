#!/usr/bin/env bash
# Cold start and Feed scroll for a MacBook emulator whose AVD is named
# pixel_fold_mc. Boot that AVD yourself, then pass the serial from
# `adb devices`. This file does not start the emulator and does not
# record a run.
#
#   tools/bench/pixel-fold.sh --self-test
#   tools/bench/pixel-fold.sh cold --device <serial> --out DIR --n 20 \
#       --left-apk counter.apk --left-pkg com.scratch.nativepoc --left-name counter \
#       --right-apk showcase.apk --right-pkg com.scratch.nativepoc.showcase --right-name showcase
#   tools/bench/pixel-fold.sh scroll --device <serial> --out DIR \
#       --apk feed.apk \
#       --activity com.scratch.nativepoc/crc64ee53024f7ed0aa60.MainActivity \
#       --swipes 8 --rounds 5 \
#       --extra "--es nativepoc_root feed"
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

usage() {
	cat <<'EOF'
tools/bench/pixel-fold.sh cold [interleave-cold.sh options]
tools/bench/pixel-fold.sh scroll [scroll-bench.sh options]
tools/bench/pixel-fold.sh --self-test

The AVD name is pixel_fold_mc. --device is the adb serial, which is not
the AVD name. cold runs tools/bench/interleave-cold.sh. scroll runs
tools/bench/scroll-bench.sh, which reads framestats from
`dumpsys gfxinfo <package> framestats`.
EOF
}

if [[ "${1:-}" == "--self-test" ]]; then
	plan="$(bash "$root/tools/bench/pixel-fold.sh" cold --device pixel_fold_mc --dry-plan)"
	scroll="$(bash "$root/tools/bench/pixel-fold.sh" scroll --device pixel_fold_mc --dry-plan)"
	[[ "$plan" == *"avd=pixel_fold_mc"* ]] || { echo "cold plan missing avd" >&2; exit 1; }
	[[ "$plan" == *"interleave-cold.sh"* ]] || { echo "cold plan missing interleave" >&2; exit 1; }
	[[ "$plan" == *"--device pixel_fold_mc"* ]] || { echo "cold plan missing device" >&2; exit 1; }
	[[ "$scroll" == *"scroll-bench.sh"* ]] || { echo "scroll plan missing scroll-bench" >&2; exit 1; }
	[[ "$scroll" == *"framestats"* ]] || { echo "scroll plan missing framestats" >&2; exit 1; }
	[[ "$scroll" == *"dumpsys gfxinfo"* ]] || { echo "scroll plan missing gfxinfo" >&2; exit 1; }
	echo "pixel-fold self-test ok"
	exit 0
fi

mode="${1:-}"
if [[ "$mode" != "cold" && "$mode" != "scroll" ]]; then
	usage >&2
	exit 2
fi
shift

dry=0
args=()
for arg in "$@"; do
	if [[ "$arg" == "--dry-plan" ]]; then
		dry=1
	else
		args+=("$arg")
	fi
done

device=""
prev=""
for arg in "${args[@]+"${args[@]}"}"; do
	if [[ "$prev" == "--device" || "$prev" == "--serial" ]]; then
		device="$arg"
	fi
	prev="$arg"
done

if [[ "$dry" -eq 1 ]]; then
	echo "avd=pixel_fold_mc"
	echo "device=${device:-unset}"
	if [[ "$mode" == "cold" ]]; then
		echo "exec tools/bench/interleave-cold.sh --device ${device:-unset}"
	else
		echo "exec tools/bench/scroll-bench.sh --device ${device:-unset}"
		echo "metric dumpsys gfxinfo <package> framestats"
	fi
	exit 0
fi

if [[ "$mode" == "cold" ]]; then
	exec bash "$root/tools/bench/interleave-cold.sh" "${args[@]+"${args[@]}"}"
fi
exec bash "$root/tools/bench/scroll-bench.sh" "${args[@]+"${args[@]}"}"
