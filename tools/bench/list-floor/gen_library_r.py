"""Write androidx.<lib>.R from a library R.txt and the linked app R.java."""

import pathlib
import re
import sys


def linked_ids(path):
	ids = {}
	current = None
	for line in pathlib.Path(path).read_text().splitlines():
		kind = re.match(r"\s*public static final class (\w+) \{", line)
		if kind:
			current = kind.group(1)
			ids.setdefault(current, {})
			continue
		item = re.match(r"\s*public static final int (\w+)=0x([0-9a-fA-F]+);", line)
		if item and current:
			ids[current][item.group(1)] = int(item.group(2), 16)
	return ids


def library_symbols(path):
	scalars = []
	arrays = []
	fields = []
	for raw in pathlib.Path(path).read_text().splitlines():
		array = re.match(r"int\[\] styleable (\w+) \{([^}]*)\}", raw)
		if array:
			values = [int(part, 0) for part in array.group(2).replace(",", " ").split()]
			arrays.append((array.group(1), values))
			continue
		field = re.match(r"int styleable (\S+) (\d+)$", raw)
		if field:
			token = field.group(1)
			index = int(field.group(2))
			styleable = ""
			name = token
			for candidate, _values in arrays:
				prefix = candidate + "_"
				if token.startswith(prefix) and len(candidate) > len(styleable):
					styleable = candidate
					name = token[len(prefix):]
			if styleable == "":
				raise SystemExit("styleable field has no array: " + token)
			fields.append((styleable, name, index))
			continue
		scalar = re.match(r"int (\w+) (\S+) 0x0$", raw)
		if scalar:
			scalars.append((scalar.group(1), scalar.group(2)))
	return scalars, arrays, fields


def main():
	linked_path, library_r, package, gen = sys.argv[1:]
	ids = linked_ids(linked_path)
	scalars, arrays, fields = library_symbols(library_r)
	for styleable, name, index in fields:
		if name.startswith("android_"):
			continue
		for entry in arrays:
			if entry[0] != styleable:
				continue
			if entry[1][index] == 0:
				entry[1][index] = ids["attr"][name]
	resolved = []
	for kind, name in scalars:
		table = ids.get(kind, {})
		if name not in table:
			# Library R.txt lists transitive symbols this link does not ship.
			# Bytecode that actually loads one fails later, at class init.
			continue
		resolved.append((kind, name, table[name]))
	for styleable, values in arrays:
		if any(value == 0 for value in values):
			raise SystemExit("styleable " + styleable + " still has a zero id")
	out = pathlib.Path(gen).joinpath(*package.split("."), "R.java")
	out.parent.mkdir(parents=True, exist_ok=True)
	lines = ["package " + package + ";", "", "public final class R {"]
	by_kind = {}
	for kind, name, value in resolved:
		by_kind.setdefault(kind, []).append((name, value))
	for kind, items in by_kind.items():
		lines.append("  public static final class " + kind + " {")
		for name, value in items:
			lines.append("    public static final int " + name + "=0x%08x;" % value)
		lines.append("  }")
	lines.append("  public static final class styleable {")
	for styleable, values in arrays:
		body = ",".join("0x%08x" % value for value in values)
		lines.append("    public static final int[] " + styleable + "={" + body + "};")
	for styleable, name, index in fields:
		lines.append("    public static final int " + styleable + "_" + name + "=" + str(index) + ";")
	lines.append("  }")
	lines.append("}")
	out.write_text("\n".join(lines) + "\n")
	print("wrote", out)


if __name__ == "__main__":
	main()
