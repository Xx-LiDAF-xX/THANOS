using System.Text.Json;
using System.Text.Json.Serialization;
using Thanos.Core;

namespace Thanos.Infrastructure;

public static class JsonData
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("JSON document is null.");
}

public static class ServiceFactory
{
    public static IAnalysisService CreateAnalysisService()
    {
        var files = new LocalFileSystem();
        var paths = new LocalPathService();
        var fingerprints = new GameFingerprinter(files, paths);
        return new AnalysisService(fingerprints, new KytyCapabilityDetector(fingerprints), new KytyLogParser(files, paths));
    }
}
