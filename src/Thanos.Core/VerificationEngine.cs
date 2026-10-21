namespace Thanos.Core;

public sealed class VerificationEngine : IVerificationEngine
{
    public VerificationResult Evaluate(
        ScenarioDefinition scenario,
        GameFingerprint game,
        KytyFingerprint kyty,
        string? patchId,
        string? baselineLogText,
        string? candidateLogText,
        ProcessResult? candidateExecution = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(kyty);

        var candidateLines = SplitLines(candidateLogText);
        var baselineLines = SplitLines(baselineLogText);

        var candidateLevel = ClassifyLevel(candidateLines, candidateExecution);
        var baselineLevel = baselineLines.Length > 0 ? ClassifyLevel(baselineLines, null) : (int?)null;

        var hasCandidateProof = HasRequiredPatterns(candidateLines, scenario.RequiredPatterns);
        var hasCandidateFailure = HasAnyFailurePattern(candidateLines, scenario.FailurePatterns) || (candidateExecution?.ExitCode != null && candidateExecution.ExitCode != 0);

        var warningCount = CountOccurrences(candidateLines, "WARN");
        var errorCount = CountOccurrences(candidateLines, "ERR", "FATAL", "Exception", "panic");

        var outcome = DetermineOutcome(scenario, candidateLevel, baselineLevel, hasCandidateProof, hasCandidateFailure, candidateExecution);

        var metrics = new Dictionary<string, double>
        {
            ["candidate_level"] = candidateLevel,
            ["baseline_level"] = baselineLevel ?? -1,
            ["target_level"] = scenario.TargetLevel,
            ["warning_count"] = warningCount,
            ["error_count"] = errorCount,
            ["candidate_lines"] = candidateLines.Length,
            ["baseline_lines"] = baselineLines.Length,
            ["exit_code"] = candidateExecution?.ExitCode ?? (candidateExecution?.TimedOut == true ? -2 : 0)
        };

        var provenance = new Provenance(
            "THANOS VerificationEngine v1",
            "Automatic Scenario Evaluation",
            DateTimeOffset.UtcNow,
            kyty.Version,
            game.BuildVersion,
            null,
            outcome == TestOutcome.Pass ? VerificationStatus.ToolVerified : VerificationStatus.Unverified);

        var id = Guid.NewGuid().ToString("N");

        return new VerificationResult
        {
            SchemaVersion = 1,
            Id = id,
            GameFamilyId = game.TitleId ?? "UNKNOWN",
            BinarySha256 = (game.Sha256 ?? string.Empty).ToLowerInvariant(),
            KytySha256 = (kyty.Sha256 ?? string.Empty).ToLowerInvariant(),
            PatchId = patchId,
            Scenario = scenario.Name,
            Level = candidateLevel,
            Outcome = outcome,
            BaselineLog = baselineLogText,
            CandidateLog = candidateLogText,
            Metrics = metrics,
            Provenance = provenance
        };
    }

    private static int ClassifyLevel(string[] lines, ProcessResult? execution)
    {
        if (lines.Length == 0) return (int)VerificationLevel.None;

        bool hasBoot = ContainsAny(lines, "runtimeLinker", "InitAll", "guest execution", "LoadSelf", "Mounting");
        if (!hasBoot) return (int)VerificationLevel.None;

        int level = (int)VerificationLevel.Boot;

        bool hasIntro = ContainsAny(lines, "window created", "swapchain", "vkCreateSwapchain", "surface", "title", "splash");
        if (hasIntro) level = (int)VerificationLevel.Intro;

        bool hasMenu = ContainsAny(lines, "menu", "ui", "controller", "dialog", "scene");
        if (hasMenu) level = (int)VerificationLevel.Menu;

        bool hasGameplay = ContainsAny(lines, "gameplay", "world", "level", "player", "stage", "camera");
        if (hasGameplay) level = (int)VerificationLevel.InGame;

        bool hasPlayable = hasGameplay && !ContainsAny(lines, "FATAL", "Unhandled exception", "Assertion failed") && (execution == null || execution.ExitCode == 0);
        if (hasPlayable) level = (int)VerificationLevel.Playable;

        bool hasComplete = hasPlayable && ContainsAny(lines, "complete", "victory", "finished", "credits", "end of stage");
        if (hasComplete) level = (int)VerificationLevel.Complete;

        bool hasPerfect = hasComplete && !ContainsAny(lines, "WARN", "glitch", "drop");
        if (hasPerfect) level = (int)VerificationLevel.Perfect;

        return level;
    }

    private static TestOutcome DetermineOutcome(
        ScenarioDefinition scenario,
        int candidateLevel,
        int? baselineLevel,
        bool hasCandidateProof,
        bool hasCandidateFailure,
        ProcessResult? execution)
    {
        if (execution?.TimedOut == true && !hasCandidateProof)
        {
            return TestOutcome.Inconclusive;
        }

        if (baselineLevel.HasValue)
        {
            if (candidateLevel > baselineLevel.Value)
            {
                return TestOutcome.Improved;
            }
            if (candidateLevel < baselineLevel.Value)
            {
                return TestOutcome.Regressed;
            }
        }

        if (!hasCandidateProof && candidateLevel < scenario.TargetLevel)
        {
            return TestOutcome.Inconclusive;
        }

        if (!hasCandidateFailure && candidateLevel >= scenario.TargetLevel)
        {
            return TestOutcome.Pass;
        }

        if (hasCandidateFailure)
        {
            return TestOutcome.Fail;
        }

        return TestOutcome.Inconclusive;
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool ContainsAny(string[] lines, params string[] patterns)
    {
        foreach (var line in lines)
        {
            foreach (var p in patterns)
            {
                if (line.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static bool HasRequiredPatterns(string[] lines, string[] requiredPatterns)
    {
        if (requiredPatterns.Length == 0) return true;
        foreach (var req in requiredPatterns)
        {
            if (ContainsAny(lines, req)) return true;
        }
        return false;
    }

    private static bool HasAnyFailurePattern(string[] lines, string[] failurePatterns)
    {
        return ContainsAny(lines, failurePatterns);
    }

    private static int CountOccurrences(string[] lines, params string[] tokens)
    {
        int count = 0;
        foreach (var line in lines)
        {
            foreach (var token in tokens)
            {
                if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }
}
