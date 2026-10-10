using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Tests;

public sealed class ContainerInputTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-inputs-" + Guid.NewGuid().ToString("N"));
    public ContainerInputTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string PathFor(string name) => Path.Combine(directory, name);
    private string Zip(params (string Name, byte[] Data)[] entries)
    {
        var path = PathFor(Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var output = zip.CreateEntry(entry.Name).Open(); output.Write(entry.Data); }
        return path;
    }

    [Fact]
    public async Task ZipGameUsesMemberHashAndRunsSharedResearch()
    {
        var binary = ElfFixture.Create();
        var path = Zip(("Game/eboot.bin", binary), ("Game/module.sprx", [1]));
        var before = await File.ReadAllBytesAsync(path);
        var report = await ServiceFactory.CreateResearchService().ResearchAsync(path, null, null);
        Assert.Equal(BinaryInspectionStatus.Inspected, report.Binary!.Status);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(binary)), report.Evidence.Game.Sha256);
        Assert.Equal(path + "!/Game/eboot.bin", report.Evidence.Game.ExecutablePath);
        Assert.Equal(["Game/module.sprx"], report.Evidence.Game.Modules);
        Assert.Contains(report.Evidence.Game.MetadataSources, e => e.Source == path);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ZipLogPreservesBytesBomUnicodeAndNewlines()
    {
        var raw = "\ufeffKyty version: test\r\nGPU: 测试😀\nShader error unsupported family=MIMG PC=0x20\r\n";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var path = Zip(("logs/run.log", bytes), ("image.png", [1]));
        var parsed = await new KytyLogParser(new LocalFileSystem(), new LocalPathService()).ParseAsync(path);
        Assert.Equal(raw, parsed.RawText);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), parsed.Sha256);
        Assert.Equal(path + "!/logs/run.log", parsed.SourcePath);
        Assert.Equal("测试😀", parsed.Hardware["gpu"]);
    }

    [Fact]
    public async Task MultipleExecutablesStayUnknownAndExplicitMemberWorks()
    {
        var path = Zip(("A/eboot.bin", ElfFixture.Create()), ("B/eboot.bin", ElfFixture.Create()));
        var fingerprints = new GameFingerprinter(new LocalFileSystem(), new LocalPathService());
        Assert.Null((await fingerprints.FingerprintAsync(path)).Sha256);
        Assert.Equal(BinaryInspectionStatus.Unsupported, (await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path)).Status);
        Assert.Equal(BinaryInspectionStatus.Inspected, (await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path + "!/B/eboot.bin")).Status);
    }

    [Fact]
    public async Task MultipleLogsRequireSelectionAndMissingMemberFails()
    {
        var path = Zip(("a.log", Encoding.UTF8.GetBytes("first")), ("b.log", Encoding.UTF8.GetBytes("last")));
        var parser = new KytyLogParser(new LocalFileSystem(), new LocalPathService());
        await Assert.ThrowsAsync<InvalidDataException>(() => parser.ParseAsync(path));
        Assert.Equal("last", (await parser.ParseAsync(path + "!/b.log")).RawText);
        await Assert.ThrowsAsync<FileNotFoundException>(() => parser.ParseAsync(path + "!/missing.log"));
    }

    [Theory]
    [InlineData("../eboot.bin")]
    [InlineData("/eboot.bin")]
    [InlineData("C:/eboot.bin")]
    [InlineData("A\\eboot.bin")]
    [InlineData("*.elf")]
    public async Task UnsafeArchiveNamesRejected(string member)
    {
        var path = Zip((member, [1, 2, 3]));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContainerInputs.ListAsync(path));
    }

    [Fact]
    public async Task DuplicateAndSymlinkMembersRejected()
    {
        var duplicates = Zip(("eboot.bin", [1]), ("eboot.bin", [2]));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContainerInputs.ListAsync(duplicates));
        var path = PathFor("link.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("eboot.bin"); entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            using var stream = entry.Open(); stream.Write(Encoding.UTF8.GetBytes("outside"));
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ContainerInputs.ListAsync(path));
    }

    [Fact]
    public async Task CancellationDoesNotLeaveAnOpenArchive()
    {
        var path = Zip(("eboot.bin", ElfFixture.Create()));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContainerInputs.OpenReadAsync(path + "!/eboot.bin", cancellation.Token));
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task PackageEvidenceDoesNotBecomeAnExecutableHashOrAddress()
    {
        var path = PathFor("game.pkg"); var bytes = new byte[128];
        new byte[] { 0x7f, 0x43, 0x4e, 0x54 }.CopyTo(bytes, 0); await File.WriteAllBytesAsync(path, bytes);
        var report = await ServiceFactory.CreateResearchService().ResearchAsync(path, null, null);
        Assert.Null(report.Evidence.Game.Sha256);
        Assert.Null(report.Evidence.Game.ExecutablePath);
        Assert.Null(report.Binary);
        Assert.Contains(report.Evidence.Game.MetadataSources, e => e.Detail.Contains(Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.Ordinal));
        Assert.Equal(BinaryInspectionStatus.Unsupported, (await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path)).Status);
    }

    [Fact]
    public async Task Utf8CodePointSplitAcrossReadBuffersIsPreserved()
    {
        var raw = new string('a', 65535) + "😀\nVulkan error";
        var path = PathFor("unicode.log"); await File.WriteAllTextAsync(path, raw, new UTF8Encoding(false));
        Assert.Equal(raw, (await new KytyLogParser(new LocalFileSystem(), new LocalPathService()).ParseAsync(path)).RawText);
    }

    [Fact]
    public async Task AllSignatureMatchesRemainVisiblePastFormerCap()
    {
        var path = PathFor("failures.log"); await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("Shader error unsupported\n", 10002)));
        var log = await new KytyLogParser(new LocalFileSystem(), new LocalPathService()).ParseAsync(path);
        Assert.Equal(10002, new DiagnosisEngine().Diagnose(log).Matches.Length);
    }

    [Fact]
    public async Task KytyFatalBannerIsContextAndShaderPcIsRelative()
    {
        var path = PathFor("fatal.log");
        await File.WriteAllTextAsync(path, "--- Build ---\nexample build\n--- Fatal Error ---\nshader CFG build failed: unsupported decoded instruction at pc 0x0040 in ShaderCFG.cpp:42\n");
        var log = await new KytyLogParser(new LocalFileSystem(), new LocalPathService()).ParseAsync(path);
        var primary = new DiagnosisEngine().Diagnose(log).PrimaryFailure!;
        Assert.Equal(4, primary.LineNumber);
        Assert.Equal(42, primary.SourceLine);
        Assert.Equal(AddressSpace.RelativeAddress, primary.ProgramCounter!.AddressSpace);
        Assert.Equal("0x0040", primary.ProgramCounter.Value);
    }

    [Fact]
    public async Task RawZArchiveGameCanBeInspectedWithoutWholeGameExtraction()
    {
        var payload = ElfFixture.Create();
        var path = PathFor("game.zar"); await File.WriteAllBytesAsync(path, Zar(payload));
        Assert.Equal(["eboot.bin"], await ContainerInputs.ListAsync(path));
        var inspected = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path);
        Assert.Equal(BinaryInspectionStatus.Inspected, inspected.Status);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), inspected.Fingerprint.Sha256);
    }

    [Fact]
    public async Task MalformedZArchiveFooterAndTreeRejected()
    {
        var path = PathFor("broken.zar");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => ContainerInputs.ListAsync(path));
        var bytes = Zar([1]); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(65536 + 40 + 11 + 4), 0);
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ContainerInputs.ListAsync(path));
    }

    [Fact]
    public async Task StoredRar5GameIsInspectedThroughLibarchive()
    {
        var payload = ElfFixture.Create();
        var path = PathFor("game.rar");
        await File.WriteAllBytesAsync(path, Rar5("eboot.bin", payload));
        Assert.Equal(["eboot.bin"], await ContainerInputs.ListAsync(path));
        var binary = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path);
        Assert.Equal(BinaryInspectionStatus.Inspected, binary.Status);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), binary.Fingerprint.Sha256);
    }

    [Theory]
    [InlineData(".7z", "7zip")]
    [InlineData(".tar", "ustar")]
    [InlineData(".tar.gz", "ustar")]
    public async Task LibarchiveGameFormatsWorkWithCommonRelativeNames(string extension, string format)
    {
        var payload = ElfFixture.Create();
        await File.WriteAllBytesAsync(PathFor("eboot.bin"), payload);
        var archive = PathFor("game" + extension);
        var executable = Environment.GetEnvironmentVariable("THANOS_BSDTAR") ?? (OperatingSystem.IsLinux() ? "bsdtar" : "tar");
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { extension.EndsWith(".gz", StringComparison.Ordinal) ? "-czf" : "-cf", archive, "--format", format, "./eboot.bin" }) { info.ArgumentList.Add(arg); }
        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        var binary = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(archive);
        Assert.Equal(BinaryInspectionStatus.Inspected, binary.Status);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), binary.Fingerprint.Sha256);
    }

    private static byte[] Rar5(string name, byte[] payload)
    {
        static byte[] Vint(uint value)
        {
            var bytes = new List<byte>();
            do { bytes.Add((byte)((value & 127) | (value > 127 ? 128u : 0))); value >>= 7; } while (value != 0);
            return bytes.ToArray();
        }
        static byte[] Header(byte[] fields)
        {
            var body = Vint((uint)fields.Length).Concat(fields).ToArray();
            uint crc = uint.MaxValue;
            foreach (var value in body) { crc ^= value; for (var bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); } }
            var result = new byte[body.Length + 4]; BinaryPrimitives.WriteUInt32LittleEndian(result, ~crc); body.CopyTo(result, 4); return result;
        }
        var encoded = Encoding.UTF8.GetBytes(name);
        var fields = new byte[] { 2, 2 }.Concat(Vint((uint)payload.Length)).Concat(new byte[] { 0 }).Concat(Vint((uint)payload.Length))
            .Concat(Vint(0x81a4)).Concat(new byte[] { 0, 1 }).Concat(Vint((uint)encoded.Length)).Concat(encoded).ToArray();
        return new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0 }.Concat(Header([1, 0, 0])).Concat(Header(fields)).Concat(payload).Concat(Header([5, 0, 0])).ToArray();
    }

    private static byte[] Zar(byte[] payload)
    {
        var names = new byte[] { 0, 9 }.Concat(Encoding.UTF8.GetBytes("eboot.bin")).ToArray();
        var bytes = new byte[65536 + 40 + names.Length + 32 + 144];
        payload.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(65536 + 8), 65535);
        names.CopyTo(bytes, 65536 + 40);
        var tree = 65536 + 40 + names.Length;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tree + 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tree + 8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tree + 16), 0x80000001);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tree + 24), (uint)payload.Length);
        var footer = bytes.Length - 144;
        var sections = new (ulong Offset, ulong Size)[] { (0, 65536), (65536, 40), (65576, (ulong)names.Length), ((ulong)tree, 32), (0, 0), (0, 0) };
        for (var i = 0; i < sections.Length; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(footer + 16 * i), sections[i].Offset);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(footer + 16 * i + 8), sections[i].Size);
        }
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(footer + 128), (ulong)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(footer + 136), 0x61bf3a01);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(footer + 140), 0x169f52d6);
        return bytes;
    }
}
