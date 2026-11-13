#!/usr/bin/env python3
"""Exit 1 unless classes.dex contains NativeAotRuntimeProvider.

Accepts a signed apk or a directory of dex files.
"""

import pathlib
import sys
import zipfile

NEEDLE = b"NativeAotRuntimeProvider"


def contains(path: pathlib.Path) -> bool:
    if path.is_dir():
        dexes = list(path.rglob("*.dex"))
        return any(NEEDLE in dex.read_bytes() for dex in dexes)
    if not path.is_file():
        return False
    with zipfile.ZipFile(path) as archive:
        names = [name for name in archive.namelist() if name.endswith(".dex")]
        return any(NEEDLE in archive.read(name) for name in names)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: dex-has-provider.py <apk-or-dex-dir>", file=sys.stderr)
        return 2
    path = pathlib.Path(sys.argv[1])
    if contains(path):
        print(f"NativeAotRuntimeProvider is in classes.dex: {path}")
        return 0
    print(f"classes.dex has no NativeAotRuntimeProvider: {path}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
