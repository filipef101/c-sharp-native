#!/usr/bin/env python3
"""Register only n_onCreate while the activity class initializes.

The generated wrapper registers every override in its static block. The
first frame calls onCreate and then onResume; the other overrides are not
on that path. This leaves n_onCreate in the static block and registers each
remaining override on its first entry. Opt-in via NativePocDeferNatives.
The stub is rewritten on every build, so the edit is idempotent.
"""

import pathlib
import re
import sys

MARK = "/* __jcw_defer */"
ASSIGN = re.compile(
	r"__md_methods\s*=\s*((?:\"(?:\\.|[^\"])*\"\s*\+\s*)*)\"\"\s*;",
	re.S,
)
METHOD = re.compile(
	r"public void (?P<name>on[A-Za-z0-9]+) \((?P<args>[^)]*)\)\s*\{\s*(?P<call>n_[A-Za-z0-9]+ \([^;]*\);)\s*\}"
)
LINE = re.compile(r'"(n_[^"]*)"')


def main() -> None:
	path = pathlib.Path(sys.argv[1])
	text = path.read_text()
	if MARK in text:
		return
	if "registerNativeMembers" not in text:
		return
	found = ASSIGN.search(text)
	if not found:
		sys.exit(path.name + ": __md_methods assignment was not the generated shape")
	lines = LINE.findall(found.group(1))
	create = [line for line in lines if line.startswith("n_onCreate:")]
	rest = [line for line in lines if not line.startswith("n_onCreate:")]
	if len(create) != 1:
		sys.exit(path.name + ": expected one n_onCreate registration")
	replacement = '__md_methods =\n\t\t\t"' + create[0] + '" +\n\t\t\t"";'
	text = text[: found.start()] + replacement + text[found.end() :]
	needle = "public static final String __md_methods;\n"
	if needle not in text:
		sys.exit(path.name + ": __md_methods field was not the generated shape")
	fields = "\t" + MARK + "\n"
	for line in rest:
		fields += "\tstatic boolean __jcw_" + java_name(line) + ";\n"
	text = text.replace(needle, needle + fields, 1)
	call = "net.dot.jni.ManagedPeer.registerNativeMembers (MainActivity.class, __md_methods);\n"
	if call not in text:
		sys.exit(path.name + ": registerNativeMembers call was not the generated shape")
	log = '\t\tandroid.util.Log.i("NativePoc", "jcw-deferred kept=onCreate rest=' + str(len(rest)) + '");\n'
	text = text.replace(call, call + log, 1)

	def repl(match: re.Match[str]) -> str:
		name = match.group("name")
		if name == "onCreate":
			return match.group(0)
		line = next((item for item in rest if java_name(item) == name), None)
		if line is None:
			return match.group(0)
		flag = "__jcw_" + name
		return (
			"public void "
			+ name
			+ " ("
			+ match.group("args")
			+ ")\n"
			+ "\t{\n"
			+ "\t\tif (!"
			+ flag
			+ ") {\n"
			+ "\t\t\t"
			+ flag
			+ " = true;\n"
			+ '\t\t\tnet.dot.jni.ManagedPeer.registerNativeMembers (MainActivity.class, "'
			+ line
			+ '");\n'
			+ "\t\t}\n"
			+ "\t\t"
			+ match.group("call")
			+ "\n"
			+ "\t}"
		)

	text, count = METHOD.subn(repl, text)
	if count < 1 + len(rest):
		sys.exit(path.name + ": did not find every generated override")
	path.write_text(text)
	print("deferred " + str(path) + " rest=" + str(len(rest)))


def java_name(line: str) -> str:
	native = line.split(":", 1)[0]
	if not native.startswith("n_"):
		sys.exit("bad native line " + line)
	return native[2:]


if __name__ == "__main__":
	main()
