using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Thanos.Core;

namespace Thanos.Infrastructure;

/// <summary>Bounded, read-only SysV ELF inspection. No container decryption or instruction emulation.</summary>
public sealed class ElfBinaryAnalyzer(IFileSystem files, IPathService paths, IGameFingerprinter fingerprints) : IBinaryInspectionService, IBinaryAnalyzer
{
    public const int MaximumTableEntries = 4096;
    public const int MaximumSymbols = 50000;
    public const int MaximumStrings = 4096;
    public const int MaximumStringScanBytes = 32 * 1024 * 1024;
    private const int MaximumStringTableBytes = 8 * 1024 * 1024;
    private const long MaximumCodeHashBytes = 256 * 1024 * 1024;

    public async Task<GameFingerprint> AnalyzeAsync(string path, CancellationToken cancellationToken = default) => (await InspectAsync(path, cancellationToken)).Fingerprint;

    public async Task<BinaryInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!files.FileExists(path)) { throw new FileNotFoundException("Binary inspection requires a file.", path); }
        var fingerprint = await fingerprints.FingerprintAsync(path, cancellationToken);
        await using var stream = files.OpenRead(paths.GetFullPath(path));
        var reader = new Reader(stream, cancellationToken);
        var magic = reader.Read(0, (int)Math.Min(stream.Length, 64));
        if (magic.Length < 4 || !magic.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }))
        {
            return new BinaryInspection
            {
                Fingerprint = fingerprint,
                Status = BinaryInspectionStatus.Unsupported,
                Limitations = ["Not an accessible raw ELF. Hash/size remain usable; code, symbols and addresses are UNKNOWN. No decryption was attempted."]
            };
        }
        try
        {
            if (ElfHeader.Read(magic, stream.Length).Limitation is { } headerError) { throw new InvalidDataException(headerError); }
            reader.LittleEndian = magic[5] == 1;
            var is64 = magic[4] == 2;
            var elfType = reader.U16(magic, 16);
            var phOffset = is64 ? reader.U64(magic, 32) : reader.U32(magic, 28);
            var shOffset = is64 ? reader.U64(magic, 40) : reader.U32(magic, 32);
            var phEntrySize = reader.U16(magic, is64 ? 54 : 42);
            ulong phCount = reader.U16(magic, is64 ? 56 : 44);
            var shEntrySize = reader.U16(magic, is64 ? 58 : 46);
            ulong shCount = reader.U16(magic, is64 ? 60 : 48);
            ulong namesIndex = reader.U16(magic, is64 ? 62 : 50);
            var limits = new List<string> { "ELF structure/symbols are evidence, not disassembly. ASCII strings and code hashes are bounded; no automatic engine identity or function equivalence inference." };
            if (shOffset != 0)
            {
                if (shEntrySize != (is64 ? 64 : 40)) { throw new InvalidDataException("Invalid ELF section entry size."); }
                var zero = ReadSection(reader, shOffset, is64);
                if (zero.Type != 0) { throw new InvalidDataException("Section zero must be SHT_NULL."); }
                if (shCount == 0) { shCount = zero.Size; }
                if (namesIndex == 0xffff) { namesIndex = zero.Link; }
                if (phCount == 0xffff) { phCount = zero.Info; }
            }
            else if (shCount != 0 || namesIndex != 0 || phCount == 0xffff) { throw new InvalidDataException("Missing ELF section table or extended numbering data."); }
            Table(reader, shOffset, shCount, shEntrySize, "section");
            if (phCount != 0 && phEntrySize != (is64 ? 56 : 32)) { throw new InvalidDataException("Invalid ELF program entry size."); }
            Table(reader, phOffset, phCount, phEntrySize, "program");
            var sections = new List<Section>();
            for (ulong i = 0; i < shCount; i++)
            {
                var section = ReadSection(reader, shOffset + i * shEntrySize, is64);
                if (i != 0 && section.Type != 8) { reader.Range(section.Offset, section.Size); }
                Long(section.Size); AddressRange(section.Address, section.Size);
                sections.Add(section);
            }
            byte[] names = [];
            if (namesIndex != 0)
            {
                if (namesIndex >= (ulong)sections.Count || sections[(int)namesIndex].Type != 3) { throw new InvalidDataException("Invalid section-name string table index."); }
                names = StringTable(reader, sections[(int)namesIndex]);
            }
            var mappings = new List<LoadMapping>();
            var segments = new List<BinarySegment>();
            for (ulong i = 0; i < phCount; i++)
            {
                var ph = reader.Read(phOffset + i * phEntrySize, phEntrySize);
                var type = reader.U32(ph, 0);
                var offset = is64 ? reader.U64(ph, 8) : reader.U32(ph, 4);
                var address = is64 ? reader.U64(ph, 16) : reader.U32(ph, 8);
                var fileSize = is64 ? reader.U64(ph, 32) : reader.U32(ph, 16);
                var memorySize = is64 ? reader.U64(ph, 40) : reader.U32(ph, 20);
                var flags = reader.U32(ph, is64 ? 4 : 24);
                var align = is64 ? reader.U64(ph, 48) : reader.U32(ph, 28);
                reader.Range(offset, fileSize); AddressRange(address, memorySize);
                if (type == 1 && (fileSize > memorySize || (align > 1 && ((align & (align - 1)) != 0 || address % align != offset % align)))) { throw new InvalidDataException("Invalid PT_LOAD sizes or alignment."); }
                segments.Add(new BinarySegment(type == 1 ? "PT_LOAD" : $"0x{type:x}", At(AddressSpace.FileOffset, offset), At(AddressSpace.VirtualAddress, address), Long(fileSize), Long(memorySize)));
                if (type == 1) { mappings.Add(new LoadMapping(At(AddressSpace.FileOffset, offset), At(AddressSpace.VirtualAddress, address), Long(fileSize), Long(memorySize), flags)); }
            }
            var code = new List<CodeSignature>();
            var codeInventoryComplete = true;
            long hashBudget = MaximumCodeHashBytes;
            var namedSections = new List<BinarySection>();
            for (var i = 1; i < sections.Count; i++)
            {
                var section = sections[i];
                var name = names.Length == 0 ? $"section-{i}" : Text(names, section.Name);
                namedSections.Add(new BinarySection(name, At(AddressSpace.FileOffset, section.Offset), At(elfType == 1 ? AddressSpace.RelativeAddress : AddressSpace.VirtualAddress, section.Address), Long(section.Size)));
                if ((section.Flags & 0x800) != 0) { limits.Add($"Compressed section {i} is not decoded."); if ((section.Flags & 4) != 0) { codeInventoryComplete = false; } continue; }
                if ((section.Flags & 4) != 0 && section.Type == 1 && section.Size > 0)
                {
                    if (section.Size <= (ulong)hashBudget)
                    {
                        code.Add(new CodeSignature(name, At(AddressSpace.FileOffset, section.Offset), Long(section.Size), reader.Hash(section.Offset, section.Size), "section"));
                        hashBudget -= Long(section.Size);
                    }
                    else { codeInventoryComplete = false; limits.Add("Code hashing limited to 256 MiB; executable-section inventory is incomplete."); }
                }
            }
            // Stripped ELF files can have loadable executable code without a section table.
            if (sections.Count == 0)
            {
                foreach (var mapping in mappings.Where(m => (m.Flags & 1) != 0 && m.FileSize > 0))
                {
                    if (mapping.FileSize <= hashBudget)
                    {
                        var offset = AddressMapper.Parse(mapping.Offset);
                        code.Add(new CodeSignature("PT_LOAD executable", mapping.Offset, mapping.FileSize, reader.Hash(offset, (ulong)mapping.FileSize), "segment"));
                        hashBudget -= mapping.FileSize;
                    }
                    else { codeInventoryComplete = false; limits.Add("Code hashing limited to 256 MiB; executable-segment inventory is incomplete."); }
                }
            }
            var symbols = new List<SymbolInfo>();
            var symbolsByTable = new Dictionary<int, string[]>();
            var symbolNameCache = new Dictionary<(uint Table, uint Offset), string>();
            long symbolNameBudget = MaximumStringTableBytes;
            for (var i = 1; i < sections.Count; i++)
            {
                var section = sections[i];
                if (section.Type is not (2 or 11) || (section.Flags & 0x800) != 0) { continue; }
                if (section.Link >= sections.Count || sections[(int)section.Link].Type != 3) { throw new InvalidDataException("Invalid symbol string table link."); }
                var strings = StringTable(reader, sections[(int)section.Link]);
                var size = is64 ? 24 : 16;
                if (section.EntrySize != (ulong)size || section.Size % (ulong)size != 0) { throw new InvalidDataException("Invalid symbol table entry size."); }
                var count = section.Size / (ulong)size;
                if (count > (ulong)(MaximumSymbols - symbols.Count)) { throw new InspectionLimitException("Symbol inspection exceeds 50000 entries."); }
                var symbolNames = new string[(int)count];
                for (ulong index = 0; index < count; index++)
                {
                    var data = reader.Read(section.Offset + index * (ulong)size, size);
                    var nameOffset = reader.U32(data, 0);
                    if (!symbolNameCache.TryGetValue((section.Link, nameOffset), out var name))
                    {
                        name = Text(strings, nameOffset);
                        symbolNameBudget -= name.Length;
                        if (symbolNameBudget < 0) { throw new InspectionLimitException("Decoded symbol names exceed the 8 MiB character budget."); }
                        symbolNameCache.Add((section.Link, nameOffset), name);
                    }
                    var info = data[is64 ? 4 : 12];
                    var other = data[is64 ? 5 : 13] & 3;
                    var sectionIndex = reader.U16(data, is64 ? 6 : 14);
                    var value = is64 ? reader.U64(data, 8) : reader.U32(data, 4);
                    var symbolSize = is64 ? reader.U64(data, 16) : reader.U32(data, 8);
                    Long(symbolSize);
                    symbolNames[index] = name;
                    if (sectionIndex == 0xffff) { limits.Add("Extended symbol section indices are not decoded."); }
                    else if (sectionIndex >= sections.Count && sectionIndex < 0xff00) { throw new InvalidDataException("Symbol references an absent section."); }
                    var binding = info >> 4;
                    symbols.Add(new SymbolInfo(name, At(elfType == 1 ? AddressSpace.RelativeAddress : AddressSpace.VirtualAddress, value), Long(symbolSize), binding switch { 0 => "local", 1 => "global", 2 => "weak", _ => $"0x{binding:x}" },
                        (info & 15) switch { 0 => "none", 1 => "object", 2 => "function", 3 => "section", 4 => "file", _ => $"0x{info & 15:x}" }, sectionIndex,
                        section.Type == 11 && sectionIndex == 0 && name.Length > 0 && binding is 1 or 2,
                        section.Type == 11 && sectionIndex != 0 && name.Length > 0 && binding is 1 or 2 && other is 0 or 3));
                    if ((info & 15) == 2 && symbolSize > 0 && symbolSize <= 65536 && sectionIndex > 0 && sectionIndex < sections.Count && code.Count < 8192)
                    {
                        var target = sections[sectionIndex];
                        var relative = elfType == 1 ? value : value >= target.Address ? value - target.Address : ulong.MaxValue;
                        if (target.Type == 1 && (target.Flags & 4) != 0 && (target.Flags & 0x800) == 0 && relative <= target.Size && symbolSize <= target.Size - relative && symbolSize <= (ulong)hashBudget)
                        {
                            var offset = target.Offset + relative;
                            code.Add(new CodeSignature(name, At(AddressSpace.FileOffset, offset), Long(symbolSize), reader.Hash(offset, symbolSize), "symbolFunction"));
                            hashBudget -= Long(symbolSize);
                        }
                    }
                }
                symbolsByTable.Add(i, symbolNames);
            }
            var relocations = new List<RelocationInfo>();
            foreach (var section in sections.Where(s => s.Type is 4 or 9 && (s.Flags & 0x800) == 0))
            {
                var hasAddend = section.Type == 4;
                var size = is64 ? hasAddend ? 24 : 16 : hasAddend ? 12 : 8;
                if (section.EntrySize != (ulong)size || section.Size % (ulong)size != 0 || !symbolsByTable.TryGetValue((int)section.Link, out var namesInTable)) { throw new InvalidDataException("Invalid relocation entry size or symbol-table link."); }
                if (section.Info >= sections.Count) { throw new InvalidDataException("Invalid relocation target section."); }
                var count = section.Size / (ulong)size;
                if (count > (ulong)(MaximumSymbols - relocations.Count)) { throw new InspectionLimitException("Relocation inspection exceeds 50000 entries."); }
                for (ulong index = 0; index < count; index++)
                {
                    var data = reader.Read(section.Offset + index * (ulong)size, size);
                    var offset = is64 ? reader.U64(data, 0) : reader.U32(data, 0);
                    var info = is64 ? reader.U64(data, 8) : reader.U32(data, 4);
                    var symbol = is64 ? info >> 32 : info >> 8;
                    if (symbol >= (ulong)namesInTable.Length) { throw new InvalidDataException("Relocation references an absent symbol."); }
                    long? addend = hasAddend ? is64 ? unchecked((long)reader.U64(data, 16)) : unchecked((int)reader.U32(data, 8)) : null;
                    relocations.Add(new RelocationInfo(At(elfType == 1 ? AddressSpace.RelativeAddress : AddressSpace.VirtualAddress, offset), $"0x{(is64 ? info & 0xffffffff : info & 0xff):x}", namesInTable[symbol], addend));
                }
            }
            var stringsFound = ReadStrings(reader, stream.Length, limits);
            if (sections.Count == 0) { limits.Add("Section headers absent: symbols/imports/exports and relocations cannot be recovered by the section-table reader. Dynamic-segment recovery is not implemented."); }
            var updated = fingerprint with
            {
                Sections = namedSections.ToArray(),
                Segments = segments.ToArray(),
                Limitations = ["Game identity, version and engine remain UNKNOWN unless assigned through provenanced exact-hash knowledge.", .. limits],
                MetadataSources = [.. fingerprint.MetadataSources, new Evidence(fingerprint.ExecutablePath!, "Guarded ELF sections, segments, symbols, relocations, code hashes and bounded strings.")]
            };
            return new BinaryInspection
            {
                Fingerprint = updated,
                Status = limits.Count > 1 ? BinaryInspectionStatus.Limited : BinaryInspectionStatus.Inspected,
                Format = is64 ? "ELF64" : "ELF32",
                ByteOrder = reader.LittleEndian ? "little" : "big",
                Symbols = symbols.ToArray(),
                Imports = symbols.Where(s => s.IsImport).Select(s => s.Name).Distinct().Order(StringComparer.Ordinal).ToArray(),
                Exports = symbols.Where(s => s.IsExport).Select(s => s.Name).Distinct().Order(StringComparer.Ordinal).ToArray(),
                Relocations = relocations.ToArray(),
                Strings = stringsFound,
                CodeSignatures = code.ToArray(),
                CodeInventoryComplete = codeInventoryComplete,
                LoadMappings = mappings.ToArray(),
                Limitations = limits.Distinct().ToArray()
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or InspectionLimitException)
        {
            return new BinaryInspection
            {
                Fingerprint = fingerprint,
                Status = ex is InspectionLimitException ? BinaryInspectionStatus.Limited : BinaryInspectionStatus.Malformed,
                Format = magic.Length > 4 ? magic[4] == 2 ? "ELF64" : magic[4] == 1 ? "ELF32" : null : null,
                Limitations = [ex.Message, "Partial file hash/header evidence is retained; no mappings or incomplete table data are trusted."]
            };
        }
    }

    private static BinaryString[] ReadStrings(Reader reader, long length, List<string> limits)
    {
        var count = (int)Math.Min(length, MaximumStringScanBytes);
        var bytes = reader.Read(0, count);
        var result = new List<BinaryString>();
        for (var start = 0; start < bytes.Length;)
        {
            reader.CheckCancellation();
            if (bytes[start] is < 32 or > 126) { start++; continue; }
            var end = start;
            while (end < bytes.Length && bytes[end] is >= 32 and <= 126) { end++; }
            if (end - start >= 6)
            {
                if (result.Count == MaximumStrings) { limits.Add("ASCII string extraction limited to 4096 strings."); break; }
                if (end - start > 256) { limits.Add("Long ASCII strings are truncated to 256 characters."); }
                result.Add(new BinaryString(At(AddressSpace.FileOffset, (ulong)start), Encoding.ASCII.GetString(bytes, start, Math.Min(end - start, 256))));
            }
            start = end;
        }
        if (length > MaximumStringScanBytes) { limits.Add("ASCII extraction scans only the first 32 MiB. UTF-16 and other encodings are not decoded."); }
        return result.ToArray();
    }
    private static void Table(Reader reader, ulong offset, ulong count, ushort size, string name)
    {
        if (count > MaximumTableEntries) { throw new InspectionLimitException($"ELF {name} table exceeds 4096 entries."); }
        if (count != 0 && (offset == 0 || size == 0)) { throw new InvalidDataException($"Invalid ELF {name} table location."); }
        if (count != 0) { reader.Range(offset, count * size); }
    }
    private static Section ReadSection(Reader reader, ulong offset, bool is64)
    {
        var data = reader.Read(offset, is64 ? 64 : 40);
        return new Section(reader.U32(data, 0), reader.U32(data, 4), is64 ? reader.U64(data, 8) : reader.U32(data, 8),
            is64 ? reader.U64(data, 16) : reader.U32(data, 12), is64 ? reader.U64(data, 24) : reader.U32(data, 16),
            is64 ? reader.U64(data, 32) : reader.U32(data, 20), reader.U32(data, is64 ? 40 : 24), reader.U32(data, is64 ? 44 : 28), is64 ? reader.U64(data, 56) : reader.U32(data, 36));
    }
    private static byte[] StringTable(Reader reader, Section section)
    {
        if ((section.Flags & 0x800) != 0) { throw new InspectionLimitException("Compressed string tables are not decoded."); }
        if (section.Size > MaximumStringTableBytes) { throw new InspectionLimitException("ELF string table exceeds 8 MiB."); }
        return reader.Read(section.Offset, (int)section.Size);
    }
    private static string Text(byte[] table, uint offset)
    {
        if (table.Length == 0 && offset == 0) { return ""; }
        if (offset >= table.Length) { throw new InvalidDataException("String index exceeds ELF string table."); }
        var end = Array.IndexOf(table, (byte)0, (int)offset);
        if (end < 0) { throw new InvalidDataException("Unterminated ELF string."); }
        if (end - offset > 4096) { throw new InspectionLimitException("ELF name exceeds 4096 bytes."); }
        return Encoding.UTF8.GetString(table, (int)offset, end - (int)offset);
    }
    private static long Long(ulong value) => value <= long.MaxValue ? (long)value : throw new InvalidDataException("ELF size exceeds supported signed range.");
    private static void AddressRange(ulong address, ulong size) { Long(size); if (size > ulong.MaxValue - address) { throw new InvalidDataException("ELF virtual address range overflows."); } }
    private static BinaryAddress At(AddressSpace space, ulong value) => new(space, $"0x{value:x}");
    private sealed record Section(uint Name, uint Type, ulong Flags, ulong Address, ulong Offset, ulong Size, uint Link, uint Info, ulong EntrySize);
    private sealed class InspectionLimitException(string message) : Exception(message);

    private sealed class Reader(Stream stream, CancellationToken token)
    {
        public bool LittleEndian { get; set; }
        public void CheckCancellation() => token.ThrowIfCancellationRequested();
        public void Range(ulong offset, ulong count)
        {
            token.ThrowIfCancellationRequested();
            var length = (ulong)stream.Length;
            if (offset > length || count > length - offset) { throw new InvalidDataException("ELF file range extends beyond input."); }
        }
        public byte[] Read(ulong offset, int count)
        {
            Range(offset, (ulong)count);
            var result = new byte[count];
            stream.Position = (long)offset;
            stream.ReadExactly(result);
            return result;
        }
        public string Hash(ulong offset, ulong count)
        {
            Range(offset, count);
            stream.Position = (long)offset;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            while (count > 0)
            {
                token.ThrowIfCancellationRequested();
                var size = (int)Math.Min((ulong)buffer.Length, count);
                stream.ReadExactly(buffer.AsSpan(0, size));
                hash.AppendData(buffer, 0, size); count -= (ulong)size;
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        public ushort U16(byte[] data, int offset) => LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        public uint U32(byte[] data, int offset) => LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        public ulong U64(byte[] data, int offset) => LittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset));
    }
}
