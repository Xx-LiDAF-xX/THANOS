using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Thanos.Core;

namespace Thanos.Infrastructure;

public static class PromptExporter
{
    public static Task WriteTextAsync(AIPrompt prompt, string destination, CancellationToken cancellationToken = default) => AtomicAsync(destination,
        async stream => await stream.WriteAsync(Encoding.UTF8.GetBytes(prompt.Text), cancellationToken), cancellationToken);

    public static async Task<EvidencePackageManifest> WritePackageAsync(ResearchReport report, AIPrompt prompt, string destination, CancellationToken cancellationToken = default)
    {
        // Regenerate to reject an accidentally mismatched prompt/evidence pair.
        var expected = new PromptGenerator().Generate(report, prompt.Mode);
        if (expected.Text != prompt.Text || expected.Readiness != prompt.Readiness || !expected.MissingEvidence.SequenceEqual(prompt.MissingEvidence) || !expected.Evidence.SequenceEqual(prompt.Evidence))
        {
            throw new InvalidDataException("Prompt text does not correspond to the selected research report/mode.");
        }
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["prompt.md"] = Encoding.UTF8.GetBytes(prompt.Text),
            ["prompt.json"] = Encoding.UTF8.GetBytes(JsonData.Serialize(prompt)),
            ["research.json"] = Encoding.UTF8.GetBytes(JsonData.Serialize(report))
        };
        var manifest = new EvidencePackageManifest(1, "1", prompt.Mode,
            entries.Select(e => new PackageEntry(e.Key, Convert.ToHexStringLower(SHA256.HashData(e.Value)), e.Value.LongLength)).ToArray(),
            ["Evidence is supplied/collected data, not independently authenticated compatibility or patch validation.", "The archive contains complete prompt/research JSON and hashes of those serialized artifacts. It does not bundle game binaries or execute/import patches.", "Internal hashes detect byte mismatches; they do not authenticate a publisher. Review local paths and raw logs before sharing with an external AI."]);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(JsonData.Serialize(manifest));
        await AtomicAsync(destination, async stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var output = zip.CreateEntry(entry.Key, CompressionLevel.Optimal).Open();
                await output.WriteAsync(entry.Value, cancellationToken);
            }
        }, cancellationToken);
        return manifest;
    }

    private static async Task AtomicAsync(string destination, Func<Stream, Task> write, CancellationToken token)
    {
        var fullPath = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".thanos-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await write(stream);
                await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
