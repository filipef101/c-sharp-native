#!/usr/bin/env python3
"""Bind the activity overrides with one prebuilt RegisterNatives call.

ManagedPeer.registerNativeMembers splits __md_methods and builds each
callback with Delegate.CreateDelegate. That is the Mac's 16 ms. This
leaves the method string in place for the startup marks and replaces the
call with nativepoc.ActivityNatives.register. Opt-in via
NativePocFastNatives. The location permission override is not in the
prebuilt table.
"""

import pathlib
import re
import sys

CALL = "\t\tnet.dot.jni.ManagedPeer.registerNativeMembers (MainActivity.class, __md_methods);\n"
FAST = "\t\tnativepoc.ActivityNatives.register(MainActivity.class);\n"
LINE = re.compile(r'"(n_[^":]+):')
EXPECTED = {
	"n_onCreate",
	"n_onNewIntent",
	"n_onResume",
	"n_onBackPressed",
	"n_onDestroy",
}


def main() -> None:
	path = pathlib.Path(sys.argv[1])
	text = path.read_text()
	if "ActivityNatives.register" in text:
		return
	if CALL not in text:
		sys.exit(path.name + ": registerNativeMembers call was not the generated shape")
	names = LINE.findall(text)
	if "n_onRequestPermissionsResult" in names:
		sys.exit(path.name + ": NativePocFastNatives does not cover the location override")
	if set(names) != EXPECTED:
		sys.exit(path.name + ": expected the five activity overrides, found " + " ".join(names))
	path.write_text(text.replace(CALL, FAST, 1))
	print("fast natives " + str(path) + " count=" + str(len(names)))


if __name__ == "__main__":
	main()
