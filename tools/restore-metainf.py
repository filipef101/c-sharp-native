#!/usr/bin/env python3
"""Put library META-INF entries back into an incremental Android App Bundle.

An incremental _CreateBaseApk rewrites base.zip without the jar META-INF
files, and the bundle built from that zip drops them. The copies on disk
and the classes.jar entries are still there. This appends the missing
non-signature files at base/root/META-INF before the universal apk is built.
"""

import os
import sys
import zipfile


def signature(name):
    base = name.rsplit("/", 1)[-1].upper()
    if base == "MANIFEST.MF" or base.endswith(".SF") or base.endswith(".RSA") or base.endswith(".EC"):
        return True
    return "MSFTSIG" in base


def collect(meta_dir, lp_dir):
    found = {}
    if os.path.isdir(meta_dir):
        for root, _dirs, files in os.walk(meta_dir):
            for name in files:
                path = os.path.join(root, name)
                rel = "META-INF/" + os.path.relpath(path, meta_dir).replace(os.sep, "/")
                if signature(rel):
                    continue
                with open(path, "rb") as handle:
                    found[rel] = handle.read()
    if os.path.isdir(lp_dir):
        for root, _dirs, files in os.walk(lp_dir):
            if "classes.jar" not in files:
                continue
            jar_path = os.path.join(root, "classes.jar")
            with zipfile.ZipFile(jar_path) as jar:
                for info in jar.infolist():
                    if info.is_dir():
                        continue
                    name = info.filename
                    if not name.startswith("META-INF/") or signature(name):
                        continue
                    if not (name.endswith(".version") or name.endswith(".pro")):
                        continue
                    if name not in found:
                        found[name] = jar.read(info)
    return found


def main():
    if len(sys.argv) != 4:
        print("usage: restore-metainf.py <aab> <META-INF dir> <lp dir>", file=sys.stderr)
        return 2
    aab, meta_dir, lp_dir = sys.argv[1:]
    if not os.path.isfile(aab):
        print("restore-metainf: no bundle")
        return 0
    found = collect(meta_dir, lp_dir)
    with zipfile.ZipFile(aab) as bundle:
        present = set(bundle.namelist())
    missing = [(rel, data) for rel, data in sorted(found.items()) if "base/root/" + rel not in present]
    if not missing:
        print("restore-metainf: bundle already has %d library entries" % len(found))
        return 0
    with zipfile.ZipFile(aab, "a") as bundle:
        for rel, data in missing:
            bundle.writestr("base/root/" + rel, data)
    print("restore-metainf: added %d" % len(missing))
    for rel, _data in missing:
        print("restore-metainf: " + rel)
    return 0


if __name__ == "__main__":
    sys.exit(main())
