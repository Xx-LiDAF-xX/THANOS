namespace Thanos.Core;

public sealed class DiagnosisEngine : IDiagnosisEngine
{
    private static readonly Provenance PolicyProvenance = new("THANOS lexical diagnosis policy v1; heuristic, not a verified Kyty fix", null, new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), null, null, null, VerificationStatus.Unverified);
    public static IReadOnlyList<ErrorSignature> BuiltInSignatures { get; } = new ErrorSignature[]
    {
        new(1, "shader-unsupported", ["unsupported"], ErrorCategory.Shader, "The emulator shader translation layer may not support an instruction used by this build.", 0.6, PolicyProvenance),
        new(1, "vulkan-device-lost", ["VK_ERROR_DEVICE_LOST"], ErrorCategory.Vulkan, "The Vulkan device was lost; driver, device, emulator and preceding shader failures need investigation.", 0.5, PolicyProvenance),
        new(1, "vulkan-device-memory", ["VK_ERROR_OUT_OF_DEVICE_MEMORY"], ErrorCategory.Vulkan, "Graphics memory pressure or a resource allocation problem may be involved.", 0.6, PolicyProvenance),
        new(1, "loader-failed", ["failed"], ErrorCategory.Loader, "Executable loading may be blocked by file format, missing dependencies or a loader limitation.", 0.5, PolicyProvenance),
        new(1, "module-missing", ["missing"], ErrorCategory.Module, "A required module may be absent or could not be discovered by the installed emulator.", 0.5, PolicyProvenance),
        new(1, "syscall-unimplemented", ["unimplemented"], ErrorCategory.Syscall, "The installed emulator may lack a required syscall implementation.", 0.6, PolicyProvenance),
        new(1, "memory-access", ["access violation"], ErrorCategory.Memory, "A memory access failed; the log alone cannot identify the faulty game or emulator implementation.", 0.4, PolicyProvenance)
    };

    public DiagnosisReport Diagnose(ParsedLog? log, IReadOnlyList<ErrorSignature>? signatures = null)
    {
        signatures ??= BuiltInSignatures;
        KnowledgeValidation.ValidateSignatures(signatures);
        var failures = log?.Events.Where(e => e.Severity >= Severity.Error || IsTermination(e)).ToArray() ?? [];
        var matches = new List<SignatureMatch>();
        var matchLimitReached = false;
        foreach (var failure in failures)
        {
            foreach (var signature in signatures)
            {
                if (signature.Category != failure.Category || !signature.ContainsAll.All(s => failure.Message.Contains(s, StringComparison.OrdinalIgnoreCase))) { continue; }
                if (matches.Count == 10000) { matchLimitReached = true; break; }
                // Even trusted signature provenance does not establish this run's root cause.
                matches.Add(new SignatureMatch(signature.Id, failure.LineNumber, signature.Hypothesis, Math.Min(signature.Confidence, 0.7), signature.Provenance));
            }
            if (matchLimitReached) { break; }
        }
        var primary = failures.FirstOrDefault(e => !IsTermination(e)) ?? failures.FirstOrDefault();
        var secondary = new List<ErrorEvent>();
        var independent = new List<ErrorEvent>();
        var links = new List<CorrelationLink>();
        foreach (var later in failures.Where(e => primary is not null && e.LineNumber > primary.LineNumber))
        {
            if (primary is not null && later.LineNumber - primary.LineNumber <= 25 && IsPossibleCascade(primary, later))
            {
                secondary.Add(later);
                links.Add(new CorrelationLink(primary.LineNumber, later.LineNumber, "Possible downstream effect; temporal/category correlation does not prove causation", 0.4,
                    [new Evidence(log!.SourcePath, primary.RawLine, primary.LineNumber), new Evidence(log.SourcePath, later.RawLine, later.LineNumber)]));
            }
            else { independent.Add(later); }
        }
        var candidates = new List<RootCause>();
        foreach (var match in matches.Where(m => primary is not null && m.LineNumber == primary.LineNumber))
        {
            candidates.Add(new RootCause
            {
                Layer = Layer(primary!),
                Facts = [$"Log line {primary!.LineNumber}: {primary.Message}"],
                Evidence = [new Evidence(log!.SourcePath, primary.RawLine, primary.LineNumber), new Evidence(match.Provenance.Source, $"Signature {match.SignatureId}; provenance status {match.Provenance.Status}")],
                Hypotheses = [match.Hypothesis],
                Confidence = match.Confidence,
                Recommendation = "Reproduce with the same executable and Kyty hashes, inspect the relevant emulator diagnostics/source and collect a baseline. No game-byte modification is justified by this match alone."
            });
        }
        if (candidates.Count == 0)
        {
            candidates.Add(new RootCause
            {
                Layer = "UNKNOWN",
                Facts = primary is null ? [] : [primary.RawLine],
                Evidence = primary is null ? [] : [new Evidence(log!.SourcePath, primary.RawLine, primary.LineNumber)],
                Hypotheses = [],
                Confidence = 0,
                Recommendation = primary is null ? "Provide a failing Kyty log. Missing failures do not establish compatibility." : "No supported signature establishes a candidate. Collect earlier context and emulator diagnostics; do not infer a root cause from termination alone."
            });
        }
        return new DiagnosisReport
        {
            PrimaryFailure = primary,
            SecondaryFailures = secondary.ToArray(),
            IndependentFailures = independent.ToArray(),
            Matches = matches.ToArray(),
            Correlations = links.ToArray(),
            Candidates = candidates.ToArray(),
            Limitations = ["Primary failure is the earliest observed non-termination error candidate, not proven causality. Cascades are limited to 25 lines. Independent errors remain visible.", "Lexical signatures produce hypotheses, not verified fixes or compatibility results. Runtime addresses require an explicit load bias to map.", .. (matchLimitReached ? new[] { "Signature match inventory limited to 10000 entries; later matches omitted." } : Array.Empty<string>())]
        };
    }
    private static bool IsTermination(ErrorEvent e) => e.EventType == "Termination" || e.Message.Contains("process terminated", StringComparison.OrdinalIgnoreCase);
    private static bool IsPossibleCascade(ErrorEvent earlier, ErrorEvent later) => IsTermination(later) || (earlier.Category switch
    {
        ErrorCategory.Shader => later.Category is ErrorCategory.Shader or ErrorCategory.Graphics or ErrorCategory.Vulkan,
        ErrorCategory.Loader => later.Category is ErrorCategory.Loader or ErrorCategory.Module or ErrorCategory.Boot,
        ErrorCategory.Module => later.Category is ErrorCategory.Module or ErrorCategory.Loader,
        _ => later.Category == earlier.Category && earlier.Category != ErrorCategory.Unknown
    });
    private static string Layer(ErrorEvent e) => e.Category switch
    {
        ErrorCategory.Shader => "KYTY EMULATOR / SHADER",
        ErrorCategory.Syscall => "KYTY EMULATOR / SYSCALL",
        ErrorCategory.Vulkan => "VULKAN / GPU / DRIVER",
        ErrorCategory.Loader => "LOADER",
        ErrorCategory.Module => "MODULE",
        ErrorCategory.Memory => "MEMORY",
        _ => "UNKNOWN"
    };
}
