using System.Buffers.Binary;
using System.Security.Cryptography;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class GameFingerprinter(IFileSystem files, IPathService paths) : IGameFingerprinter
{
    public const int MaximumFiles = 10000;

    public async Task<GameFingerprint> FingerprintAsync(string input, CancellationToken cancellationToken = default)
    {
        var fullPath = paths.GetFullPath(input);
        if (!files.FileExists(fullPath) && !files.DirectoryExists(fullPath))
        {
            throw new FileNotFoundException("Game input does not exist.", fullPath);
        }
        if (files.IsSymbolicLink(fullPath))
        {
            throw new InvalidDataException("Select a real file or directory rather than a symbolic link.");
        }
        var limitations = new List<string>
        {
            "Game identity, family, version and engine are UNKNOWN without verified metadata.",
            "Fingerprinting collects accessible file hashes and ELF headers. Use binary-info/diagnose for structural analysis; no decryption is performed."
        };
        string? executable = fullPath;
        string[] modules = [];
        if (ContainerInputs.Split(fullPath).Member is null && ContainerInputs.IsArchive(fullPath))
        {
            var inventory = await ContainerInputs.ListAsync(fullPath, cancellationToken);
            var candidates = inventory.Where(p => Path.GetFileName(p).Equals("eboot.bin", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) { candidates = inventory.Where(p => p.EndsWith(".elf", StringComparison.OrdinalIgnoreCase)).ToArray(); }
            modules = inventory.Where(p => p.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prx", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
            executable = candidates.Length == 1 ? fullPath + "!/" + candidates[0] : null;
            limitations.Add("Archive member paths identify decompressed evidence. Members are spooled read-only; no archive paths are extracted into the game directory.");
            if (executable is null)
            {
                limitations.Add("No unique executable in archive. Select an exact member with archive.zip!/path/to/eboot.bin.");
                return new GameFingerprint { Modules = modules, Limitations = limitations.ToArray(), MetadataSources = [new Evidence(fullPath, "Archive member inventory; no executable selected")] };
            }
        }
        else if (files.DirectoryExists(fullPath))
        {
            var inventory = new List<string>();
            foreach (var file in files.EnumerateFiles(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (inventory.Count == MaximumFiles)
                {
                    limitations.Add("Directory inventory is limited to 10000 files and 32 levels.");
                    break;
                }
                inventory.Add(file);
            }
            var candidates = inventory.Where(p => paths.GetFileName(p).Equals("eboot.bin", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
            {
                candidates = inventory.Where(p => p.EndsWith(".elf", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            executable = candidates.Length == 1 ? candidates[0] : null;
            modules = inventory.Where(p => p.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prx", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetRelativePath(fullPath, p)).Order(StringComparer.Ordinal).ToArray();
            limitations.Add("Directory inventory excludes symbolic links; inaccessible entries are reported as errors. Metadata files are not parsed yet.");
            if (executable is null)
            {
                limitations.Add(candidates.Length == 0 ? "No unambiguous executable found; select a file explicitly." : "Multiple executable candidates found; select a file explicitly.");
                return new GameFingerprint { Modules = modules, Limitations = limitations.ToArray(), MetadataSources = [new Evidence(fullPath, "Directory inventory only")] };
            }
        }
        await using var stream = await files.OpenReadAsync(executable, cancellationToken);
        var length = stream.Length;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        stream.Position = 0;
        var header = new byte[64];
        var count = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
        if (fullPath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
        {
            var recognized = count >= 4 && header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, 0x43, 0x4e, 0x54 });
            return new GameFingerprint
            {
                FileSize = length,
                MetadataSources = [new Evidence(fullPath, $"Complete package SHA-256: {hash}"), new Evidence(fullPath, recognized ? "CNT package header magic observed; package contents are not authenticated." : "Unknown PKG header; package variant is unsupported.")],
                Limitations = ["PKG loaded as package evidence, not as an executable. Package filesystem/decryption is not implemented; executable hash, identity and addresses are UNKNOWN. Select an accessible extracted eboot.bin for code analysis."]
            };
        }
        var info = ElfHeader.Read(header.AsSpan(0, count), length);
        if (info.Limitation is not null)
        {
            limitations.Add(info.Limitation);
        }
        return new GameFingerprint
        {
            ExecutablePath = executable,
            Sha256 = hash,
            FileSize = length,
            Architecture = info.Architecture,
            EntryPoint = info.EntryPoint,
            ElfType = info.Type,
            Modules = modules,
            MetadataSources = [new Evidence(executable, "SHA-256 of complete accessible file/member"), new Evidence(executable, "ELF identification/header inspection"), .. (executable == fullPath ? Array.Empty<Evidence>() : new[] { new Evidence(fullPath, "Container from which the executable member was selected") })],
            Limitations = limitations.ToArray()
        };
    }
}

internal sealed record ElfHeader(string? Architecture, BinaryAddress? EntryPoint, string? Type, string? Limitation)
{
    public static ElfHeader Read(ReadOnlySpan<byte> data, long fileLength)
    {
        static ElfHeader Unknown(string message) => new(null, null, null, message);
        if (data.Length < 4 || !data[..4].SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }))
        {
            return Unknown("Content is not a raw ELF. It may be another format or protected; code accessibility is UNKNOWN.");
        }
        if (data.Length < 16 || data[4] is not (1 or 2) || data[5] is not (1 or 2) || data[6] != 1)
        {
            return Unknown("Malformed or unsupported ELF identification.");
        }
        var is64 = data[4] == 2;
        var minimum = is64 ? 64 : 52;
        if (data.Length < minimum || fileLength < minimum)
        {
            return Unknown("Truncated ELF header.");
        }
        var little = data[5] == 1;
        var type = Read16(data[16..], little);
        var machine = Read16(data[18..], little);
        var version = Read32(data[20..], little);
        var headerSize = Read16(data[(is64 ? 52 : 40)..], little);
        if (version != 1 || headerSize != minimum)
        {
            return Unknown("Invalid ELF header version or size.");
        }
        var entry = is64 ? Read64(data[24..], little) : Read32(data[24..], little);
        return new ElfHeader(machine switch { 62 => "x86_64", 3 => "x86", 183 => "arm64", 40 => "arm", _ => $"ELF machine {machine}" },
            new BinaryAddress(AddressSpace.VirtualAddress, $"0x{entry:x}"), $"0x{type:x4}", null);
    }
    private static ushort Read16(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
    private static uint Read32(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);
    private static ulong Read64(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt64LittleEndian(data) : BinaryPrimitives.ReadUInt64BigEndian(data);
}

public sealed class KytyCapabilityDetector(IGameFingerprinter fingerprints) : IKytyCapabilityDetector
{
    public async Task<KytyFingerprint> DetectAsync(string executable, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable) || ContainerInputs.IsArchive(executable))
        {
            throw new FileNotFoundException("Kyty executable must be a file.", executable);
        }
        var file = await fingerprints.FingerprintAsync(executable, cancellationToken);
        if (file.ExecutablePath is null || file.Sha256 is null) { throw new InvalidDataException("Kyty fingerprinting requires an executable file rather than a package."); }
        return new KytyFingerprint
        {
            ExecutablePath = file.ExecutablePath!,
            Sha256 = file.Sha256!,
            FileSize = file.FileSize,
            Architecture = file.Architecture,
            CapabilityStatus = "Unknown",
            Evidence = [new Evidence(file.ExecutablePath!, "Executable fingerprint only; not executed or authenticated as Kyty.")],
            Limitations = ["Build/version and capabilities are UNKNOWN. No launch arguments inferred. PE/Mach-O architecture detection and version-specific adapters are planned."]
        };
    }
}
