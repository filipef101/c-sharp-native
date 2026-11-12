#!/usr/bin/env python3
"""Sequential hot-reload edits against one cnative listener.

Every edit must apply, show a compile-error overlay that a later edit clears,
or take the documented rude restart. The script restores the files it edits.
"""

from __future__ import annotations

import argparse
import os
import pty
import subprocess
import sys
import threading
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


class Edit:
	def __init__(self, kind: str, path: str, repls: list[tuple[str, str, int]], expect: str, token: str = "", clear: bool = False, restore: bool = False):
		self.kind = kind
		self.path = path
		self.repls = repls
		self.expect = expect
		self.token = token
		self.clear = clear
		self.restore = restore


def build_edits() -> list[Edit]:
	edits: list[Edit] = []
	feed = "samples/feed/FeedScreen.cs"
	notes = "samples/notes/NotesScreen.cs"
	note = "samples/note/NoteScreen.cs"
	screen = "src/NativePoc.Screen/Screen.cs"
	where = "samples/where/WhereScreen.cs"
	packing = "samples/NativePoc.Packing/PackingScreen.cs"
	places = "samples/NativePoc.Places/PlacesScreen.cs"
	form = "samples/NativePoc.Form/FormScreen.cs"
	library = "samples/NativePoc.Library/LibraryFormat.cs"
	elements = "src/NativePoc.Core/Elements.cs"
	service = "src/NativePoc.Screen/CounterService.cs"

	for index in range(8):
		token = f"T{index:02d}"
		edits.append(Edit("text", feed, [('new Text("Feed")', f'new Text("Feed {token}")', 2)], "apply", f"Feed {token}"))
	for index in range(6):
		token = f"N{index:02d}"
		edits.append(Edit("text", notes, [('new Text("Notes")', f'new Text("Notes {token}")', 1)], "apply", f"Notes {token}"))
	for index in range(4):
		token = f"E{index:02d}"
		edits.append(Edit("text", note, [('new Text("Note")', f'new Text("Note {token}")', 1)], "apply", f"Note {token}"))
	for index in range(6):
		token = f"P{index:02d}"
		edits.append(Edit("text", places, [('new Text("Place")', f'new Text("Place {token}")', 1)], "apply", f"Place {token}"))
	for index in range(6):
		token = f"C{index:02d}"
		edits.append(Edit("text", screen, [('public const string TitlePrefix = "Counter ";', f'public const string TitlePrefix = "{token} ";', 1)], "apply", token))
	for index in range(6):
		token = f"D{index:02d}"
		edits.append(Edit("text", form, [('state.Sent ? "Sent" : "Draft"', f'state.Sent ? "Sent" : "Draft {token}"', 1)], "apply", f"Draft {token}"))

	heading = 'new Text("Feed") { Key = "heading", Style = new Style { FontSize = 22 } }'
	for index, size in enumerate((18, 20, 24, 26, 28, 30, 32, 16)):
		token = f"S{index:02d}"
		new = 'new Text("Feed ' + token + '") { Key = "heading", Style = new Style { FontSize = ' + str(size) + " } }"
		edits.append(Edit("style", feed, [(heading, new, 2)], "apply", f"Feed {token}/{size}"))
	for index, size in enumerate((18, 20, 24, 26, 28)):
		edits.append(Edit("style", notes, [("FontSize = 22", f"FontSize = {size}", 1)], "apply", f"Notes/{size}"))
	note_heading = 'new Text("Note") { Key = "heading", Style = new Style { FontSize = 22 } }'
	for index, size in enumerate((18, 20, 24, 26, 28)):
		token = f"Y{index:02d}"
		new = 'new Text("Note ' + token + '") { Key = "heading", Style = new Style { FontSize = ' + str(size) + " } }"
		edits.append(Edit("style", note, [(note_heading, new, 1)], "apply", f"Note {token}/{size}"))

	save = '''new Button(
				"Save",
				() =>
				{
					state.Count++;
					platform.CopyNonce("poc-" + state.Count);
					platform.RequestRender();
				},
				"Save name")'''
	for index in range(8):
		token = f"H{index:02d}"
		new = save.replace('"Save"', f'"{token}"', 1).replace("state.Count++;", "state.Count += 1;")
		edits.append(Edit("handler", screen, [(save, new, 1)], "apply", token))
	refresh = 'new Button("Refresh", () => Load(), "Refresh")'
	for index in range(4):
		token = f"R{index:02d}"
		edits.append(Edit("handler", feed, [(refresh, f'new Button("{token}", () => Load(), "Refresh")', 2), ('state.Phase = "loading";', 'state.Phase = "working";', 2)], "apply", token))
	add = 'new Button("Add", () => Add(), "Add")'
	for index in range(4):
		token = f"A{index:02d}"
		edits.append(Edit("handler", notes, [(add, f'new Button("{token}", () => Add(), "Add")', 1), ('state.Error = "Need a title";', f'state.Error = "Need a title {token}";', 1)], "apply", token))

	row = 'new Text("• " + item.Title)'
	for index in range(15):
		token = f"L{index:02d}"
		edits.append(Edit("list", feed, [(row, f'new Text("{token} " + item.Title)', 1)], "apply", token))
	for index in range(8):
		token = f"M{index:02d}"
		edits.append(Edit("helper", feed, [(row, f'new Text("{token} " + item.Title)', 1)], "apply", token))
	for index in range(4):
		token = f"e{index}"
		edits.append(Edit("helper", library, [('name.Length == 0 ? "empty"', f'name.Length == 0 ? "{token}"', 1)], "swap", token))

	load_head = '''async void Load()
		{
			try
			{
				state.Loading = true;
				state.Phase = "loading";'''
	for index in range(10):
		token = f"Z{index:02d}"
		new_head = load_head.replace('"loading"', f'"{token}"', 1)
		edits.append(Edit("async", feed, [(load_head, new_head, 1), ('InvalidOperationException("down")', f'InvalidOperationException("{token}")', 1), ('new Text("Feed")', f'new Text("Feed {token}")', 2)], "apply", f"Feed {token}"))
	caught = 'state.Phase = "caught";'
	for index in range(10):
		token = f"K{index:02d}"
		edits.append(Edit("try", feed, [(caught, f'state.Phase = "{token}";', 1), ('new Text("Feed")', f'new Text("Feed {token}")', 2)], "apply", f"Feed {token}"))

	push = 'new Button("Packing", () => { platform.Push("packing"); platform.RequestRender(); }, "Packing")'
	for index in range(10):
		token = f"V{index:02d}"
		new = f'new Button("{token}", () => {{ platform.Push("notes"); platform.RequestRender(); }}, "Packing")'
		edits.append(Edit("nav", screen, [(push, new, 2)], "apply", token))
	read = 'new Button("Read", () => Read(), "Read")'
	for index in range(8):
		token = f"W{index:02d}"
		edits.append(Edit("location", where, [(read, f'new Button("{token}", () => Read(), "Read")', 1)], "apply", token))

	prefix = 'public const string TitlePrefix = "Counter ";'
	broken = "public const string TitlePrefix = ;"
	for index in range(8):
		edits.append(Edit("syntax", screen, [(prefix, broken, 1)], "overlay"))
		edits.append(Edit("syntax", screen, [], "apply", clear=True, restore=True))

	field_old = "\tpublic bool PlainRoot { get; set; }\n}"
	field_new = "\tpublic bool PlainRoot { get; set; }\n\n\tpublic int RudeGeneration { get; set; }\n}"
	edits.append(Edit("rude-field", elements, [(field_old, field_new, 1)], "restart"))
	edits.append(Edit("rude-field", elements, [], "restart", restore=True))
	label = "public static string Label(string name)"
	edits.append(Edit("rude-signature", library, [(label, "public static string Label(string name, int mark)", 1)], "swap"))
	edits.append(Edit("rude-signature", library, [], "swap", restore=True))
	nxt = "public static int Next(int count)"
	edits.append(Edit("rude-signature", service, [(nxt, "public static int Next(int count, int extra)", 1)], "swap"))
	edits.append(Edit("rude-signature", service, [], "swap", restore=True))
	return edits


