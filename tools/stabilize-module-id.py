#!/usr/bin/env python3
"""Pin a managed assembly's module id so a second clean publish matches.

The Android resource designer assembly is rewritten with a wall-clock PE
timestamp. The C# compiler then hashes that reference into this assembly's
module id, CodeView GUID, and PDB checksum, and the linker writes a new
module id on the way to ILC. ilc 10.0.14 has no working --deterministic
switch. Zip entry dates are already 1981-01-01.

This rewrites those stamp fields in place. The module id becomes a SHA-256
of the assembly with the stamps cleared, so two builds of the same IL match.
A sibling portable PDB gets the same treatment and the CodeView GUID follows
it. Framework assemblies are not passed in.
"""

import hashlib
import pathlib
import struct
import sys

IMAGE_DEBUG_TYPE_CODEVIEW = 2
IMAGE_DEBUG_TYPE_PDBCHECKSUM = 19
RSDS = b"RSDS"


class Pe:
    def __init__(self, data: bytearray):
        if data[:2] != b"MZ":
            raise ValueError("not a PE")
        self.data = data
        self.pe = struct.unpack_from("<I", data, 0x3C)[0]
        if data[self.pe : self.pe + 4] != b"PE\0\0":
            raise ValueError("not a PE")
        self.magic = struct.unpack_from("<H", data, self.pe + 24)[0]
        if self.magic not in (0x10B, 0x20B):
            raise ValueError(f"unknown optional magic {self.magic:#x}")
        self.sections = self._sections()
        self.clr = self._data_directory(14)

    def _sections(self) -> list[tuple[int, int, int]]:
        data = self.data
        count = struct.unpack_from("<H", data, self.pe + 6)[0]
        optional = struct.unpack_from("<H", data, self.pe + 20)[0]
        start = self.pe + 24 + optional
        found = []
        for index in range(count):
            offset = start + index * 40
            virtual_size, virtual_address, raw_size, raw = struct.unpack_from(
                "<IIII", data, offset + 8
            )
            found.append((virtual_address, max(virtual_size, raw_size), raw))
        return found

    def _data_directory(self, index: int) -> tuple[int, int]:
        # NumberOfRvaAndSizes sits just before the directory array.
        entry = self.pe + 24 + (112 if self.magic == 0x20B else 96) + index * 8
        return struct.unpack_from("<II", self.data, entry)

    def rva(self, address: int) -> int:
        for virtual_address, span, raw in self.sections:
            if virtual_address <= address < virtual_address + span:
                return raw + (address - virtual_address)
        raise ValueError(f"RVA {address:#x} is outside the image")

    @property
    def timestamp_at(self) -> int:
        return self.pe + 8

    def metadata_offset(self) -> int:
        rva, size = self.clr
        if size < 16:
            raise ValueError("assembly has no CLI header")
        header = self.rva(rva)
        strong_rva, strong_size = struct.unpack_from("<II", self.data, header + 32)
        if strong_size:
            # The resource designer reserves a signature slot and leaves it
            # zero. A real signature is not ours to recompute.
            blob = self.rva(strong_rva)
            if any(self.data[blob : blob + strong_size]):
                raise ValueError("refusing to rewrite a strong-named assembly")
        meta_rva = struct.unpack_from("<I", self.data, header + 8)[0]
        return self.rva(meta_rva)

    def debug_entries(self) -> list[tuple[int, int, int, int]]:
        rva, size = self._data_directory(6)
        if size == 0:
            return []
        start = self.rva(rva)
        entries = []
        for index in range(size // 28):
            offset = start + index * 28
            _chars, _stamp, _maj, _minor, typ, data_size, address, pointer = (
                struct.unpack_from("<IIHHIIII", self.data, offset)
            )
            data_at = 0
            if data_size and (pointer or address):
                data_at = pointer if pointer else self.rva(address)
            # offset is the directory entry; data_at is the payload, or 0.
            entries.append((offset, typ, data_size, data_at))
        return entries


def stream_headers(data: bytearray, meta: int) -> list[tuple[str, int, int]]:
    if data[meta : meta + 4] != b"BSJB":
        raise ValueError("metadata root is missing")
    version_length = struct.unpack_from("<I", data, meta + 12)[0]
    header = meta + 16 + ((version_length + 3) & ~3)
    count = struct.unpack_from("<H", data, header + 2)[0]
    cursor = header + 4
    found = []
    for _ in range(count):
        offset, size = struct.unpack_from("<II", data, cursor)
        cursor += 8
        end = data.index(0, cursor)
        name = data[cursor:end].decode("utf-8")
        name_length = end - cursor + 1
        cursor += (name_length + 3) & ~3
        found.append((name, meta + offset, size))
    return found


def mvid_span(data: bytearray, meta: int) -> tuple[int, int]:
    for name, offset, size in stream_headers(data, meta):
        if name == "#GUID":
            if size < 16:
                raise ValueError("#GUID stream is shorter than a module id")
            return offset, 16
    raise ValueError("assembly has no #GUID stream")


def pdb_id_span(data: bytearray) -> tuple[int, int]:
    for name, offset, size in stream_headers(data, 0):
        if name == "#Pdb":
            if size < 20:
                raise ValueError("#Pdb stream is shorter than an id")
            return offset, 20
    raise ValueError("portable PDB has no #Pdb stream")


def zero(data: bytearray, offset: int, size: int) -> None:
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError(f"span {offset:#x}+{size} is outside the file")
    data[offset : offset + size] = b"\0" * size


def write(data: bytearray, offset: int, blob: bytes) -> None:
    if offset < 0 or offset + len(blob) > len(data):
        raise ValueError(f"write {offset:#x}+{len(blob)} is outside the file")
    data[offset : offset + len(blob)] = blob


def stabilize_pdb(path: pathlib.Path) -> bytes | None:
    data = bytearray(path.read_bytes())
    if data[:2] == b"MZ":
        # A PE-wrapped PDB is not what this SDK writes. Leave it untouched.
        return None
    if data[:4] != b"BSJB":
        raise ValueError(f"{path} is not a portable PDB")
    offset, _size = pdb_id_span(data)
    zero(data, offset, 20)
    digest = hashlib.sha256(data).digest()
    write(data, offset, digest[:16])
    path.write_bytes(data)
    return digest[:16]


def checksum_span(data: bytearray, pointer: int, size: int) -> tuple[int, int]:
    blob = data[pointer : pointer + size]
    end = blob.find(0)
    if end < 0 or end + 1 >= size:
        raise ValueError("PDB checksum entry has no algorithm name")
    start = pointer + end + 1
    return start, size - (end + 1)


def stabilize_dll(path: pathlib.Path) -> None:
    pdb_path = path.with_suffix(".pdb")
    pdb_guid = stabilize_pdb(pdb_path) if pdb_path.is_file() else None
    pdb_sha = hashlib.sha256(pdb_path.read_bytes()).digest() if pdb_guid is not None else None

    data = bytearray(path.read_bytes())
    image = Pe(data)
    meta = image.metadata_offset()
    module_at, module_len = mvid_span(data, meta)
    stamps = [image.timestamp_at]
    codeview: tuple[int, int] | None = None
    checksum: tuple[int, int] | None = None
    for offset, typ, size, pointer in image.debug_entries():
        stamps.append(offset + 4)
        if pointer == 0:
            continue
        if typ == IMAGE_DEBUG_TYPE_CODEVIEW and data[pointer : pointer + 4] == RSDS:
            codeview = (pointer + 4, 16)
        elif typ == IMAGE_DEBUG_TYPE_PDBCHECKSUM and size:
            checksum = checksum_span(data, pointer, size)

    for offset in stamps:
        zero(data, offset, 4)
    zero(data, module_at, module_len)
    if codeview is not None:
        zero(data, codeview[0], codeview[1])
    if checksum is not None:
        zero(data, checksum[0], checksum[1])

    if checksum is not None and pdb_sha is not None:
        if checksum[1] != len(pdb_sha):
            raise ValueError(f"PDB checksum is {checksum[1]} bytes, not SHA-256")
        write(data, checksum[0], pdb_sha)
    if codeview is not None and pdb_guid is not None:
        write(data, codeview[0], pdb_guid)

    digest = hashlib.sha256(data).digest()
    write(data, module_at, digest[:module_len])
    if codeview is not None and pdb_guid is None:
        write(data, codeview[0], digest[: codeview[1]])
    if checksum is not None and pdb_sha is None:
        write(data, checksum[0], digest[: checksum[1]])
    path.write_bytes(data)
    print(f"stabilize {path} {hashlib.sha256(data).hexdigest()}")


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: stabilize-module-id.py <assembly.dll>...", file=sys.stderr)
        return 2
    for arg in sys.argv[1:]:
        path = pathlib.Path(arg)
        if not path.is_file():
            print(f"missing {path}", file=sys.stderr)
            return 1
        try:
            stabilize_dll(path)
        except ValueError as error:
            print(f"{path}: {error}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
