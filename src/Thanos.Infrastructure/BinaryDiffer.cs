using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class BinaryDiffer(IFileSystem files, IGameFingerprinter fingerprints) : IBinaryDiffer
{
    public const int MaximumRanges = 4096;
    public const long MaximumComparisonBytes = 256 * 1024 * 1024;
    public async Task<BinaryDifference> CompareAsync(string original, string candidate, CancellationToken cancellationToken = default)
    {
        var before = await fingerprints.FingerprintAsync(original, cancellationToken);
        var after = await fingerprints.FingerprintAsync(candidate, cancellationToken);
        if (before.ExecutablePath is null || after.ExecutablePath is null) { throw new InvalidDataException("Binary differences require explicit files."); }
        await using var left = files.OpenRead(before.ExecutablePath);
        await using var right = files.OpenRead(after.ExecutablePath);
        var ranges = new List<ChangedRange>();
        var maximum = Math.Max(left.Length, right.Length);
        var scanned = Math.Min(maximum, MaximumComparisonBytes);
        long changed = 0;
        long? start = null;
        var originalBytes = new List<byte>();
        var candidateBytes = new List<byte>();
        var leftBuffer = new byte[65536];
        var rightBuffer = new byte[65536];
        var omittedRanges = false;
        for (long offset = 0; offset < scanned;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = (int)Math.Min(leftBuffer.Length, scanned - offset);
            var leftCount = (int)Math.Min(size, Math.Max(0, left.Length - offset));
            var rightCount = (int)Math.Min(size, Math.Max(0, right.Length - offset));
            left.Position = Math.Min(offset, left.Length);
            right.Position = Math.Min(offset, right.Length);
            await left.ReadExactlyAsync(leftBuffer.AsMemory(0, leftCount), cancellationToken);
            await right.ReadExactlyAsync(rightBuffer.AsMemory(0, rightCount), cancellationToken);
            for (var index = 0; index < size; index++)
            {
                var differs = index >= leftCount || index >= rightCount || leftBuffer[index] != rightBuffer[index];
                if (differs)
                {
                    changed++;
                    start ??= offset + index;
                    if (originalBytes.Count < 64 && index < leftCount) { originalBytes.Add(leftBuffer[index]); }
                    if (candidateBytes.Count < 64 && index < rightCount) { candidateBytes.Add(rightBuffer[index]); }
                }
                else if (start is { } beginning)
                {
                    AddRange(beginning, offset + index);
                    start = null; originalBytes.Clear(); candidateBytes.Clear();
                }
            }
            offset += size;
        }
        if (start is { } finalStart) { AddRange(finalStart, scanned); }
        var truncated = scanned < maximum || omittedRanges;
        return new BinaryDifference(1, before.Sha256!, after.Sha256!, changed, ranges.ToArray(), truncated,
            ["Position-based differences do not identify the logical fix; insertions shift later offsets. Changed byte totals are limited to the scanned prefix. Byte previews are capped at 64 bytes; surrounding contexts at 16 bytes each side.", "Comparison scans at most 256 MiB and retains at most 4096 ranges. No patch or stable instruction signature is inferred."]);

        void AddRange(long beginning, long end)
        {
            if (ranges.Count == MaximumRanges) { omittedRanges = true; return; }
            ranges.Add(new ChangedRange(new BinaryAddress(AddressSpace.FileOffset, $"0x{beginning:x}"), Math.Max(0, Math.Min(end, left.Length) - beginning), Math.Max(0, Math.Min(end, right.Length) - beginning),
                Convert.ToHexStringLower(originalBytes.ToArray()), Convert.ToHexStringLower(candidateBytes.ToArray()), Context(left, beginning, end), Context(right, beginning, end)));
        }
    }
    private static string Context(Stream stream, long start, long end)
    {
        var prefixOffset = Math.Min(stream.Length, Math.Max(0, start - 16));
        var prefixCount = (int)Math.Max(0, Math.Min(start, stream.Length) - prefixOffset);
        var suffixOffset = Math.Min(end, stream.Length);
        var suffixCount = (int)Math.Min(16, stream.Length - suffixOffset);
        var prefix = new byte[prefixCount]; var suffix = new byte[suffixCount];
        stream.Position = prefixOffset; stream.ReadExactly(prefix);
        stream.Position = suffixOffset; stream.ReadExactly(suffix);
        return Convert.ToHexStringLower(prefix) + " | " + Convert.ToHexStringLower(suffix);
    }
}
