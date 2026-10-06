using System.Buffers.Binary;
using System.Text;

namespace Thanos.Tests;

internal static class ElfFixture
{
    // Fully synthetic ELF with .text, dynamic symbols, a RELA relocation, and .bss.
    public static byte[] Create(bool is64 = true, bool bigEndian = false)
    {
        var bytes = new byte[1536];
        bytes[0] = 0x7f; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = is64 ? (byte)2 : (byte)1; bytes[5] = bigEndian ? (byte)2 : (byte)1; bytes[6] = 1;
        U16(16, 2); U16(18, is64 ? (ushort)62 : (ushort)3); U32(20, 1);
        Word(24, 0x400100); Word(is64 ? 32 : 28, is64 ? 64UL : 52UL); Word(is64 ? 40 : 32, 1024);
        U16(is64 ? 52 : 40, is64 ? (ushort)64 : (ushort)52);
        U16(is64 ? 54 : 42, is64 ? (ushort)56 : (ushort)32); U16(is64 ? 56 : 44, 1);
        U16(is64 ? 58 : 46, is64 ? (ushort)64 : (ushort)40); U16(is64 ? 60 : 48, 7); U16(is64 ? 62 : 50, 2);
        var ph = is64 ? 64 : 52;
        U32(ph, 1);
        if (is64) { U32(ph + 4, 5); U64(ph + 8, 256); U64(ph + 16, 0x400100); U64(ph + 32, 160); U64(ph + 40, 192); U64(ph + 48, 16); }
        else { U32(ph + 4, 256); U32(ph + 8, 0x400100); U32(ph + 16, 160); U32(ph + 20, 192); U32(ph + 24, 5); U32(ph + 28, 16); }
        for (var i = 256; i < 320; i++) { bytes[i] = (byte)(0x80 + i % 32); }
        var names = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0.dynstr\0.dynsym\0.rela.text\0.bss\0");
        names.CopyTo(bytes, 512);
        var dynstr = Encoding.ASCII.GetBytes("\0sceExample\0game_main\0"); dynstr.CopyTo(bytes, 576);
        Section(1, 1, 1, 6, 0x400100, 256, 64, 0, 0, 0);
        Section(2, 7, 3, 0, 0, 512, (ulong)names.Length, 0, 0, 0);
        Section(3, 17, 3, 0, 0, 576, (ulong)dynstr.Length, 0, 0, 0);
        Section(4, 25, 11, 0, 0, 640, is64 ? 72UL : 48UL, 3, 1, is64 ? 24UL : 16UL);
        Section(5, 33, 4, 0, 0, 736, is64 ? 24UL : 12UL, 4, 1, is64 ? 24UL : 12UL);
        Section(6, 44, 8, 3, 0x4001a0, 1536, 32, 0, 0, 0);
        Symbol(1, 1, 0, 0, 0); Symbol(2, 12, 1, 0x400110, 16);
        if (is64) { U64(736, 0x400118); U64(744, (2UL << 32) | 7); U64(752, unchecked((ulong)-4L)); }
        else { U32(736, 0x400118); U32(740, (2U << 8) | 7); U32(744, unchecked((uint)-4)); }
        return bytes;

        void Section(int index, uint name, uint type, ulong flags, ulong address, ulong offset, ulong size, uint link, uint info, ulong entrySize)
        {
            var pos = 1024 + index * (is64 ? 64 : 40);
            U32(pos, name); U32(pos + 4, type); Word(pos + 8, flags);
            Word(pos + (is64 ? 16 : 12), address); Word(pos + (is64 ? 24 : 16), offset); Word(pos + (is64 ? 32 : 20), size);
            U32(pos + (is64 ? 40 : 24), link); U32(pos + (is64 ? 44 : 28), info);
            Word(pos + (is64 ? 48 : 32), 1); Word(pos + (is64 ? 56 : 36), entrySize);
        }
        void Symbol(int index, uint name, ushort sectionIndex, ulong address, ulong size)
        {
            var pos = 640 + index * (is64 ? 24 : 16);
            U32(pos, name); bytes[pos + (is64 ? 4 : 12)] = 0x12;
            U16(pos + (is64 ? 6 : 14), sectionIndex);
            Word(pos + (is64 ? 8 : 4), address); Word(pos + (is64 ? 16 : 8), size);
        }
        void Word(int offset, ulong value) { if (is64) { U64(offset, value); } else { U32(offset, (uint)value); } }
        void U16(int offset, ushort value) { if (bigEndian) { BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value); } else { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value); } }
        void U32(int offset, uint value) { if (bigEndian) { BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value); } else { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value); } }
        void U64(int offset, ulong value) { if (bigEndian) { BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(offset), value); } else { BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value); } }
    }
}
