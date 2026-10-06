using System.Buffers.Binary;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Tests;

public sealed class BinaryResearchTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-binary-" + Guid.NewGuid().ToString("N"));
    public BinaryResearchTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private async Task<string> WriteAsync(byte[] bytes)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".elf");
        await File.WriteAllBytesAsync(path, bytes); return path;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task InspectRealStructuresInBothElfClassesAndByteOrders(bool is64, bool bigEndian)
    {
        var data = ElfFixture.Create(is64, bigEndian);
        var path = await WriteAsync(data);
        var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path);
        Assert.Equal(BinaryInspectionStatus.Inspected, result.Status);
        Assert.Equal(is64 ? "ELF64" : "ELF32", result.Format);
        Assert.Equal(bigEndian ? "big" : "little", result.ByteOrder);
        Assert.Equal(6, result.Fingerprint.Sections.Length);
        Assert.Contains(result.Fingerprint.Sections, s => s.Name == ".bss" && s.Size == 32);
        Assert.Equal(["sceExample"], result.Imports);
        Assert.Equal(["game_main"], result.Exports);
        Assert.Equal(3, result.Symbols.Length);
        var relocation = Assert.Single(result.Relocations);
        Assert.Equal("game_main", relocation.Symbol);
        Assert.Equal(-4, relocation.Addend);
        Assert.Equal(AddressSpace.VirtualAddress, relocation.Location.AddressSpace);
        Assert.Contains(result.CodeSignatures, s => s.Kind == "section" && s.Size == 64);
        Assert.Contains(result.CodeSignatures, s => s.Kind == "symbolFunction" && s.Name == "game_main");
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task AddressesRequireUniqueFileBackedMappingAndExplicitRuntimeBias()
    {
        var binary = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(ElfFixture.Create()));
        var mapper = new AddressMapper();
        var mapped = mapper.Map(binary, new BinaryAddress(AddressSpace.VirtualAddress, "0x400118"), AddressSpace.FileOffset);
        Assert.Equal("0x118", mapped.Output!.Value);
        Assert.Null(mapper.Map(binary, new BinaryAddress(AddressSpace.VirtualAddress, "0x4001a0"), AddressSpace.FileOffset).Output);
        Assert.Null(mapper.Map(binary, new BinaryAddress(AddressSpace.RuntimeAddress, "0x500118"), AddressSpace.FileOffset).Output);
        Assert.Equal("0x118", mapper.Map(binary, new BinaryAddress(AddressSpace.RuntimeAddress, "0x500118"), AddressSpace.FileOffset, 0x100000).Output!.Value);
        Assert.Equal("0x500118", mapper.Map(binary, new BinaryAddress(AddressSpace.FileOffset, "0x118"), AddressSpace.RuntimeAddress, 0x100000).Output!.Value);
        Assert.Null(mapper.Map(binary, new BinaryAddress(AddressSpace.RelativeAddress, "0x18"), AddressSpace.FileOffset).Output);
        var overlap = binary with { LoadMappings = [.. binary.LoadMappings, binary.LoadMappings[0]] };
        Assert.Null(mapper.Map(overlap, new BinaryAddress(AddressSpace.VirtualAddress, "0x400118"), AddressSpace.FileOffset).Output);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("count")]
    [InlineData("link")]
    [InlineData("size")]
    [InlineData("overflow")]
    [InlineData("symbol")]
    public async Task MalformedTablesNeverExposeTrustedMappings(string problem)
    {
        var data = ElfFixture.Create();
        switch (problem)
        {
            case "offset": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40), ulong.MaxValue); break;
            case "count": BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60), 1000); break;
            case "link": BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1024 + 4 * 64 + 40), 999); break;
            case "size": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(64 + 40), 1); break;
            case "overflow": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(64 + 16), ulong.MaxValue - 1); break;
            case "symbol": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(744), 999UL << 32); break;
        }
        var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(data));
        Assert.Equal(BinaryInspectionStatus.Malformed, result.Status);
        Assert.Empty(result.LoadMappings);
        Assert.Empty(result.CodeSignatures);
        Assert.NotNull(result.Fingerprint.Sha256);
    }

    [Fact]
    public async Task ExtendedSectionAndNameNumberingUsesSectionZero()
    {
        var data = ElfFixture.Create();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(62), 0xffff);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1024 + 32), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1024 + 40), 2);
        var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(data));
        Assert.Equal(BinaryInspectionStatus.Inspected, result.Status);
        Assert.Equal(6, result.Fingerprint.Sections.Length);
    }

    [Fact]
    public async Task ResourceCapsAndOpaqueContentRemainPartial()
    {
        var data = ElfFixture.Create();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60), ElfBinaryAnalyzer.MaximumTableEntries + 1);
        var capped = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(data));
        Assert.Equal(BinaryInspectionStatus.Limited, capped.Status);
        Assert.Empty(capped.LoadMappings);
        var opaque = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync([1, 2, 3]));
        Assert.Equal(BinaryInspectionStatus.Unsupported, opaque.Status);
        Assert.NotNull(opaque.Fingerprint.Sha256);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(52)]
    [InlineData(63)]
    public async Task TruncatedIdentificationAndHeadersCannotCrashInspection(int length)
    {
        var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(ElfFixture.Create()[..length]));
        Assert.Equal(length < 4 ? BinaryInspectionStatus.Unsupported : BinaryInspectionStatus.Malformed, result.Status);
        Assert.Empty(result.LoadMappings);
        Assert.NotNull(result.Fingerprint.Sha256);
    }

    [Fact]
    public async Task DeterministicMalformedTableMutationsRemainInsideInspectionBoundary()
    {
        var random = new Random(17);
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var data = ElfFixture.Create();
            var start = new[] { 32, 40, 1024 + 64 + 24, 1024 + 4 * 64 + 32, 1024 + 4 * 64 + 40 }[iteration % 5];
            random.NextBytes(data.AsSpan(start, 8));
            var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(data));
            Assert.True(result.Status is BinaryInspectionStatus.Malformed or BinaryInspectionStatus.Limited);
            Assert.Empty(result.LoadMappings);
        }
    }

    [Fact]
    public async Task StrippedElfCodeIsHashedWithoutInventingSymbols()
    {
        var data = ElfFixture.Create();
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(62), 0);
        var result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(await WriteAsync(data));
        Assert.Empty(result.Symbols);
        Assert.Single(result.LoadMappings);
        Assert.Contains(result.CodeSignatures, s => s.Kind == "segment");
        Assert.Contains(result.Limitations, l => l.Contains("Section headers absent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DifferenceRangesCrossBlocksAndDetectTrailingBytes()
    {
        var original = new byte[65540];
        var candidate = new byte[65543];
        candidate[65535] = 1; candidate[65536] = 2;
        candidate[65542] = 3;
        var files = new LocalFileSystem();
        var differ = new BinaryDiffer(files, new GameFingerprinter(files, new LocalPathService()));
        var result = await differ.CompareAsync(await WriteAsync(original), await WriteAsync(candidate));
        Assert.Equal(5, result.ChangedBytes);
        Assert.Equal(2, result.Ranges.Length);
        Assert.Equal("0xffff", result.Ranges[0].Offset.Value);
        Assert.Equal(2, result.Ranges[0].OriginalSize);
        Assert.Equal(0, result.Ranges[1].OriginalSize);
        Assert.Equal(3, result.Ranges[1].CandidateSize);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task IdenticalFilesHaveNoDifferenceRanges()
    {
        var path = await WriteAsync(ElfFixture.Create());
        var files = new LocalFileSystem();
        var result = await new BinaryDiffer(files, new GameFingerprinter(files, new LocalPathService())).CompareAsync(path, path);
        Assert.Equal(0, result.ChangedBytes); Assert.Empty(result.Ranges);
    }
}
