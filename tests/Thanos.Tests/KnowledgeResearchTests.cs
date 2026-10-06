using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thanos.Core;
using Thanos.Infrastructure;
using Thanos.Infrastructure.Storage;

namespace Thanos.Tests;

public sealed class KnowledgeResearchTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-knowledge-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "knowledge.db");
    private static Provenance Provenance => new("synthetic community fixture", "example author", DateTimeOffset.Parse("2026-10-06T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "test-build", "test-game", "test-hardware", VerificationStatus.CommunityReported);
    public KnowledgeResearchTests() => Directory.CreateDirectory(directory);
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    private static KnowledgeBundle Bundle(string family = "family", string entry = "entry") => new()
    {
        Families = [new GameFamily { Id = family, Name = "Synthetic Family", Provenance = Provenance }],
        Entries = [new KnowledgeEntry { Id = entry, Kind = "issue", Summary = "Synthetic unverified issue", ExecutableHashes = [new string('a', 64)], Provenance = Provenance }]
    };

    [Fact]
    public async Task MigrationPreservesExistingVersionOneReports()
    {
        await using (var connection = new SqliteConnection($"Data Source={Database}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            await using var resource = typeof(SqliteKnowledgeStore).Assembly.GetManifestResourceStream("Thanos.Infrastructure.Storage.schema-v1.sql")!;
            using var reader = new StreamReader(resource); command.CommandText = await reader.ReadToEndAsync(); await command.ExecuteNonQueryAsync();
        }
        var store = new SqliteKnowledgeStore(Database);
        var old = new AnalysisReport(1, new GameFingerprint(), null, null, ["old data"]);
        await store.SaveAnalysisAsync("old-id", old);
        await store.InitializeAsync(); await store.InitializeAsync();
        Assert.Equal(JsonData.Serialize(old), JsonData.Serialize(await store.GetAnalysisAsync("old-id")));
        await using var verify = new SqliteConnection($"Data Source={Database}"); await verify.OpenAsync();
        using var version = verify.CreateCommand(); version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, await version.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ImportExportPreservesStatusesAndPatchFamilyProvenance()
    {
        var store = new SqliteKnowledgeStore(Database); await store.InitializeAsync();
        var original = Bundle() with
        {
            Bindings = [new FamilyBinding(new string('a', 64), "family", Provenance)],
            PatchFamilies = [new PatchFamily { Id = "patch-family", GameFamilyId = "family", LogicalFix = "Reported logical fix; not tested", Provenance = Provenance }],
            ErrorSignatures = [new ErrorSignature(1, "community-signature", ["example"], ErrorCategory.Shader, "Unverified hypothesis", 0.2, Provenance)],
            Relationships = [new FamilyRelationship(1, "a", "b", RelationshipStrength.Unknown, [], 0, Provenance)]
        };
        await store.ImportKnowledgeAsync(original);
        var exported = await store.ExportKnowledgeAsync();
        Assert.Equal(JsonData.Serialize(original), JsonData.Serialize(exported));
        Assert.Equal(VerificationStatus.CommunityReported, exported.Entries[0].Provenance.Status);
        Assert.Equal(JsonData.Serialize(exported), JsonData.Serialize(KnowledgeJson.Parse(JsonData.Serialize(exported))));
    }

    [Fact]
    public async Task DuplicateEntryRollsBackEntireImport()
    {
        var store = new SqliteKnowledgeStore(Database); await store.InitializeAsync();
        await store.ImportKnowledgeAsync(Bundle());
        await Assert.ThrowsAsync<SqliteException>(() => store.ImportKnowledgeAsync(Bundle("second-family")));
        Assert.Equal("family", Assert.Single((await store.ExportKnowledgeAsync()).Families).Id);
    }

    [Fact]
    public async Task IdenticalFamiliesCanBeReferencedByIncrementalImportsButCannotBePromoted()
    {
        var store = new SqliteKnowledgeStore(Database); await store.InitializeAsync();
        var bundle = Bundle(); await store.ImportKnowledgeAsync(bundle);
        await store.ImportKnowledgeAsync(Bundle(entry: "next-entry"));
        Assert.Equal(2, (await store.ExportKnowledgeAsync()).Entries.Length);
        var promoted = bundle with { Families = [bundle.Families[0] with { Provenance = Provenance with { Status = VerificationStatus.ToolVerified } }], Entries = [] };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportKnowledgeAsync(promoted));
        Assert.Equal(VerificationStatus.CommunityReported, (await store.ExportKnowledgeAsync()).Families[0].Provenance.Status);
    }

    [Theory]
    [InlineData("missing-family")]
    [InlineData("no-provenance")]
    [InlineData("invalid-hash")]
    [InlineData("empty-signature")]
    [InlineData("confidence")]
    [InlineData("reserved-signature")]
    public void InvalidKnowledgeIsRejectedBeforeStorage(string problem)
    {
        var bundle = Bundle();
        bundle = problem switch
        {
            "missing-family" => bundle with { Bindings = [new FamilyBinding(new string('a', 64), "absent", Provenance)] },
            "no-provenance" => bundle with { Entries = [bundle.Entries[0] with { Provenance = null! }] },
            "invalid-hash" => bundle with { Entries = [bundle.Entries[0] with { ExecutableHashes = ["invalid"] }] },
            "empty-signature" => bundle with { ErrorSignatures = [new ErrorSignature(1, "custom", [], ErrorCategory.Unknown, "claim", 0, Provenance)] },
            "confidence" => bundle with { ErrorSignatures = [new ErrorSignature(1, "custom", ["error"], ErrorCategory.Unknown, "claim", 1.1, Provenance)] },
            _ => bundle with { ErrorSignatures = [DiagnosisEngine.BuiltInSignatures[0]] }
        };
        Assert.Throws<InvalidDataException>(() => KnowledgeValidation.Validate(bundle));
    }

    [Fact]
    public void ExternalJsonRequiresExplicitVersionsFieldsAndNoDuplicateProperties()
    {
        var valid = JsonData.Serialize(Bundle());
        Assert.Throws<InvalidDataException>(() => KnowledgeJson.Parse(valid.Replace("\"schemaVersion\": 1,", "", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => KnowledgeJson.Parse("{\"schemaVersion\":1,\"schemaVersion\":1}"));
        Assert.Throws<InvalidDataException>(() => KnowledgeJson.Parse(valid.Replace("\"titleIds\": [],", "", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => KnowledgeJson.Parse(valid.Replace("\"families\":", "\"runScript\":null,\"families\":", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => KnowledgeJson.Parse("{\"schemaVersion\":99}"));
    }

    [Fact]
    public async Task BoundedKnowledgeInputRejectsOversizedJson()
    {
        var path = Path.Combine(directory, "large.json"); await using (var file = File.Create(path)) { file.SetLength(KnowledgeJson.MaximumBytes + 1L); }
        await Assert.ThrowsAsync<InvalidDataException>(() => KnowledgeJson.ReadAsync(path));
    }

    [Fact]
    public async Task KnownIssuesRequireAllSelectorsAndUnverifiedFamilyDoesNotAssignIdentity()
    {
        var game = Path.Combine(directory, "game.elf"); await File.WriteAllBytesAsync(game, ElfFixture.Create());
        var hash = (await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(game)).Fingerprint.Sha256!;
        var log = Path.Combine(directory, "kyty.log"); await File.WriteAllTextAsync(log, "Shader fatal unsupported");
        var bundle = Bundle() with
        {
            Bindings = [new FamilyBinding(hash, "family", Provenance)],
            Entries = [new KnowledgeEntry { Id = "matching", Kind = "fix", GameFamilyId = "family", ExecutableHashes = [hash], SignatureIds = ["shader-unsupported"], Summary = "Community-reported fix", Provenance = Provenance },
                new KnowledgeEntry { Id = "wrong-hash", Kind = "fix", GameFamilyId = "family", ExecutableHashes = [new string('b', 64)], SignatureIds = ["shader-unsupported"], Summary = "Another executable", Provenance = Provenance }]
        };
        var result = await ServiceFactory.CreateResearchService().ResearchAsync(game, null, log, bundle);
        Assert.Equal("matching", Assert.Single(result.KnownIssues).Id);
        Assert.Null(result.Evidence.Game.GameFamily);
        Assert.Equal(VerificationStatus.CommunityReported, result.KnownIssues[0].Provenance.Status);
        Assert.Single(result.RelatedFamilies);
        var trusted = bundle with
        {
            Families = [bundle.Families[0] with { Provenance = Provenance with { Status = VerificationStatus.Confirmed } }],
            Bindings = [bundle.Bindings[0] with { Provenance = Provenance with { Status = VerificationStatus.Confirmed } }]
        };
        Assert.Equal("family", (await ServiceFactory.CreateResearchService().ResearchAsync(game, null, log, trusted)).Evidence.Game.GameFamily);
    }

    [Fact]
    public async Task ResearchSnapshotsRoundTripWithoutReplacingOldAnalysisIds()
    {
        var store = new SqliteKnowledgeStore(Database); await store.InitializeAsync();
        var report = new ResearchReport { Evidence = new AnalysisReport(1, new GameFingerprint(), null, null, []), Diagnosis = new DiagnosisEngine().Diagnose(null) };
        await store.SaveResearchAsync("research", report);
        Assert.Equal(JsonData.Serialize(report), JsonData.Serialize(await store.GetResearchAsync("research")));
        Assert.Null(await store.GetResearchAsync("missing"));
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveResearchAsync("research", report));
    }
}
