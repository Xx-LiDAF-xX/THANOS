using Thanos.Core;
using Thanos.Infrastructure.Storage;
using Xunit;

namespace Thanos.Tests;

public class VerificationStoreTests
{
    private static VerificationResult CreateSampleResult(string id, string gameHash, string kytyHash) => new()
    {
        SchemaVersion = 1,
        Id = id,
        GameFamilyId = "CUSA00001",
        BinarySha256 = gameHash,
        KytySha256 = kytyHash,
        PatchId = null,
        Scenario = "BOOT",
        Level = 1,
        Outcome = TestOutcome.Pass,
        BaselineLog = null,
        CandidateLog = "[INFO] guest execution",
        Metrics = new Dictionary<string, double> { ["exit_code"] = 0, ["duration_ms"] = 120 },
        Provenance = new Provenance("Test", null, DateTimeOffset.UtcNow, "1.0", "1.0", "PC", VerificationStatus.ToolVerified)
    };

    [Fact]
    public async Task SaveVerificationAsync_PersistsAndRetrievesResult()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"thanos-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteKnowledgeStore(tempFile);
            await store.InitializeAsync();

            var gameHash = new string('3', 64);
            var kytyHash = new string('4', 64);
            var sample = CreateSampleResult("ver-01", gameHash, kytyHash);

            await store.SaveVerificationAsync("ver-01", sample);

            var retrieved = await store.GetVerificationAsync("ver-01");
            Assert.NotNull(retrieved);
            Assert.Equal("ver-01", retrieved.Id);
            Assert.Equal(gameHash, retrieved.BinarySha256);
            Assert.Equal(kytyHash, retrieved.KytySha256);
            Assert.Equal(TestOutcome.Pass, retrieved.Outcome);
            Assert.Equal(1, retrieved.Level);
            Assert.Equal("BOOT", retrieved.Scenario);
            Assert.Equal(0, retrieved.Metrics["exit_code"]);

            var list = await store.ListVerificationsAsync(gameHash);
            Assert.Single(list);
            Assert.Equal("ver-01", list[0].Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveVerificationAsync_EnforcesAdditiveIntegrity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"thanos-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteKnowledgeStore(tempFile);
            await store.InitializeAsync();

            var gameHash = new string('5', 64);
            var kytyHash = new string('6', 64);
            var sample = CreateSampleResult("ver-dup", gameHash, kytyHash);

            await store.SaveVerificationAsync("ver-dup", sample);

            // Re-saving an identical ID with conflicting outcome must fail
            var conflicting = sample with { Outcome = TestOutcome.Fail };
            await Assert.ThrowsAnyAsync<Exception>(() => store.SaveVerificationAsync("ver-dup", conflicting));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
