using Microsoft.Data.Sqlite;
using Thanos.Core;

namespace Thanos.Infrastructure.Storage;

public sealed class SqliteKnowledgeStore(string databasePath) : IResearchKnowledgeStore
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
        if (version > 2) { throw new InvalidDataException("Database schema is newer than this application."); }
        for (var next = version + 1; next <= 2; next++)
        {
            await using var schema = typeof(SqliteKnowledgeStore).Assembly.GetManifestResourceStream($"Thanos.Infrastructure.Storage.schema-v{next}.sql")
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

    public async Task SaveResearchAsync(string id, ResearchReport report, CancellationToken cancellationToken = default)
    {
        if (report.SchemaVersion != 1) { throw new InvalidDataException("Unsupported research schema version."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO research_reports(id,created_utc,schema_version,report_json) VALUES($id,$date,1,$json);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonData.Serialize(report));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ResearchReport?> GetResearchAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT report_json FROM research_reports WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : JsonData.Deserialize<ResearchReport>(json);
    }

    public async Task ImportKnowledgeAsync(KnowledgeBundle bundle, CancellationToken cancellationToken = default)
    {
        KnowledgeValidation.Validate(bundle);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        // Imports are additive. Existing IDs cannot silently overwrite evidence or verification status.
        foreach (var family in bundle.Families)
        {
            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT json FROM game_families WHERE id=$id";
            existing.Parameters.AddWithValue("$id", family.Id);
            if (await existing.ExecuteScalarAsync(cancellationToken) is string previous)
            {
                if (JsonData.Serialize(JsonData.Deserialize<GameFamily>(previous)) != JsonData.Serialize(family)) { throw new InvalidDataException("Existing family ID has different content/provenance. Import cannot overwrite it."); }
                continue; // Identical family records may be reused as dependencies by a later import.
            }
            await InsertAsync(connection, transaction, "INSERT INTO game_families(id,name,json,provenance_json) VALUES($id,$name,$json,$provenance)", cancellationToken,
                ("$id", family.Id), ("$name", family.Name), ("$json", JsonData.Serialize(family)), ("$provenance", JsonData.Serialize(family.Provenance)));
        }
        foreach (var binding in bundle.Bindings)
        {
            await InsertAsync(connection, transaction, "INSERT INTO family_bindings(executable_sha256,family_id,binding_json) VALUES($hash,$family,$json)", cancellationToken,
                ("$hash", binding.ExecutableSha256), ("$family", binding.GameFamilyId), ("$json", JsonData.Serialize(binding)));
        }
        foreach (var patch in bundle.PatchFamilies)
        {
            await InsertAsync(connection, transaction, "INSERT INTO patch_families(id,game_family_id,logical_fix,provenance_json,family_json) VALUES($id,$family,$fix,$provenance,$json)", cancellationToken,
                ("$id", patch.Id), ("$family", patch.GameFamilyId), ("$fix", patch.LogicalFix), ("$provenance", JsonData.Serialize(patch.Provenance)), ("$json", JsonData.Serialize(patch)));
        }
        foreach (var entry in bundle.Entries)
        {
            await InsertAsync(connection, transaction, "INSERT INTO knowledge_entries(id,family_id,kind,payload_json,provenance_json,status) VALUES($id,$family,$kind,$json,$provenance,$status)", cancellationToken,
                ("$id", entry.Id), ("$family", entry.GameFamilyId), ("$kind", entry.Kind), ("$json", JsonData.Serialize(entry)), ("$provenance", JsonData.Serialize(entry.Provenance)),
                ("$status", System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(entry.Provenance.Status.ToString())));
        }
        foreach (var signature in bundle.ErrorSignatures)
        {
            await InsertAsync(connection, transaction, "INSERT INTO error_signatures(id,signature_json) VALUES($id,$json)", cancellationToken, ("$id", signature.Id), ("$json", JsonData.Serialize(signature)));
        }
        foreach (var relationship in bundle.Relationships)
        {
            await InsertAsync(connection, transaction, "INSERT INTO family_relationships(left_build,right_build,relationship_json) VALUES($left,$right,$json)", cancellationToken,
                ("$left", relationship.LeftBuild), ("$right", relationship.RightBuild), ("$json", JsonData.Serialize(relationship)));
        }
        using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT (SELECT count(*) FROM game_families)+(SELECT count(*) FROM family_bindings)+(SELECT count(*) FROM patch_families)+(SELECT count(*) FROM knowledge_entries)+(SELECT count(*) FROM error_signatures)+(SELECT count(*) FROM family_relationships)";
        if (Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken)) > KnowledgeValidation.MaximumRecords) { throw new InvalidDataException("Import would exceed the supported 10000 knowledge records; transaction rolled back."); }
        transaction.Commit();
    }

    public async Task<KnowledgeBundle> ExportKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var bundle = new KnowledgeBundle
        {
            Families = await ReadAsync<GameFamily>(connection, transaction, "SELECT json FROM game_families ORDER BY id", cancellationToken),
            Bindings = await ReadAsync<FamilyBinding>(connection, transaction, "SELECT binding_json FROM family_bindings ORDER BY executable_sha256", cancellationToken),
            PatchFamilies = await ReadAsync<PatchFamily>(connection, transaction, "SELECT family_json FROM patch_families WHERE family_json IS NOT NULL ORDER BY id", cancellationToken),
            Entries = await ReadAsync<KnowledgeEntry>(connection, transaction, "SELECT payload_json FROM knowledge_entries ORDER BY id", cancellationToken),
            ErrorSignatures = await ReadAsync<ErrorSignature>(connection, transaction, "SELECT signature_json FROM error_signatures ORDER BY id", cancellationToken),
            Relationships = await ReadAsync<FamilyRelationship>(connection, transaction, "SELECT relationship_json FROM family_relationships ORDER BY left_build,right_build", cancellationToken)
        };
        KnowledgeValidation.Validate(bundle);
        transaction.Commit();
        return bundle;
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, string? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        foreach (var value in values) { command.Parameters.AddWithValue(value.Name, (object?)value.Value ?? DBNull.Value); }
        await command.ExecuteNonQueryAsync(token);
    }
    private static async Task<T[]> ReadAsync<T>(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<T>();
        while (await reader.ReadAsync(token))
        {
            if (result.Count == KnowledgeValidation.MaximumRecords) { throw new InvalidDataException("Knowledge export exceeds the supported record limit."); }
            var json = reader.GetString(0);
            if (json.Length > 16 * 1024 * 1024) { throw new InvalidDataException("Stored knowledge record exceeds 16 MiB."); }
            result.Add(JsonData.Deserialize<T>(json));
        }
        return result.ToArray();
    }
}
