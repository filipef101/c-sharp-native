#!/bin/sh
set -eu

require_mac() {
	os=$(uname -s)
	if [ "$os" != "Darwin" ]; then
		echo "This script needs macOS. This host is $os. It does not build or run UIKit." >&2
		exit 1
	fi
}

ensure_dotnet() {
	if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
		export PATH="$HOME/.dotnet:$PATH"
		export DOTNET_ROOT="$HOME/.dotnet"
	fi
	if ! command -v dotnet >/dev/null 2>&1; then
		echo "dotnet is not on PATH." >&2
		exit 1
	fi
}
