using System.Reflection;
using System.Text.Json;
using Thanos.Core;

namespace Thanos.Tests;

public sealed class SchemaContractTests
{
    public static TheoryData<Type, string> Models => new()
    {
        { typeof(GameFingerprint), "game" }, { typeof(GameFamily), "game-family" },
        { typeof(KytyFingerprint), "kyty-fingerprint" }, { typeof(ParsedLog), "parsed-log" },
        { typeof(ErrorEvent), "error-event" }, { typeof(RootCause), "diagnosis" },
        { typeof(PatchFamily), "patch-family" }, { typeof(PatchProposal), "patch" },
        { typeof(AIPrompt), "ai-prompt" }, { typeof(VerificationResult), "verification" },
        { typeof(ErrorSignature), "error-signature" }, { typeof(FamilyRelationship), "family-relationship" },
        { typeof(AnalysisReport), "analysis" }
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void WireModelPropertiesMatchVersionedSchema(Type model, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", name + "-schema-v1.json")));
        var properties = document.RootElement.GetProperty("properties");
        var wireNames = model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order().ToArray();
        Assert.Equal(wireNames, properties.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(1, properties.GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Assert.False(document.RootElement.GetProperty("additionalProperties").GetBoolean());
    }
}
