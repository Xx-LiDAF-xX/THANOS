"""Synthetic interoperability and corruption tests for the bundled ZArchive reader."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("zarchive_reader", ROOT / "src/Thanos.Infrastructure/InputHelpers/zarchive_reader.py")
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)


def zar(payload, name="eboot.bin", compressed=True):
    raw = payload.ljust(max(1, (len(payload) + 65535) // 65536) * 65536, b"\0")
    blocks = []
    for offset in range(0, len(raw), 65536):
        block = raw[offset:offset + 65536]
        if compressed:
            try:
                from compression import zstd
                encoded = zstd.compress(block)
            except ImportError:
                import zstandard
                encoded = zstandard.ZstdCompressor().compress(block)
            if len(encoded) < 65536:
                block = encoded
        blocks.append(block)
    name_bytes = name.encode("utf-8")
    length = len(name_bytes)
    prefix = bytes([length]) if length < 128 else bytes([(length & 127) | 128, length >> 7])
    names = b"\0" + prefix + name_bytes
    records = b""
    base = 0
    for index in range(0, len(blocks), 16):
        group = blocks[index:index + 16]
        sizes = [len(block) - 1 for block in group]
        records += struct.pack(">Q16H", base, *(sizes + [0] * (16 - len(sizes))))
        base += sum(len(block) for block in group)
    tree = struct.pack(">8I", 0, 1, 1, 0, 0x80000001, 0, len(payload), 0)
    sections = [b"".join(blocks), records, names, tree, b"", b""]
    offsets = []
    offset = 0
    for section in sections:
        offsets.extend([offset, len(section)])
        offset += len(section)
    footer = struct.pack(">12Q", *offsets) + bytes(32) + struct.pack(">QII", offset + 144, 0x61BF3A01, 0x169F52D6)
    return b"".join(sections) + footer


class ZArchiveTests(unittest.TestCase):
    def check_archive(self, data, expected, name="eboot.bin"):
        import io
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "game.zar"
            path.write_bytes(data)
            archive = reader.Archive(path)
            try:
                [(actual_name, offset, size)] = list(archive.entries())
                self.assertEqual(name, actual_name)
                output = io.BytesIO()
                archive.copy(offset, size, output)
                self.assertEqual(expected, output.getvalue())
            finally:
                archive.file.close()

    def test_compressed_payload(self):
        payload = b"\x7fELF" + b"shader fixture" * 100
        self.check_archive(zar(payload), payload)

    def test_uncompressed_payload(self):
        self.check_archive(zar(b"log\r\n", compressed=False), b"log\r\n")

    def test_extended_utf8_name(self):
        name = "游戏" * 30
        self.check_archive(zar(b"fixture", name), b"fixture", name)

    def test_cross_block_and_cross_offset_record_payload(self):
        payload = bytes(range(256)) * (17 * 256) + b"tail"
        self.check_archive(zar(payload), payload)

    def test_corrupt_zstd_does_not_return_partial_success(self):
        data = bytearray(zar(b"fixture"))
        data[0:4] = b"BAD!"
        with self.assertRaises(Exception):
            self.check_archive(data, b"fixture")

    def test_overlapping_sections_rejected(self):
        data = bytearray(zar(b"fixture"))
        struct.pack_into(">Q", data, len(data) - 144 + 16, 0)
        with self.assertRaises(ValueError):
            self.check_archive(data, b"fixture")


if __name__ == "__main__":
    unittest.main()