def check(edits: list[Edit]) -> None:
	originals: dict[str, str] = {}
	for edit in edits:
		if edit.restore:
			continue
		if edit.path not in originals:
			originals[edit.path] = (REPO / edit.path).read_text()
		text = originals[edit.path]
		for old, _, count in edit.repls:
			found = text.count(old)
			if found != count:
				raise SystemExit(f"{edit.kind} {edit.path}: found {found}, expected {count}, for {old[:80]!r}")


def percentile(values: list[float], p: float) -> float:
	xs = sorted(values)
	n = len(xs)
	if n == 0:
		raise ValueError("empty")
	if n == 1:
		return xs[0]
	h = 1 + (n - 1) * p
	k = int(h)
	d = h - k
	if k <= 1:
		return xs[0]
	if k >= n:
		return xs[-1]
	return xs[k - 1] + d * (xs[k] - xs[k - 1])


def pidof(device: str) -> str:
	ran = subprocess.run(["adb", "-s", device, "shell", "pidof", "com.scratch.nativepoc"], capture_output=True, text=True)
	return ran.stdout.strip()


class LineLog:
	"""Copy a process through a pty so line-buffered logs land on disk."""

	def __init__(self, argv: list[str], path: Path, cwd: Path | None = None):
		self.path = path
		self.master, slave = pty.openpty()
		self.proc = subprocess.Popen(argv, cwd=cwd, stdin=slave, stdout=slave, stderr=slave, start_new_session=True)
		os.close(slave)
		self.file = path.open("w", buffering=1)
		self.thread = threading.Thread(target=self._copy, daemon=True)
		self.thread.start()

	def _copy(self) -> None:
		buf = b""
		while True:
			try:
				chunk = os.read(self.master, 4096)
			except OSError:
				break
			if not chunk:
				break
			buf += chunk
			while b"\n" in buf:
				line, buf = buf.split(b"\n", 1)
				self.file.write(line.replace(b"\r", b"").decode("utf-8", "replace") + "\n")
				self.file.flush()
		if buf:
			self.file.write(buf.replace(b"\r", b"").decode("utf-8", "replace"))
			self.file.flush()

	def stop(self) -> None:
		if self.proc.poll() is None:
			self.proc.terminate()
			try:
				self.proc.wait(timeout=10)
			except subprocess.TimeoutExpired:
				self.proc.kill()
		try:
			os.close(self.master)
		except OSError:
			pass
		self.thread.join(timeout=2)
		self.file.close()


