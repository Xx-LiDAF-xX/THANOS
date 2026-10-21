using System.Security.Cryptography;
using Thanos.Core;

namespace Thanos.Infrastructure;

internal sealed record PatchTarget(string Root, string Path, string RelativePath, bool RootIsDirectory);

public sealed class PatchValidationService : IPatchValidationService
{
    private readonly ElfBinaryAnalyzer binaries = ServiceFactory.CreateBinaryAnalyzer();

    public async Task<PatchValidationResult> ValidateAsync(PatchProposal patch, string gameInput, ResearchReport? research = null, CancellationToken cancellationToken = default)
    {
        patch = PatchImportService.Normalize(patch);
        var checks = new List<PatchCheck>();
        void Check(string name, bool passed, string detail, params Evidence[] evidence) => checks.Add(new PatchCheck(name, passed, true, detail, evidence));
        PatchTarget target;
        try
        {
            target = ResolveTarget(patch, gameInput);
            Check("Target file", true, "Resolved an explicit regular filesystem target inside the selected game root.", new Evidence(target.Path, target.RelativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Check("Target file", false, ex.Message);
            return Result(patch, null, null, null, checks);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetHash = await HashAsync(target.Path, cancellationToken);
        Check("Target SHA-256", targetHash == patch.Sha256, targetHash == patch.Sha256 ? "Exact target bytes match the proposal hash." : "PATCH REJECTED: target SHA-256 differs from the proposal.", new Evidence(target.Path, targetHash));

        var inspection = await binaries.InspectAsync(target.Path, cancellationToken);
        var elf = inspection.Status is BinaryInspectionStatus.Inspected or BinaryInspectionStatus.Limited && inspection.Format is "ELF32" or "ELF64";
        Check("ELF format", elf, elf ? $"Accessible {inspection.Format} structure passed guarded inspection ({inspection.Status})." : "PATCH REJECTED: target is not a trustworthy accessible ELF.", new Evidence(target.Path, string.Join("; ", inspection.Limitations)));
        var architecture = !string.IsNullOrWhiteSpace(inspection.Fingerprint.Architecture) && !inspection.Fingerprint.Architecture.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
        Check("Architecture", elf && architecture, elf && architecture ? $"Recognized ELF architecture: {inspection.Fingerprint.Architecture}. The proposal schema has no separate architecture claim." : "PATCH REJECTED: architecture could not be established.");

        ValidateIdentity(patch, research, targetHash, checks);

        BinaryAddress? mapped = null;
        ulong offset = 0;
        if (elf)
        {
            if (patch.Location.AddressSpace == AddressSpace.FileOffset)
            {
                offset = AddressMapper.Parse(patch.Location);
                mapped = new BinaryAddress(AddressSpace.FileOffset, $"0x{offset:x}");
            }
            else if (patch.Location.AddressSpace == AddressSpace.VirtualAddress)
            {
                var result = new AddressMapper().Map(inspection, patch.Location, AddressSpace.FileOffset);
                mapped = result.Output;
                if (mapped is not null) { offset = AddressMapper.Parse(mapped); }
            }
        }
        Check("Address mapping", mapped is not null, mapped is null ? "PATCH REJECTED: location has no unique file-backed mapping. Runtime and relative addresses require evidence not present in PatchProposal." : $"Mapped target to exact file offset {mapped.Value}.", mapped is null ? [] : [new Evidence(target.Path, $"{patch.Location.AddressSpace} {patch.Location.Value} -> {mapped.Value}")]);

        var original = PatchImportService.Bytes(patch.OriginalBytes, "originalBytes");
        var replacement = PatchImportService.Bytes(patch.ReplacementBytes, "replacementBytes");
        var rangeValid = mapped is not null && offset <= (ulong)new FileInfo(target.Path).Length && (ulong)original.Length <= (ulong)new FileInfo(target.Path).Length - offset;
        if (rangeValid)
        {
            var inMapping = inspection.LoadMappings.Any(m =>
            {
                var start = AddressMapper.Parse(m.Offset);
                return offset >= start && (ulong)m.FileSize >= (offset - start) + (ulong)original.Length;
            });
            rangeValid = inMapping;
        }
        Check("Modification range", rangeValid, rangeValid ? "The equal-length replacement is wholly inside one file-backed PT_LOAD segment." : "PATCH REJECTED: modification is out of bounds or not wholly file-backed.");

        var originalMatches = false;
        if (rangeValid)
        {
            var actual = await ReadAsync(target.Path, offset, original.Length, cancellationToken);
            originalMatches = actual.SequenceEqual(original);
        }
        Check("Original bytes", originalMatches, originalMatches ? "Original bytes match exactly at the mapped location." : "PATCH REJECTED: original bytes do not match at the proposed location.");

        var pattern = PatchImportService.ParsePattern(patch.Signature, true, "signature");
        var signatureMatches = await CountMatchesAsync(target.Path, pattern, patch.ExpectedMatches, cancellationToken);
        Check("Signature match count", signatureMatches == patch.ExpectedMatches, signatureMatches == patch.ExpectedMatches ? $"Signature match count is exactly {signatureMatches}." : $"PATCH REJECTED: expected {patch.ExpectedMatches} signature matches; observed {(signatureMatches > patch.ExpectedMatches ? $"at least {signatureMatches}" : signatureMatches)}.");
        var atLocation = mapped is not null && rangeValid && await MatchAtAsync(target.Path, offset, pattern, cancellationToken);
        Check("Signature location", atLocation, atLocation ? "The signature starts at the explicit mapped patch location." : "PATCH REJECTED: the signature does not match at the explicit patch location; nearby matches are never substituted.");

        Check("Replacement bytes", !replacement.SequenceEqual(original), !replacement.SequenceEqual(original) ? "Replacement is an equal-length, explicit byte sequence distinct from the original." : "PATCH REJECTED: replacement is identical to original bytes.");
        return Result(patch, target.Path, targetHash, mapped, checks);
    }

    internal static PatchTarget ResolveTarget(PatchProposal patch, string gameInput)
    {
        var input = Path.GetFullPath(gameInput);
        if (ContainerInputs.Split(gameInput).Member is not null || ContainerInputs.IsArchive(input)) { throw new InvalidDataException("Patch transactions require an accessible filesystem file/folder, not an archive member or container."); }
        if (Directory.Exists(input))
        {
            EnsureNoReparse(input, input);
            var target = Path.GetFullPath(Path.Combine(input, patch.TargetFile.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(input, target)) { throw new InvalidDataException("Patch target escapes the selected game directory."); }
            if (!File.Exists(target)) { throw new InvalidDataException("Patch target file is absent from the selected game directory."); }
            EnsureNoReparse(input, target);
            return new PatchTarget(input, target, Path.GetRelativePath(input, target).Replace('\\', '/'), true);
        }
        if (!File.Exists(input)) { throw new FileNotFoundException("Selected game input was not found.", input); }
        if (!Path.GetFileName(input).Equals(Path.GetFileName(patch.TargetFile), PathComparison)) { throw new InvalidDataException("Explicit target filename does not match patch targetFile."); }
        EnsureNoReparse(Path.GetDirectoryName(input)!, input);
        return new PatchTarget(input, input, Path.GetFileName(input), false);
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
    }

    private static void ValidateIdentity(PatchProposal patch, ResearchReport? research, string hash, List<PatchCheck> checks)
    {
        void Add(string name, bool pass, string detail) => checks.Add(new PatchCheck(name, pass, true, detail, []));
        if (research is not null)
        {
            Add("Research target identity", research.Evidence.Game.Sha256 == hash, research.Evidence.Game.Sha256 == hash ? "Research fingerprint matches the exact selected target." : "PATCH REJECTED: research fingerprint does not match the selected target.");
        }
        else { Add("Research target identity", true, "No research report supplied; exact target SHA-256 remains the binary identity."); }

        var familyClaim = !patch.GameFamily.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
        Add("Game family", !familyClaim || research?.Evidence.Game.GameFamily == patch.GameFamily,
            !familyClaim ? "Patch family identity is explicitly UNKNOWN; no family inference was made." : research?.Evidence.Game.GameFamily == patch.GameFamily ? "Exact research game family matches." : "PATCH REJECTED: asserted game family is not established by matching research evidence.");

        var titles = research is null ? [] : research.Evidence.Game.TitleIds.Append(research.Evidence.Game.TitleId).Where(v => v is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        Add("Title identity", patch.TitleIds.Length == 0 || patch.TitleIds.Any(titles.Contains), patch.TitleIds.Length == 0 ? "Proposal has no title-ID selector; exact hash remains authoritative." : patch.TitleIds.Any(titles.Contains) ? "At least one explicit title ID matches research." : "PATCH REJECTED: no patch title ID matches research.");
        var versions = research is null ? [] : new[] { research.Evidence.Game.GameVersion, research.Evidence.Game.BuildVersion }.Where(v => v is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        Add("Game version", patch.GameVersions.Length == 0 || patch.GameVersions.Any(versions.Contains), patch.GameVersions.Length == 0 ? "Proposal has no game-version selector; exact hash remains authoritative." : patch.GameVersions.Any(versions.Contains) ? "Explicit game/build version matches research." : "PATCH REJECTED: no patch game version matches research.");
        var kytyMatch = patch.KytyVersions.Length == 0 || research?.Evidence.Kyty?.Version is { } version && patch.KytyVersions.Contains(version, StringComparer.Ordinal);
        Add("Kyty version", kytyMatch, patch.KytyVersions.Length == 0 ? "Proposal has no Kyty-version selector." : kytyMatch ? "Explicit Kyty version matches research." : "PATCH REJECTED: no patch Kyty version matches research.");
    }

    private static PatchValidationResult Result(PatchProposal patch, string? path, string? hash, BinaryAddress? offset, List<PatchCheck> checks) => new()
    {
        PatchId = patch.PatchId,
        Status = checks.All(c => !c.Critical || c.Passed) ? PatchValidationStatus.Valid : PatchValidationStatus.Rejected,
        TargetPath = path,
        TargetSha256 = hash,
        FileOffset = offset,
        Checks = checks.ToArray(),
        Limitations = ["Validation proves the selected bytes match this proposal. It does not prove root cause, compatibility, safety or successful gameplay.", "No nearby offset, alternate signature match or missing identity/version was guessed."]
    };

    private static async Task<byte[]> ReadAsync(string path, ulong offset, int count, CancellationToken token)
    {
        var result = new byte[count];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.Position = checked((long)offset);
        await stream.ReadExactlyAsync(result, token);
        return result;
    }

    private static async Task<bool> MatchAtAsync(string path, ulong offset, byte?[] pattern, CancellationToken token)
    {
        if (offset > (ulong)new FileInfo(path).Length || (ulong)pattern.Length > (ulong)new FileInfo(path).Length - offset) { return false; }
        var bytes = await ReadAsync(path, offset, pattern.Length, token);
        return Match(bytes, 0, pattern);
    }

    private static async Task<long> CountMatchesAsync(string path, byte?[] pattern, int expected, CancellationToken token)
    {
        const int chunkSize = 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var chunk = new byte[chunkSize];
        byte[] overlap = [];
        long total = 0, nextStart = 0, matches = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            token.ThrowIfCancellationRequested();
            var data = new byte[overlap.Length + read];
            overlap.CopyTo(data, 0); Array.Copy(chunk, 0, data, overlap.Length, read);
            var baseOffset = total - overlap.Length;
            var lastStart = baseOffset + data.Length - pattern.Length;
            for (var start = Math.Max(nextStart, baseOffset); start <= lastStart; start++)
            {
                if (Match(data, checked((int)(start - baseOffset)), pattern) && ++matches > expected) { return matches; }
            }
            nextStart = Math.Max(nextStart, lastStart + 1);
            total += read;
            var keep = Math.Min(pattern.Length - 1, data.Length);
            overlap = keep == 0 ? [] : data[^keep..];
        }
        return matches;
    }

    private static bool Match(byte[] bytes, int start, byte?[] pattern)
    {
        for (var i = 0; i < pattern.Length; i++) { if (pattern[i] is { } expected && bytes[start + i] != expected) { return false; } }
        return true;
    }

    internal static void EnsureNoReparse(string root, string target)
    {
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Symbolic links/reparse points are not accepted for patch targets."); }
        var relative = Path.GetRelativePath(current, target);
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current)) { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Symbolic links/reparse points are not accepted for patch targets."); } }
        }
    }

    internal static bool Within(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
