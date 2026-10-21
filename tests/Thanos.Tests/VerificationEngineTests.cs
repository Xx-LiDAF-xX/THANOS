using Thanos.Core;
using Xunit;

namespace Thanos.Tests;

public class VerificationEngineTests
{
    private readonly IVerificationEngine engine = new VerificationEngine();

    private static GameFingerprint CreateGame() => new()
    {
        ExecutablePath = "C:/Games/eboot.bin",
        Sha256 = new string('1', 64),
        FileSize = 2048,
        Architecture = "x86_64",
        Confidence = 1.0,
        Status = AnalysisStatus.Complete
    };

    private static KytyFingerprint CreateKyty() => new()
    {
        ExecutablePath = "C:/Emulators/Kyty.exe",
        Sha256 = new string('2', 64),
        FileSize = 1048576,
        Version = "0.2.0",
        CapabilityStatus = "Detected"
    };

    [Fact]
    public void Evaluate_CandidatePassesBootScenario_WithRequiredEvidence()
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot);
        var game = CreateGame();
        var kyty = CreateKyty();

        var candidateLog = "[INFO] runtimeLinker: mapping program\n[INFO] InitAll: subsystems initialized\n[INFO] guest execution started successfully\n";
        var result = engine.Evaluate(scenario, game, kyty, null, null, candidateLog);

        Assert.Equal(TestOutcome.Pass, result.Outcome);
        Assert.Equal((int)VerificationLevel.Boot, result.Level);
        Assert.Equal("BOOT", result.Scenario);
        Assert.Equal(game.Sha256, result.BinarySha256);
        Assert.Equal(kyty.Sha256, result.KytySha256);
        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void Evaluate_MissingRequiredEvidence_ReturnsInconclusive()
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot);
        var game = CreateGame();
        var kyty = CreateKyty();

        // Empty or unrelated log without boot proof
        var candidateLog = "[INFO] Some generic output without emulator milestones\n";
        var result = engine.Evaluate(scenario, game, kyty, null, null, candidateLog);

        Assert.Equal(TestOutcome.Inconclusive, result.Outcome);
        Assert.Equal((int)VerificationLevel.None, result.Level);
    }

    [Fact]
    public void Evaluate_CandidateReachesMenuWhileBaselineDiedAtBoot_ReturnsImproved()
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Menu);
        var game = CreateGame();
        var kyty = CreateKyty();

        var baselineLog = "[INFO] runtimeLinker: mapping program\n[FATAL] Assertion failed at line 42\n";
        var candidateLog = "[INFO] runtimeLinker: mapping program\n[INFO] title: splash screen\n[INFO] menu: main menu UI loaded\n[FATAL] Unhandled exception in audio\n";

        var result = engine.Evaluate(scenario, game, kyty, "patch-01", baselineLog, candidateLog);

        Assert.Equal(TestOutcome.Improved, result.Outcome);
        Assert.Equal((int)VerificationLevel.Menu, result.Level);
        Assert.True(result.Metrics["candidate_level"] > result.Metrics["baseline_level"]);
    }

    [Fact]
    public void Evaluate_CandidateCrashesEarlierThanBaseline_ReturnsRegressed()
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Menu);
        var game = CreateGame();
        var kyty = CreateKyty();

        var baselineLog = "[INFO] runtimeLinker: mapping program\n[INFO] title: splash screen\n[INFO] menu: main menu UI loaded\n";
        var candidateLog = "[FATAL] runtimeLinker: failed to load module\n";

        var result = engine.Evaluate(scenario, game, kyty, "patch-02", baselineLog, candidateLog);

        Assert.Equal(TestOutcome.Regressed, result.Outcome);
        Assert.True(result.Metrics["candidate_level"] < result.Metrics["baseline_level"]);
    }

    [Fact]
    public void Evaluate_CollectsAccurateMetrics()
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot);
        var game = CreateGame();
        var kyty = CreateKyty();

        var candidateLog = "[INFO] runtimeLinker: initialized\n[WARN] missing texture\n[INFO] guest execution\n";
        var execResult = new ProcessResult(0, "stdout", "stderr", false);

        var result = engine.Evaluate(scenario, game, kyty, null, null, candidateLog, execResult);

        Assert.Equal(0, result.Metrics["exit_code"]);
        Assert.Equal(1, result.Metrics["warning_count"]);
        Assert.Equal(0, result.Metrics["error_count"]);
        Assert.Equal(3, result.Metrics["candidate_lines"]);
    }

    [Fact]
    public async Task VerificationService_VerifyAsync_OfflineLogs_ProducesValidResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "thanos-service-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var gamePath = Path.Combine(tempDir, "eboot.bin");
            await File.WriteAllBytesAsync(gamePath, new byte[64]);

            var candidateLog = Path.Combine(tempDir, "candidate.log");
            await File.WriteAllTextAsync(candidateLog, "[INFO] runtimeLinker: mapping program\n[INFO] guest execution\n");

            var fs = new Thanos.Infrastructure.LocalFileSystem();
            var paths = new Thanos.Infrastructure.LocalPathService();
            var fingerprinter = new Thanos.Infrastructure.GameFingerprinter(fs, paths);
            var detector = new Thanos.Infrastructure.KytyCapabilityDetector(fingerprinter);
            var registry = new Thanos.Infrastructure.KytyAdapterRegistry();
            var runner = new Thanos.Infrastructure.StreamingProcessRunner();
            var txService = new Thanos.Infrastructure.PatchTransactionService(new Thanos.Infrastructure.PatchValidationService());
            var service = new Thanos.Infrastructure.VerificationService(fingerprinter, detector, registry, runner, engine, txService);

            var scenario = ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot);
            var result = await service.VerifyAsync(scenario, gamePath, candidateLogPath: candidateLog);

            Assert.Equal(TestOutcome.Pass, result.Outcome);
            Assert.Equal((int)VerificationLevel.Boot, result.Level);
            Assert.Equal(1, result.SchemaVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
