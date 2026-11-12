#!/usr/bin/env python3
"""Split java-oncreate-end -> jni-activity on the generated activity wrapper.

The stub is rewritten on every build, so this runs before javac and does
nothing if the marks are already present. It only edits the activity that
calls ManagedPeer.registerNativeMembers.
"""

import pathlib
import sys

ENTER = '\tnativepoc.StartupProbe.mark("jcw-enter");\n'
REGISTER = (
	'\tnativepoc.StartupProbe.mark("jcw-register");\n'
	'\tint jcwNatives = 0;\n'
	'\tfor (int jcwIndex = 0; jcwIndex < __md_methods.length(); jcwIndex++)\n'
	'\t\tif (__md_methods.charAt(jcwIndex) == \'\\n\')\n'
	'\t\t\tjcwNatives++;\n'
	'\tandroid.util.Log.i("NativePoc", "jcw-natives count=" + jcwNatives);\n'
)
REGISTERED = '\tnativepoc.StartupProbe.mark("jcw-registered");\n'
SUPER = '\t\tnativepoc.StartupProbe.mark("jcw-super");\n'
PEER = '\t\tnativepoc.StartupProbe.mark("jcw-peer");\n'


def main() -> None:
	path = pathlib.Path(sys.argv[1])
	text = path.read_text()
	if "jcw-enter" in text:
		return
	if "registerNativeMembers" not in text:
		return
	static = "\tstatic {\n\t\t__md_methods ="
	if static not in text:
		sys.exit(path.name + ": static block was not the generated shape")
	text = text.replace(static, "\tstatic {\n" + ENTER + "\t\t__md_methods =", 1)
	call = "\t\tnet.dot.jni.ManagedPeer.registerNativeMembers (MainActivity.class, __md_methods);\n"
	if call not in text:
		sys.exit(path.name + ": registerNativeMembers call was not the generated shape")
	text = text.replace(call, REGISTER + call + REGISTERED, 1)
	ctor = "\t\tsuper ();\n"
	if text.count(ctor) != 1:
		sys.exit(path.name + ": expected one super() in the activity")
	text = text.replace(ctor, ctor + SUPER, 1)
	end = '\t\t\tnet.dot.jni.ManagedPeer.construct (this, "()V", new java.lang.Object[] {  });\n\t\t}\n\t}\n'
	if end not in text:
		sys.exit(path.name + ": construct() was not the generated shape")
	text = text.replace(end, end.replace("\t}\n", PEER + "\t}\n", 1), 1)
	path.write_text(text)
	print("marked " + str(path))


if __name__ == "__main__":
	main()