def run_corpus(device: str, edits: list[Edit]) -> int:
	log_path = Path("/tmp/edit-corpus-cnative.log")
	originals = {edit.path: (REPO / edit.path).read_text() for edit in edits}
	proc = None
	try:
		subprocess.run(["adb", "-s", device, "logcat", "-G", "8M"], check=False)
		subprocess.run(["adb", "-s", device, "logcat", "-c"], check=False)
		proc = LineLog(
			["tools/devx/cnative", "run", "android", "--device", device, "--listener", "--restart-on-rude", "--debounce", "40"],
			log_path,
			REPO,
		)
		wait_text(log_path, 0, "cnative: watching", 900)
		print("listener watching", flush=True)
		counts = {"apply": 0, "overlay": 0, "restart": 0, "swap": 0}
		times: list[float] = []
		for index, edit in enumerate(edits):
			host_at = log_path.stat().st_size
			before = pidof(device)
			subprocess.run(["adb", "-s", device, "logcat", "-c"], check=False)
			write_edit(edit, originals)
			outcome, chunk, cat_chunk = wait_edit(log_path, host_at, device, edit.expect)
			if "FATAL EXCEPTION" in cat_chunk or "FATAL EXCEPTION" in chunk:
				raise SystemExit(f"edit {index} {edit.kind} crashed\n{cat_chunk[-1500:]}")
			after = pidof(device)
			if edit.expect == "restart":
				if outcome != "restart":
					raise SystemExit(f"edit {index} {edit.kind} expected restart, saw {outcome}\n{chunk[-1500:]}")
				if after == "" or after == before:
					raise SystemExit(f"edit {index} rude restart left pid {after!r} (was {before!r})")
				counts["restart"] += 1
			elif edit.expect == "overlay":
				if outcome != "overlay":
					raise SystemExit(f"edit {index} syntax expected overlay, saw {outcome}\n{chunk[-1500:]}\n{cat_chunk[-1500:]}")
				if "dev-overlay id=" not in cat_chunk and "error CS" not in chunk:
					raise SystemExit(f"edit {index} overlay was not logged\n{chunk[-1500:]}\n{cat_chunk[-1500:]}")
				if after != before or after == "":
					raise SystemExit(f"edit {index} overlay changed pid {before} -> {after}")
				counts["overlay"] += 1
			elif edit.expect == "swap":
				if outcome != "swap":
					raise SystemExit(f"edit {index} {edit.kind} expected swap, saw {outcome}\n{chunk[-1500:]}\n{cat_chunk[-1500:]}")
				if "vm-body " not in cat_chunk:
					raise SystemExit(f"edit {index} swap was not installed\n{cat_chunk[-1500:]}")
				if after != before or after == "":
					raise SystemExit(f"edit {index} swap changed pid {before} -> {after}")
				counts["swap"] += 1
				times.extend(reload_times(chunk))
			else:
				if outcome != "apply":
					raise SystemExit(f"edit {index} {edit.kind} expected apply, saw {outcome}\n{chunk[-1500:]}\n{cat_chunk[-1500:]}")
				late = time.time() + 3
				while time.time() < late:
					need_token = bool(edit.token) and edit.token not in cat_chunk and edit.token not in chunk
					need_clear = edit.clear and "dev-overlay clear" not in cat_chunk
					if not need_token and not need_clear:
						break
					time.sleep(0.1)
					chunk, cat_chunk = read_from(log_path, host_at), dump_logcat(device)
				if edit.token and edit.token not in cat_chunk and edit.token not in chunk:
					raise SystemExit(f"edit {index} {edit.kind} applied without {edit.token!r} on screen\n{cat_chunk[-1500:]}\n{chunk[-800:]}")
				if edit.clear and "dev-overlay clear" not in cat_chunk:
					raise SystemExit(f"edit {index} did not clear the overlay\n{cat_chunk[-1500:]}")
				if after != before or after == "":
					raise SystemExit(f"edit {index} apply changed pid {before} -> {after}")
				counts["apply"] += 1
				times.extend(reload_times(chunk))
			print(f"{index + 1}/{len(edits)} {edit.kind} {outcome}", flush=True)
		p50 = percentile(times, 0.50) if times else float("nan")
		p90 = percentile(times, 0.90) if times else float("nan")
		print(f"edits={len(edits)} apply={counts['apply']} swap={counts['swap']} overlay={counts['overlay']} restart={counts['restart']} crash=0")
		print(f"reload_ms n={len(times)} p50={p50:.1f} p90={p90:.1f}")
		return 0
	finally:
		for path, text in originals.items():
			(REPO / path).write_text(text)
		if proc is not None:
			proc.stop()


