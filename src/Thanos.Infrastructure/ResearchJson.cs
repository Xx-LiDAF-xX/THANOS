using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Thanos.Core;

namespace Thanos.Infrastructure;

/// <summary>Strict saved research import without applying the knowledge-file size limit to logs.</summary>
public static class ResearchJson
{
    public static async Task<ResearchReport> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
        var basic = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("game", out _);
        Shape(document.RootElement, basic ? typeof(AnalysisReport) : typeof(ResearchReport));
        var report = basic
            ? FromAnalysis(document.RootElement.Deserialize<AnalysisReport>(JsonData.Options) ?? throw new InvalidDataException("Analysis cannot be null."))
            : document.RootElement.Deserialize<ResearchReport>(JsonData.Options) ?? throw new InvalidDataException("Research cannot be null.");
        if (report.Binary is { } binary && binary.Fingerprint.Sha256 != report.Evidence.Game.Sha256) { throw new InvalidDataException("Binary evidence hash does not match the game fingerprint."); }
        if (report.Evidence.Log is { } log)
        {
            if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(log.RawText))) != log.Sha256) { throw new InvalidDataException("Raw log text does not match its SHA-256."); }
            using var lines = new StringReader(log.RawText.TrimStart('\ufeff'));
            var index = 0;
            while (lines.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index >= log.Events.Length || log.Events[index].LineNumber != index + 1 || log.Events[index].RawLine != line)
                {
                    throw new InvalidDataException("Log event inventory does not match its complete raw text and line order.");
                }
                index++;
            }
            if (index != log.Events.Length) { throw new InvalidDataException("Log contains extra events without matching raw lines."); }
        }
        if (report.Evidence.Log is { } sourceLog)
        {
            var failures = report.Diagnosis.SecondaryFailures.Concat(report.Diagnosis.IndependentFailures);
            if (report.Diagnosis.PrimaryFailure is { } primary) { failures = failures.Prepend(primary); }
            foreach (var failure in failures)
            {
                if (failure.LineNumber > sourceLog.Events.Length || sourceLog.Events[failure.LineNumber - 1] != failure) { throw new InvalidDataException("Diagnosis failures must match source log events."); }
            }
            if (report.Diagnosis.Matches.Any(m => m.LineNumber > sourceLog.Events.Length) || report.Diagnosis.Correlations.Any(c => c.FromLine > sourceLog.Events.Length || c.ToLine > sourceLog.Events.Length)) { throw new InvalidDataException("Diagnosis references lines outside the source log."); }
        }
        else if (report.Diagnosis.PrimaryFailure is not null || report.Diagnosis.SecondaryFailures.Length > 0 || report.Diagnosis.IndependentFailures.Length > 0 || report.Diagnosis.Matches.Length > 0 || report.Diagnosis.Correlations.Length > 0) { throw new InvalidDataException("Log diagnosis requires source log evidence."); }
        return report;
    }

    private static ResearchReport FromAnalysis(AnalysisReport analysis) => new()
    {
        Evidence = analysis,
        Diagnosis = new DiagnosisEngine().Diagnose(analysis.Log),
        Limitations = ["Imported basic analysis snapshot. Binary inspection, related builds and knowledge lookup were not performed."]
    };

    private static void Shape(JsonElement element, Type type, bool nullable = false, string? field = null)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!nullable) { throw new InvalidDataException("Required research values cannot be null."); }
            return;
        }
        if (type.IsArray)
        {
            if (element.ValueKind != JsonValueKind.Array) { throw new InvalidDataException("Expected a research array."); }
            foreach (var item in element.EnumerateArray()) { Shape(item, type.GetElementType()!, field: field); }
            return;
        }
        if (type == typeof(string))
        {
            if (element.ValueKind != JsonValueKind.String) { throw new InvalidDataException("Expected research text."); }
            if (field is "sha256" or "executableHashes") { KnowledgeValidation.Hash(element.GetString()!); }
            return;
        }
        if (type.IsValueType)
        {
            if (field is "fileSize" or "memorySize" or "size" or "sectionIndex" && (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var size) || size < 0)) { throw new InvalidDataException("Research sizes/indexes must be nonnegative integers."); }
            if (field is "lineNumber" or "sourceLine" or "fromLine" or "toLine" && (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var line) || line < 1)) { throw new InvalidDataException("Evidence lines must be positive integers."); }
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Expected a research object."); }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!keys.Add(property.Name)) { throw new InvalidDataException("Duplicate research JSON properties are not accepted."); }
            if (type != typeof(Dictionary<string, string>) && property.Name == "schemaVersion" && (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version) || version != 1)) { throw new InvalidDataException("Unsupported research schema version."); }
            if (type != typeof(Dictionary<string, string>) && property.Name == "confidence" && (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var confidence) || !double.IsFinite(confidence) || confidence is < 0 or > 1)) { throw new InvalidDataException("Confidence must be finite and in [0,1]."); }
        }
        if (type == typeof(Dictionary<string, string>))
        {
            foreach (var property in element.EnumerateObject()) { if (property.Value.ValueKind != JsonValueKind.String) { throw new InvalidDataException("Hardware values must be strings."); } }
            return;
        }
        var context = new NullabilityInfoContext();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!element.TryGetProperty(name, out var value)) { throw new InvalidDataException("Missing research property: " + name); }
            Shape(value, property.PropertyType, context.Create(property).ReadState == NullabilityState.Nullable || Nullable.GetUnderlyingType(property.PropertyType) is not null, name);
        }
    }
}
