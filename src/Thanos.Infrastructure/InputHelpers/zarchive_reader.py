"""Read-only ZArchive v1 member reader. Independent implementation of the public format.

Format: Exzap/ZArchive at d2c717730092c7bf8cbb033b12fd4001b7c4d932.
No game paths are ever extracted. File payloads go to stdout in 64 KiB blocks.
"""
import json
import os
import struct
import sys


class Archive:
    def __init__(self, path):
        self.file = open(path, "rb")
        try:
            self.initialize()
        except Exception:
            self.file.close()
            raise

    def initialize(self):
        self.size = os.fstat(self.file.fileno()).st_size
        if self.size <= 144:
            raise ValueError("Truncated ZArchive footer")
        footer = self.read(self.size - 144, 144)
        total, version, magic = struct.unpack_from(">QII", footer, 128)
        if (total, version, magic) != (self.size, 0x61BF3A01, 0x169F52D6):
            raise ValueError("Invalid ZArchive size, version or magic")
        self.sections = [struct.unpack_from(">QQ", footer, i * 16) for i in range(6)]
        for offset, size in self.sections:
            if offset > self.size - 144 or size > self.size - 144 - offset:
                raise ValueError("ZArchive section outside archive")
        occupied = sorted((o, o + s) for o, s in self.sections if s)
        if any(a[1] > b[0] for a, b in zip(occupied, occupied[1:])):
            raise ValueError("Overlapping ZArchive sections")
        self.data, self.records, self.names, self.tree = self.sections[:4]
        if not self.records[1] or self.records[1] % 40 or not self.tree[1] or self.tree[1] % 16:
            raise ValueError("Invalid ZArchive table sizes")
        self.count = self.tree[1] // 16
        root = self.node(0)
        if root[0] & 0x80000000 or self.name(root[0]) != "":
            raise ValueError("Invalid ZArchive root")

    def read(self, offset, length):
        if offset < 0 or length < 0 or offset > self.size or length > self.size - offset:
            raise ValueError("Out-of-range ZArchive read")
        self.file.seek(offset)
        data = self.file.read(length)
        if len(data) != length:
            raise ValueError("Truncated ZArchive data")
        return data

    def node(self, index):
        if not 0 <= index < self.count:
            raise ValueError("Invalid ZArchive tree index")
        return struct.unpack(">IIII", self.read(self.tree[0] + index * 16, 16))

    def name(self, flags):
        offset = flags & 0x7FFFFFFF
        if offset >= self.names[1]:
            raise ValueError("Invalid ZArchive name offset")
        first = self.read(self.names[0] + offset, 1)[0]
        length, header = first & 127, 1
        if first & 128:
            if offset + 1 >= self.names[1]:
                raise ValueError("Truncated ZArchive name length")
            length |= self.read(self.names[0] + offset + 1, 1)[0] << 7
            header = 2
        if header + length > self.names[1] - offset:
            raise ValueError("Truncated ZArchive name")
        return self.read(self.names[0] + offset + header, length).decode("utf-8", "strict")

    def entries(self):
        visited = set()
        pending = [(0, "")]
        while pending:
            index, path = pending.pop()
            if index in visited:
                raise ValueError("Cyclic or multiply referenced ZArchive tree")
            visited.add(index)
            flags, low_offset, low_size, high = self.node(index)
            if flags & 0x80000000:
                offset = low_offset | ((high & 65535) << 32)
                size = low_size | ((high >> 16) << 32)
                capacity = (self.records[1] // 40) * 16 * 65536
                if offset > capacity or size > capacity - offset:
                    raise ValueError("File exceeds ZArchive block inventory")
                yield path, offset, size
            else:
                if low_offset > self.count or low_size > self.count - low_offset:
                    raise ValueError("Invalid ZArchive directory range")
                for child in reversed(range(low_offset, low_offset + low_size)):
                    name = self.name(self.node(child)[0])
                    if not name or name in (".", "..") or any(c in name for c in "/\\\x00"):
                        raise ValueError("Invalid ZArchive member name")
                    pending.append((child, path + "/" + name if path else name))
        if len(visited) != self.count:
            raise ValueError("Unreachable ZArchive tree entries")

    def block(self, index):
        group, slot = divmod(index, 16)
        if group >= self.records[1] // 40:
            raise ValueError("Missing ZArchive block")
        record = self.read(self.records[0] + group * 40, 40)
        base = struct.unpack_from(">Q", record)[0]
        sizes = struct.unpack_from(">16H", record, 8)
        offset = base + sum(n + 1 for n in sizes[:slot])
        length = sizes[slot] + 1
        if offset > self.data[1] or length > self.data[1] - offset:
            raise ValueError("ZArchive block outside compressed data")
        data = self.read(self.data[0] + offset, length)
        if length != 65536:
            try:
                from compression import zstd
                decoder = zstd.ZstdDecompressor()
                data = decoder.decompress(data, max_length=65537)
                if not decoder.eof or decoder.unused_data:
                    raise ValueError("Invalid ZArchive zstd frame")
            except ImportError:
                try:
                    import zstandard
                except ImportError as error:
                    raise ValueError("Compressed ZAR requires Python 3.14 or the zstandard module") from error
                data = zstandard.ZstdDecompressor().decompress(data, max_output_size=65536)
        if len(data) != 65536:
            raise ValueError("ZArchive block does not expand to 64 KiB")
        return data

    def copy(self, offset, size, destination):
        while size:
            block, start = divmod(offset, 65536)
            amount = min(size, 65536 - start)
            destination.write(self.block(block)[start:start + amount])
            offset += amount
            size -= amount


def main():
    if len(sys.argv) not in (3, 4) or sys.argv[1] not in ("list", "read"):
        raise ValueError("Usage: zarchive_reader.py list|read archive.zar [member]")
    archive = Archive(sys.argv[2])
    try:
        entries = list(archive.entries())
        names = [e[0] for e in entries]
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZArchive paths")
        if sys.argv[1] == "list":
            sys.stdout.buffer.write(json.dumps(names, ensure_ascii=False).encode("utf-8"))
        else:
            if len(sys.argv) != 4 or sys.argv[3] not in names:
                raise ValueError("ZArchive member not found")
            _, offset, size = entries[names.index(sys.argv[3])]
            archive.copy(offset, size, sys.stdout.buffer)
    finally:
        archive.file.close()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(3)
