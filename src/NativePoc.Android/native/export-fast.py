#!/usr/bin/env python3
"""Export the prebuilt activity registration from the NativeAOT version script.

The generated script keeps JNI_OnLoad, JNI_OnUnload, and
JavaInteropRuntime.init. A local wildcard hides every other definition,
including Java_nativepoc_ActivityNatives_register.
"""

import pathlib
import sys

SYMBOL = "        Java_nativepoc_ActivityNatives_register;\n"
ANCHOR = "        Java_net_dot_jni_nativeaot_JavaInteropRuntime_init;\n"


def main() -> None:
	path = pathlib.Path(sys.argv[1])
	text = path.read_text()
	if "Java_nativepoc_ActivityNatives_register;" in text:
		return
	if ANCHOR not in text:
		sys.exit(path.name + ": JavaInteropRuntime export was not in the version script")
	path.write_text(text.replace(ANCHOR, ANCHOR + SYMBOL, 1))
	print("exported ActivityNatives.register in " + str(path))


if __name__ == "__main__":
	main()
