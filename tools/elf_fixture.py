"""Synthetic ELF64 executable for CLI integration; no game/emulator content."""
import struct


def create():
    data = bytearray(768)
    data[:7] = b"\x7fELF\x02\x01\x01"
    struct.pack_into("<HHIQQQIHHHHHH", data, 16, 2, 62, 1, 0x400100, 64, 512, 0, 64, 56, 1, 64, 4, 2)
    struct.pack_into("<IIQQQQQQ", data, 64, 1, 5, 256, 0x400100, 0x400100, 64, 96, 16)
    data[256:320] = bytes(range(128, 192))
    names = b"\0.text\0.shstrtab\0.bss\0"
    data[384:384 + len(names)] = names
    struct.pack_into("<IIQQQQIIQQ", data, 576, 1, 1, 6, 0x400100, 256, 64, 0, 0, 16, 0)
    struct.pack_into("<IIQQQQIIQQ", data, 640, 7, 3, 0, 0, 384, len(names), 0, 0, 1, 0)
    struct.pack_into("<IIQQQQIIQQ", data, 704, 17, 8, 3, 0x400140, 768, 32, 0, 0, 16, 0)
    return data
