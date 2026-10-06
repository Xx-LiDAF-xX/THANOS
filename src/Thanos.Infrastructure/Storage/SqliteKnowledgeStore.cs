using Microsoft.Data.Sqlite;
using Thanos.Core;

namespace Thanos.Infrastructure.Storage;

public sealed class SqliteKnowledgeStore(string databasePath) : IKnowledgeStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), ForeignKeys = true }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        if (version > 1) { throw new InvalidDataException("Database schema is newer than this application."); }
        if (version == 0)
        {
            await using var schema = typeof(SqliteKnowledgeStore).Assembly.GetManifestResourceStream("Thanos.Infrastructure.Storage.schema-v1.sql")
                ?? throw new InvalidOperationException("Embedded SQLite schema missing.");
            using var reader = new StreamReader(schema);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = await reader.ReadToEndAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task SaveAnalysisAsync(string id, AnalysisReport report, CancellationToken cancellationToken = default)
    {
        if (report.SchemaVersion != 1) { throw new InvalidDataException("Unsupported analysis schema version."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO analyses(id,created_utc,schema_version,report_json) VALUES($id,$date,1,$json);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonData.Serialize(report));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AnalysisReport?> GetAnalysisAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT report_json FROM analyses WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : JsonData.Deserialize<AnalysisReport>(json);
    }
}
