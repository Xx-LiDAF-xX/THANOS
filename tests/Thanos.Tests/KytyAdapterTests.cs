using Thanos.Core;
using Thanos.Infrastructure;
using Xunit;

namespace Thanos.Tests;

public class KytyAdapterTests
{
    private static KytyFingerprint CreateFingerprint(string version = "1.0.0", KytyCapability[]? capabilities = null) => new()
    {
        ExecutablePath = "C:/Emulators/Kyty.exe",
        Sha256 = new string('a', 64),
        FileSize = 1048576,
        Version = version,
        CapabilityStatus = "Detected",
        Capabilities = capabilities ?? []
    };

    [Fact]
    public void DefaultAdapter_GeneratesGameArgumentWithoutGuessedFlags()
    {
        var adapter = new DefaultKytyAdapter();
        var kyty = CreateFingerprint();
        Assert.True(adapter.Supports(kyty));

        var request = adapter.CreateRequest(kyty, "C:/Games/Title");
        Assert.Equal("C:/Emulators/Kyty.exe", request.Executable);
        Assert.Contains("--game", request.Arguments);
        Assert.Contains("C:/Games/Title", request.Arguments);
        // Ensure no speculative flags were guessed
        Assert.DoesNotContain("--headless", request.Arguments);
        Assert.DoesNotContain("--novideo", request.Arguments);
        Assert.DoesNotContain("--patch", request.Arguments);
    }

    [Fact]
    public void KytyPs5Adapter_SupportsPs5SignatureAndZarInput()
    {
        var ps5Cap = new KytyCapability("kyty-ps5", ["--game", ".zar"], new Evidence("source", "Review"));
        var kyty = CreateFingerprint("KytyPS5-v0.2.0", [ps5Cap]);
        var adapter = new KytyPs5Adapter();

        Assert.True(adapter.Supports(kyty));

        var request = adapter.CreateRequest(kyty, "C:/Games/game.zar");
        Assert.Equal(["--game", "C:/Games/game.zar"], request.Arguments);
    }

    [Fact]
    public void KytyPs5Adapter_RejectsUnsupportedArchiveTypes()
    {
        var ps5Cap = new KytyCapability("kyty-ps5", ["--game"], new Evidence("source", "Review"));
        var kyty = CreateFingerprint("KytyPS5-v0.2.0", [ps5Cap]);
        var adapter = new KytyPs5Adapter();

        // Kyty only supports .zar as archive, not zip or 7z directly
        var ex = Assert.Throws<InvalidOperationException>(() => adapter.CreateRequest(kyty, "C:/Games/game.zip"));
        Assert.Contains("Kyty does not accept .zip archives directly", ex.Message);
    }

    [Fact]
    public void KytyAdapterRegistry_SelectsAppropriateAdapter()
    {
        var registry = new KytyAdapterRegistry();
        var genericKyty = CreateFingerprint("generic-kyty");
        var ps5Kyty = CreateFingerprint("KytyPS5", [new KytyCapability("kyty-ps5", [], new Evidence("source", "Review"))]);

        var selectedGeneric = registry.Select(genericKyty);
        var selectedPs5 = registry.Select(ps5Kyty);

        Assert.IsType<DefaultKytyAdapter>(selectedGeneric);
        Assert.IsType<KytyPs5Adapter>(selectedPs5);
    }

    [Fact]
    public void Adapter_AppliesScenarioTimeoutAndEnvironment()
    {
        var adapter = new DefaultKytyAdapter();
        var kyty = CreateFingerprint();
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Stability) with
        {
            Timeout = TimeSpan.FromSeconds(42),
            Environment = new Dictionary<string, string> { ["KYTY_LOG_LEVEL"] = "debug" }
        };

        var request = adapter.CreateRequest(kyty, "C:/Games/Title", scenario);
        Assert.Equal(TimeSpan.FromSeconds(42), request.Timeout);
        Assert.True(request.Environment.ContainsKey("KYTY_LOG_LEVEL"));
        Assert.Equal("debug", request.Environment["KYTY_LOG_LEVEL"]);
    }
}
