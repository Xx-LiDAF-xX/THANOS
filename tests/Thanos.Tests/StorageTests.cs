using Microsoft.Data.Sqlite;
using Thanos.Core;
using Thanos.Infrastructure;
using Thanos.Infrastructure.Storage;

namespace Thanos.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-db-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "knowledge.db");
    public StorageTests() => Directory.CreateDirectory(directory);
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }

    [Fact]
    public async Task MigrationIsIdempotentAndReportRoundTripsIncludingRawLog()
    {
        var store = new SqliteKnowledgeStore(Database);
        await store.InitializeAsync();
        await store.InitializeAsync();
        var report = new AnalysisReport(1, new GameFingerprint(), null,
            new ParsedLog { SourcePath = "fixture", Sha256 = new string('a', 64), RawText = "raw\r\nlog", Events = [] }, ["partial"]);
        await store.SaveAnalysisAsync("id'; DROP TABLE analyses;--", report);
        var restored = await store.GetAnalysisAsync("id'; DROP TABLE analyses;--");
        Assert.Equal(JsonData.Serialize(report), JsonData.Serialize(restored));
        Assert.Null(await store.GetAnalysisAsync("missing"));
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAnalysisAsync("id'; DROP TABLE analyses;--", report));
    }

    [Fact]
    public async Task FutureDatabaseVersionIsRejected()
    {
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version=99;";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteKnowledgeStore(Database).InitializeAsync());
    }

    [Fact]
    public async Task ForeignKeyIntegrityIsEnforced()
    {
        await new SqliteKnowledgeStore(Database).InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={Database};Foreign Keys=True");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO titles(id,family_id,title_id) VALUES('a','nonexistent','title');";
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task JsonExportImportPreservesUnknownValues()
    {
        var original = new GameFingerprint();
        var imported = JsonData.Deserialize<GameFingerprint>(JsonData.Serialize(original));
        Assert.Null(imported.TitleId);
        Assert.Null(imported.Sha256);
        Assert.Equal(AnalysisStatus.Partial, imported.Status);
        Assert.Empty(imported.TitleIds);
        await Task.CompletedTask;
    }

    [Fact]
    public void UnknownJsonMembersRejected() => Assert.Throws<System.Text.Json.JsonException>(() => JsonData.Deserialize<GameFingerprint>("{\"schemaVersion\":1,\"executeScript\":\"bad\"}"));

    [Fact]
    public async Task FutureReportSchemaCannotBeSaved()
    {
        var store = new SqliteKnowledgeStore(Database);
        await store.InitializeAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAnalysisAsync("future", new AnalysisReport(2, new GameFingerprint(), null, null, [])));
    }
}
