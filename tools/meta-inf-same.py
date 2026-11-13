#!/usr/bin/env python3
"""Compare non-signature META-INF names of a clean apk and an incremental apk.

Both lists must match, and both must contain the androidx version files and
the annotations proguard file. A pair that dropped the same files does not pass.
"""

import sys
import zipfile

REQUIRED = (
    "META-INF/androidx.core_core.version",
    "META-INF/androidx.customview_customview.version",
    "META-INF/androidx.recyclerview_recyclerview.version",
    "META-INF/proguard/androidx-annotations.pro",
)


def signature(name):
    base = name.rsplit("/", 1)[-1].upper()
    if base == "MANIFEST.MF" or base.endswith(".SF") or base.endswith(".RSA") or base.endswith(".EC"):
        return True
    return "MSFTSIG" in base


def names(apk):
    found = []
    with zipfile.ZipFile(apk) as bundle:
        for info in bundle.infolist():
            name = info.filename
            if name.startswith("META-INF/") and not signature(name):
                found.append(name)
    return sorted(found)


def main():
    if len(sys.argv) != 3:
        print("usage: meta-inf-same.py <clean.apk> <incremental.apk>", file=sys.stderr)
        return 2
    clean = names(sys.argv[1])
    incremental = names(sys.argv[2])
    missing = [name for name in REQUIRED if name not in clean or name not in incremental]
    if clean != incremental or missing:
        print("META-INF mismatch", file=sys.stderr)
        print("only clean: " + " ".join(sorted(set(clean) - set(incremental))), file=sys.stderr)
        print("only incremental: " + " ".join(sorted(set(incremental) - set(clean))), file=sys.stderr)
        print("missing required: " + " ".join(missing), file=sys.stderr)
        return 1
    print("META-INF entries %d" % len(clean))
    return 0


if __name__ == "__main__":
    sys.exit(main())
