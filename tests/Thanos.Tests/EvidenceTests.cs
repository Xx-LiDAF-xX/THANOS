using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Tests;

public sealed class EvidenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-tests-" + Guid.NewGuid().ToString("N"));
    private readonly GameFingerprinter fingerprints = new(new LocalFileSystem(), new LocalPathService());
    private readonly KytyLogParser logs = new(new LocalFileSystem(), new LocalPathService());
    public EvidenceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private async Task<string> FileAsync(string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
    private static byte[] Elf(bool bigEndian = false, bool is64 = true)
    {
        var data = new byte[is64 ? 64 : 52];
        data[0] = 0x7f; data[1] = 0x45; data[2] = 0x4c; data[3] = 0x46;
        data[4] = is64 ? (byte)2 : (byte)1; data[5] = bigEndian ? (byte)2 : (byte)1; data[6] = 1;
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), 2);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18), is64 ? (ushort)62 : (ushort)3);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 1);
            if (is64) { BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(24), 0x401000); }
            else { BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), 0x401000); }
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(is64 ? 52 : 40), (ushort)data.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), is64 ? (ushort)62 : (ushort)3);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1);
            if (is64) { BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), 0x401000); }
            else { BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 0x401000); }
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(is64 ? 52 : 40), (ushort)data.Length);
        }
        return data;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ElfHeadersPreserveAddressSpaceAndHash(bool bigEndian, bool is64)
    {
        var bytes = Elf(bigEndian, is64);
        var path = await FileAsync("game.elf", bytes);
        var result = await fingerprints.FingerprintAsync(path);
        Assert.Equal(is64 ? "x86_64" : "x86", result.Architecture);
        Assert.Equal(new BinaryAddress(AddressSpace.VirtualAddress, "0x401000"), result.EntryPoint);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Null(result.TitleId);
        Assert.Null(result.GameFamily);
        Assert.Equal(AnalysisStatus.Partial, result.Status);
        Assert.Equal(0, result.Confidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(63)]
    public async Task TruncatedElfStillHasHashButNoInventedArchitecture(int size)
    {
        var result = await fingerprints.FingerprintAsync(await FileAsync("broken.elf", Elf()[..size]));
        Assert.NotNull(result.Sha256);
        Assert.Null(result.Architecture);
        Assert.Null(result.EntryPoint);
    }

    [Fact]
    public async Task ProtectedOrUnknownContentDoesNotBlockLocalEvidence()
    {
        var result = await fingerprints.FingerprintAsync(await FileAsync("eboot.bin", Encoding.UTF8.GetBytes("uninspectable data")));
        Assert.NotNull(result.Sha256);
        Assert.Null(result.Architecture);
        Assert.Contains(result.Limitations, l => l.Contains("UNKNOWN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectoryAmbiguityDoesNotChooseRandomExecutable()
    {
        await FileAsync("a.elf", Elf());
        await FileAsync("b.elf", Elf());
        await FileAsync("module.sprx", [1, 2, 3]);
        var result = await fingerprints.FingerprintAsync(directory);
        Assert.Null(result.ExecutablePath);
        Assert.Null(result.Sha256);
        Assert.Equal(["module.sprx"], result.Modules);
    }

    [Fact]
    public async Task DirectorySelectsUniqueEbootAndRecordsModules()
    {
        var path = await FileAsync("eboot.bin", Elf());
        await FileAsync("lib.sprx", [1]);
        var result = await fingerprints.FingerprintAsync(directory);
        Assert.Equal(path, result.ExecutablePath);
        Assert.Equal(["lib.sprx"], result.Modules);
    }

    [Fact]
    public async Task MissingGameReportsInputError() => await Assert.ThrowsAsync<FileNotFoundException>(() => fingerprints.FingerprintAsync(Path.Combine(directory, "missing")));

    [Fact]
    public async Task CancellationIsHonored()
    {
        var path = await FileAsync("game.elf", Elf());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fingerprints.FingerprintAsync(path, canceled.Token));
    }

    [Fact]
    public async Task LogKeepsTimelineRawEvidenceAndReportedHardware()
    {
        const string raw = "Kyty version: custom\r\nGPU: example\r\nTitle ID: TEST00001\r\nShaderCFG.cpp:40 fatal unsupported family=MIMG PC=0x123\r\nVulkan error VK_ERROR_DEVICE_LOST\r\nunknown line\r\n";
        var parsed = await logs.ParseAsync(await FileAsync("kyty.log", Encoding.UTF8.GetBytes(raw)));
        Assert.Equal(raw, parsed.RawText);
        Assert.Equal(6, parsed.Events.Length);
        var shader = parsed.Events[3];
        Assert.Equal(ErrorCategory.Shader, shader.Category);
        Assert.Equal(Severity.Fatal, shader.Severity);
        Assert.Equal("MIMG", shader.Opcode);
        Assert.Equal(40, shader.SourceLine);
        Assert.Equal(4, shader.LineNumber);
        Assert.Equal(AddressSpace.RuntimeAddress, shader.ProgramCounter!.AddressSpace);
        Assert.Equal("custom", parsed.KytyVersion);
        Assert.Equal("example", parsed.Hardware["gpu"]);
        Assert.Equal(ErrorCategory.Unknown, parsed.Events[^1].Category);
        Assert.Equal(ErrorCategory.Vulkan, parsed.Events[4].Category);
        Assert.Equal(Severity.Information, parsed.Events[^1].Severity);
    }

    [Fact]
    public async Task EmptyLogDoesNotInventSuccess()
    {
        var parsed = await logs.ParseAsync(await FileAsync("empty.log", []));
        Assert.Empty(parsed.Events);
        Assert.Null(parsed.KytyVersion);
        Assert.Empty(parsed.Hardware);
    }

    [Fact]
    public async Task OversizedLogRejectedBeforeParsing()
    {
        var path = Path.Combine(directory, "large.log");
        await using (var file = File.Create(path)) { file.SetLength(KytyLogParser.MaximumBytes + 1L); }
        await Assert.ThrowsAsync<InvalidDataException>(() => logs.ParseAsync(path));
    }

    [Fact]
    public async Task OversizedLineRejected()
    {
        var path = await FileAsync("long.log", Encoding.UTF8.GetBytes(new string('x', KytyLogParser.MaximumLineLength + 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => logs.ParseAsync(path));
    }

    [Fact]
    public async Task InvalidUtf8Rejected()
    {
        var path = await FileAsync("bad.log", [0xff, 0xff]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => logs.ParseAsync(path));
    }

    [Fact]
    public async Task ManyShortLinesCannotExpandIntoUnboundedEventObjects()
    {
        var path = await FileAsync("many.log", Encoding.UTF8.GetBytes(new string('\n', KytyLogParser.MaximumLines + 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => logs.ParseAsync(path));
    }

    [Fact]
    public async Task InvalidElfHeaderDoesNotProduceTrustedAddresses()
    {
        var data = Elf();
        data[52] = 0;
        var result = await fingerprints.FingerprintAsync(await FileAsync("invalid.elf", data));
        Assert.Null(result.EntryPoint);
        Assert.Null(result.Architecture);
        Assert.NotNull(result.Sha256);
    }

    [Fact]
    public async Task KytyDetectionNeverExecutesOrClaimsUnknownCapabilities()
    {
        var result = await new KytyCapabilityDetector(fingerprints).DetectAsync(await FileAsync("Kyty", [1, 2, 3]));
        Assert.Equal("Unknown", result.CapabilityStatus);
        Assert.Empty(result.Capabilities);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task SharedAnalysisSupportsPartialEvidence()
    {
        var path = await FileAsync("game.elf", Elf());
        var result = await ServiceFactory.CreateAnalysisService().AnalyzeAsync(path, null, null);
        Assert.Equal(1, result.SchemaVersion);
        Assert.NotNull(result.Game.Sha256);
        Assert.Null(result.Kyty);
        Assert.Null(result.Log);
    }
}
