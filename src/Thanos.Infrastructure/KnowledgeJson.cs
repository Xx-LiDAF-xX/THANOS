using System.Reflection;
using System.Text;
using System.Text.Json;
using Thanos.Core;

namespace Thanos.Infrastructure;

public static class KnowledgeJson
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public static async Task<KnowledgeBundle> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var input = new LocalFileSystem().OpenRead(path);
        if (input.Length > MaximumBytes) { throw new InvalidDataException("Knowledge JSON exceeds 16 MiB."); }
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumBytes) { throw new InvalidDataException("Knowledge JSON grew beyond 16 MiB."); }
            buffer.Write(chunk, 0, read);
        }
        return Parse(new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\ufeff'));
    }

    public static KnowledgeBundle Parse(string json)
    {
        if (json.Length > MaximumBytes) { throw new InvalidDataException("Knowledge JSON exceeds the supported size."); }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Knowledge JSON must be an object."); }
        CheckObject(root);
        RequireWireShape(root, typeof(KnowledgeBundle));
        RequireVersion(root);
        foreach (var name in new[] { "families", "bindings", "patchFamilies", "entries", "errorSignatures", "relationships" })
        {
            if (!root.TryGetProperty(name, out var collection) || collection.ValueKind != JsonValueKind.Array) { throw new InvalidDataException($"Knowledge requires an explicit {name} array."); }
            foreach (var item in collection.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Knowledge records must be objects."); } if (name != "bindings") { RequireVersion(item); } }
        }
        var bundle = JsonData.Deserialize<KnowledgeBundle>(json);
        KnowledgeValidation.Validate(bundle);
        return bundle;
    }
    private static void RequireVersion(JsonElement element)
    {
        if (!element.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var value) || value != 1) { throw new InvalidDataException("Knowledge requires explicit schemaVersion 1 on versioned records."); }
    }
    private static void CheckObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate JSON properties are not accepted."); }
                CheckObject(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) { CheckObject(item); } }
    }

    private static void RequireWireShape(JsonElement element, Type type, bool nullable = false)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!nullable) { throw new InvalidDataException("Required knowledge values cannot be null."); }
            return;
        }
        if (type.IsArray)
        {
            if (element.ValueKind != JsonValueKind.Array) { throw new InvalidDataException("Knowledge collection must be an array."); }
            foreach (var item in element.EnumerateArray()) { RequireWireShape(item, type.GetElementType()!); }
            return;
        }
        if (type == typeof(string) || type.IsValueType) { return; } // Scalar typing is enforced by System.Text.Json.
        if (element.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Knowledge record must be an object."); }
        var context = new NullabilityInfoContext();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!element.TryGetProperty(name, out var value)) { throw new InvalidDataException($"Missing knowledge property: {name}."); }
            RequireWireShape(value, property.PropertyType, context.Create(property).ReadState == NullabilityState.Nullable || Nullable.GetUnderlyingType(property.PropertyType) is not null);
        }
    }
}
