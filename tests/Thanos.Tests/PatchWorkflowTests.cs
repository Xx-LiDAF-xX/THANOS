using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Tests;

public sealed class PatchWorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-patch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PatchImportService importer = new();
    private readonly PatchValidationService validator = new();
    private readonly PatchTransactionService transactions;

    public PatchWorkflowTests()
    {
        Directory.CreateDirectory(directory);
        transactions = new PatchTransactionService(validator);
    }

    public void Dispose() => Directory.Delete(directory, true);

    private async Task<(string Root, string Target, byte[] Bytes, PatchProposal Patch)> FixtureAsync(bool folder = true, AddressSpace space = AddressSpace.FileOffset)
    {
        var bytes = ElfFixture.Create();
        var root = folder ? Path.Combine(directory, "game-" + Guid.NewGuid().ToString("N")) : directory;
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "eboot.bin");
        await File.WriteAllBytesAsync(target, bytes);
        if (folder) { await File.WriteAllTextAsync(Path.Combine(root, "unchanged.dat"), "preserve me"); }
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var location = space == AddressSpace.VirtualAddress ? "0x400110" : space == AddressSpace.RuntimeAddress ? "0x500110" : space == AddressSpace.RelativeAddress ? "0x10" : "0x110";
        var patch = new PatchProposal
        {
            PatchId = "synthetic-patch",
            PatchFamily = "synthetic-fix",
            GameFamily = "UNKNOWN",
            TargetFile = "eboot.bin",
            Sha256 = hash,
            Signature = "90 91 92 93",
            OriginalBytes = "90 91",
            ReplacementBytes = "aa bb",
            Location = new BinaryAddress(space, location),
            ExpectedMatches = 2,
            Reason = "Synthetic validation",
            Risk = "Synthetic only",
            VerificationRules = ["Reinspect exact bytes"],
            Provenance = new Provenance("external-ai synthetic fixture", null, DateTimeOffset.Parse("2026-10-10T00:00:00+00:00"), null, null, null, VerificationStatus.Unverified)
        };
        return (folder ? root : target, target, bytes, patch);
    }

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task JsonAndMarkdownNormalizeWithoutExecutingContent()
    {
        var fixture = await FixtureAsync();
        var json = JsonData.Serialize(fixture.Patch with { Signature = "90 91 ?? 93", OriginalBytes = "90 91", ReplacementBytes = "AA BB" });
        var direct = await importer.ImportAsync(await WriteAsync("patch.json", json));
        Assert.Equal(PatchImportStatus.Normalized, direct.Status);
        Assert.Equal("aa bb", direct.Patch!.ReplacementBytes);
        Assert.Equal("90 91 ?? 93", direct.Patch.Signature);
        var markdown = await importer.ImportAsync(await WriteAsync("answer.md", "Do not run this\n```json\n" + json + "\n```\nmore prose"));
        Assert.Equal(PatchImportStatus.Normalized, markdown.Status);
        Assert.Equal("markdown-json", markdown.DetectedFormat);
        Assert.Equal(JsonData.Serialize(direct.Patch), JsonData.Serialize(markdown.Patch));
    }

    [Theory]
    [InlineData("tool.py", "open('eboot.bin','wb').write(b'bad')", "script")]
    [InlineData("change.diff", "--- a/file\n+++ b/file\n@@ -1 +1 @@", "source-diff")]
    public async Task CodeAndDiffRemainInspectOnly(string name, string content, string format)
    {
        var result = await importer.ImportAsync(await WriteAsync(name, content));
        Assert.Equal(PatchImportStatus.InspectionRequired, result.Status);
        Assert.Equal(format, result.DetectedFormat);
        Assert.Null(result.Patch);
        Assert.Contains(result.Limitations, l => l.Contains("never executes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BinaryPatchRemainsInspectOnly()
    {
        var path = Path.Combine(directory, "change.ips");
        await File.WriteAllBytesAsync(path, [0x50, 0x41, 0x54, 0x43, 0x48]);
        var result = await importer.ImportAsync(path);
        Assert.Equal(PatchImportStatus.InspectionRequired, result.Status);
        Assert.Equal("binary-patch", result.DetectedFormat);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("traversal")]
    [InlineData("verified")]
    [InlineData("resize")]
    [InlineData("same")]
    public async Task UnsafeOrMalformedPatchJsonIsRejected(string fault)
    {
        var fixture = await FixtureAsync();
        var node = JsonNode.Parse(JsonData.Serialize(fixture.Patch))!;
        switch (fault)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "unknown": node["command"] = "run me"; break;
            case "traversal": node["targetFile"] = "../eboot.bin"; break;
            case "verified": node["provenance"]!["status"] = "confirmed"; break;
            case "resize": node["replacementBytes"] = "aa"; break;
            case "same": node["replacementBytes"] = "90 91"; break;
        }
        var text = node.ToJsonString();
        if (fault == "duplicate") { text = text.Insert(1, "\"patchId\":\"duplicate\","); }
        var result = await importer.ImportAsync(await WriteAsync("invalid-" + fault + ".json", text));
        Assert.Equal(PatchImportStatus.Rejected, result.Status);
        Assert.Null(result.Patch);
    }

    [Theory]
    [InlineData(AddressSpace.FileOffset)]
    [InlineData(AddressSpace.VirtualAddress)]
    public async Task ExactFileAndVirtualAddressPatchValidate(AddressSpace space)
    {
        var fixture = await FixtureAsync(space: space);
        var result = await validator.ValidateAsync(fixture.Patch, fixture.Root);
        Assert.Equal(PatchValidationStatus.Valid, result.Status);
        Assert.Equal("0x110", result.FileOffset!.Value);
        Assert.All(result.Checks, check => Assert.True(check.Passed, check.Name + ": " + check.Detail));
        Assert.Equal(fixture.Patch.Sha256, result.TargetSha256);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("signature")]
    [InlineData("bytes")]
    [InlineData("count")]
    [InlineData("runtime")]
    [InlineData("range")]
    [InlineData("title")]
    [InlineData("version")]
    public async Task CriticalMismatchRejectsWithoutTryingNearby(string fault)
    {
        var fixture = await FixtureAsync();
        var patch = fault switch
        {
            "hash" => fixture.Patch with { Sha256 = new string('a', 64) },
            "signature" => fixture.Patch with { Signature = "90 91 92 ff" },
            "bytes" => fixture.Patch with { OriginalBytes = "91 92" },
            "count" => fixture.Patch with { ExpectedMatches = 1 },
            "runtime" => fixture.Patch with { Location = new BinaryAddress(AddressSpace.RuntimeAddress, "0x500110") },
            "range" => fixture.Patch with { Location = new BinaryAddress(AddressSpace.FileOffset, "0x600") },
            "title" => fixture.Patch with { TitleIds = ["CUSA00001"] },
            "version" => fixture.Patch with { GameVersions = ["9.99"] },
            _ => fixture.Patch
        };
        var result = await validator.ValidateAsync(patch, fixture.Root);
        Assert.Equal(PatchValidationStatus.Rejected, result.Status);
        Assert.Contains(result.Checks, c => c.Critical && !c.Passed);
        Assert.Contains(result.Checks.Where(c => !c.Passed), c => c.Detail.Contains("PATCH REJECTED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResearchSelectorsMustMatchExactTargetContext()
    {
        var fixture = await FixtureAsync();
        var game = new GameFingerprint { Sha256 = fixture.Patch.Sha256, GameFamily = "family", TitleIds = ["CUSA00001"], GameVersion = "1.00" };
        var report = new ResearchReport { Evidence = new AnalysisReport(1, game, null, null, []), Diagnosis = new DiagnosisEngine().Diagnose(null) };
        var patch = fixture.Patch with { GameFamily = "family", TitleIds = ["CUSA00001"], GameVersions = ["1.00"] };
        Assert.Equal(PatchValidationStatus.Valid, (await validator.ValidateAsync(patch, fixture.Root, report)).Status);
        Assert.Equal(PatchValidationStatus.Rejected, (await validator.ValidateAsync(patch with { GameFamily = "other" }, fixture.Root, report)).Status);
        Assert.Equal(PatchValidationStatus.Rejected, (await validator.ValidateAsync(patch, fixture.Root, report with { Evidence = report.Evidence with { Game = game with { Sha256 = new string('b', 64) } } })).Status);
    }

    [Fact]
    public async Task ApplyCreatesBackupAndSeparateWorkingTreeThenRollbackRemovesWorking()
    {
        var fixture = await FixtureAsync();
        var transactionPath = Path.Combine(directory, "transaction");
        var applied = await transactions.ApplyAsync(fixture.Patch, fixture.Root, transactionPath, fixture.Patch.PatchId);
        Assert.Equal(PatchTransactionStatus.Applied, applied.Status);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Target));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(Path.Combine(transactionPath, "working", "unchanged.dat")));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(Path.Combine(transactionPath, "backup", "eboot.bin")));
        var candidate = await File.ReadAllBytesAsync(Path.Combine(transactionPath, "working", "eboot.bin"));
        Assert.Equal(0xaa, candidate[0x110]); Assert.Equal(0xbb, candidate[0x111]);
        Assert.NotEqual(applied.OriginalSha256, applied.CandidateSha256);
        Assert.True(File.Exists(Path.Combine(transactionPath, "patch.json")));
        var rolledBack = await transactions.RollbackAsync(transactionPath, applied.TransactionId);
        Assert.Equal(PatchTransactionStatus.RolledBack, rolledBack.Status);
        Assert.False(Directory.Exists(Path.Combine(transactionPath, "working")));
        Assert.True(File.Exists(Path.Combine(transactionPath, "backup", "eboot.bin")));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Target));
    }

    [Fact]
    public async Task ApprovalAndExistingDestinationAreRejectedWithoutMutation()
    {
        var fixture = await FixtureAsync();
        var wrong = Path.Combine(directory, "wrong-approval");
        await Assert.ThrowsAsync<InvalidDataException>(() => transactions.ApplyAsync(fixture.Patch, fixture.Root, wrong, "different"));
        Assert.False(Directory.Exists(wrong));
        var existing = Path.Combine(directory, "existing"); Directory.CreateDirectory(existing); await File.WriteAllTextAsync(Path.Combine(existing, "keep"), "safe");
        await Assert.ThrowsAsync<IOException>(() => transactions.ApplyAsync(fixture.Patch, fixture.Root, existing, fixture.Patch.PatchId));
        Assert.Equal("safe", await File.ReadAllTextAsync(Path.Combine(existing, "keep")));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Target));
    }

    [Fact]
    public async Task ExplicitRestorePreservesDisplacedOriginalAndRestoresBackup()
    {
        var fixture = await FixtureAsync();
        var transactionPath = Path.Combine(directory, "restore-transaction");
        var applied = await transactions.ApplyAsync(fixture.Patch, fixture.Root, transactionPath, fixture.Patch.PatchId);
        await File.WriteAllBytesAsync(fixture.Target, [1, 2, 3, 4]);
        var rolledBack = await transactions.RollbackAsync(transactionPath, applied.TransactionId, fixture.Root);
        Assert.Equal(PatchTransactionStatus.RolledBack, rolledBack.Status);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Target));
        var recovery = Directory.GetFiles(Path.Combine(transactionPath, "recovery"));
        Assert.Single(recovery);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(recovery[0]));
    }
}