def write_edit(edit: Edit, originals: dict[str, str]) -> None:
	path = REPO / edit.path
	if edit.restore:
		path.write_text(originals[edit.path])
		return
	text = originals[edit.path]
	for old, new, count in edit.repls:
		found = text.count(old)
		if found != count:
			raise SystemExit(f"refusing to write {edit.path}: found {found}, expected {count}")
		text = text.replace(old, new)
	path.write_text(text)


def read_from(path: Path, start: int) -> str:
	if not path.exists():
		return ""
	return path.read_bytes()[start:].decode("utf-8", "replace")


def wait_text(path: Path, start: int, needle: str, timeout: float) -> str:
	deadline = time.time() + timeout
	while time.time() < deadline:
		chunk = read_from(path, start)
		if needle in chunk:
			return chunk
		time.sleep(0.2)
	raise SystemExit(f"timed out waiting for {needle}")


def dump_logcat(device: str) -> str:
	ran = subprocess.run(
		["adb", "-s", device, "logcat", "-d", "-v", "brief", "-s", "NativePoc:V", "AndroidRuntime:E"],
		capture_output=True,
	)
	return ran.stdout.decode("utf-8", "replace")


def wait_edit(host: Path, host_at: int, device: str, expect: str) -> tuple[str, str, str]:
	timeout = 240 if expect == "restart" else 45
	deadline = time.time() + timeout
	quiet_at = 0.0
	quiet_size = -1
	while time.time() < deadline:
		chunk, cat_chunk = read_from(host, host_at), dump_logcat(device)
		if "FATAL EXCEPTION" in cat_chunk:
			return "crash", chunk, cat_chunk
		if "reload failed" in chunk and "reloaded in " not in chunk and "restarting" not in chunk:
			return "timeout", chunk, cat_chunk
		if expect == "restart" and "restarting" in chunk and "hotreload-listen" in cat_chunk:
			size = host.stat().st_size if host.exists() else 0
			now = time.time()
			if size != quiet_size:
				quiet_size = size
				quiet_at = now
			elif now - quiet_at >= 2:
				return "restart", chunk, cat_chunk
		elif "error CS" in chunk or "dev-overlay id=" in cat_chunk:
			if expect == "overlay":
				return "overlay", chunk, cat_chunk
		if "swapped " in chunk:
			if expect == "swap":
				return "swap", chunk, cat_chunk
		if "reloaded in " in chunk and expect == "apply":
			return "apply", chunk, cat_chunk
		time.sleep(0.1)
	chunk, cat_chunk = read_from(host, host_at), dump_logcat(device)
	return "timeout", chunk, cat_chunk


def reload_times(chunk: str) -> list[float]:
	times: list[float] = []
	for line in chunk.splitlines():
		if line.startswith("reloaded in "):
			try:
				times.append(float(line.split()[2]))
			except (IndexError, ValueError):
				continue
		if line.startswith("body-swap "):
			try:
				times.append(float(line.split()[1]))
			except (IndexError, ValueError):
				continue
	return times


def main() -> None:
	parser = argparse.ArgumentParser()
	parser.add_argument("--device", default=os.environ.get("ANDROID_SERIAL", "emulator-5554"))
	parser.add_argument("--check", action="store_true")
	args = parser.parse_args()
	edits = build_edits()
	if len(edits) < 150:
		raise SystemExit(f"corpus has {len(edits)} edits")
	check(edits)
	kinds: dict[str, int] = {}
	for edit in edits:
		kinds[edit.kind] = kinds.get(edit.kind, 0) + 1
	print(f"corpus={len(edits)} " + " ".join(f"{name}={count}" for name, count in sorted(kinds.items())))
	if args.check:
		return
	sys.exit(run_corpus(args.device, edits))


if __name__ == "__main__":
	main()
